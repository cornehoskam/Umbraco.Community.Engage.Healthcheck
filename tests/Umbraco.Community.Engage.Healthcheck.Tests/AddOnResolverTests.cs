using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.AddOns;
using Umbraco.Community.Engage.Healthcheck.Core.Comparison;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Schema;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

public sealed class AddOnResolverTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "addon_" + Guid.NewGuid().ToString("N"));
    private static readonly AddOn Forms = AddOnCatalog.Forms;

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static DatabaseSchema Schema(AddOn addOn) => SchemaBuilder.Db(SchemaBuilder.Table(addOn.Tables[0], [SchemaBuilder.Col("Id")]));

    private static readonly DatabaseSchema Installed = Schema(Forms);
    private static readonly DatabaseSchema NotInstalled = new([]);

    private void Write(AddOn addOn, string version, string step)
    {
        var manifest = new Manifest(1, addOn.Product, version, "fresh", "17.5.3", addOn.StateKey, step, null, Schema(addOn));
        var path = Path.Combine(_folder, Products.Folder(addOn.Product), ManifestStore.FileName(version, "fresh"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ManifestSerializer.Serialize(manifest));
    }

    private void WriteForms()
    {
        Write(Forms, "17.0.0", "T");
        Write(Forms, "17.1.0", "S");
        Write(Forms, "17.1.1", "S");
        Write(Forms, "17.1.2", "S");
    }

    private ManifestStore Store => new(_folder, includeEmbedded: false);

    [Fact]
    public void A_not_installed_is_null()
    {
        WriteForms();
        Assert.Null(AddOnResolver.Resolve(Forms, null, null, NotInstalled, Store));
    }

    [Fact]
    public void B_requested_version_is_used_and_detection_still_runs()
    {
        WriteForms();
        var r = AddOnResolver.Resolve(Forms, "17.1.2", "T", Installed, Store)!;
        Assert.Equal("17.1.2", r.Manifest!.Version);
        Assert.Equal("17.1.2", r.Version);
        Assert.Equal("option", r.Source);
        Assert.True(r.CheckState);
        Assert.Empty(r.Findings);
        Assert.Equal("17.0.0", r.Detection!.Group!.RangeText);
    }

    [Fact]
    public void C_unknown_requested_version_throws()
    {
        WriteForms();
        var ex = Assert.Throws<ManifestNotFoundException>(() => AddOnResolver.Resolve(Forms, "9.9.9", null, NotInstalled, Store));
        Assert.StartsWith("No manifest for Engage Forms 9.9.9. Available: ", ex.Message);
        Assert.Contains("17.1.2", ex.Message);
    }

    [Fact]
    public void D_step_matches_several_versions_newest_wins()
    {
        WriteForms();
        var r = AddOnResolver.Resolve(Forms, null, "S", Installed, Store)!;
        Assert.Equal("17.1.2", r.Manifest!.Version);
        Assert.Equal("17.1.2", r.Version);
        Assert.Equal("between 17.1.0 and 17.1.2", r.Detection!.Group!.RangeText);
        Assert.True(r.Detection.RecordMatchesSchema);
        Assert.Equal("detected", r.Source);
        Assert.True(r.CheckState);
        Assert.Empty(r.Findings);
    }

    [Fact]
    public void Unparseable_external_version_is_skipped()
    {
        WriteForms();
        Write(Forms, "not-a-version", "S");
        var r = AddOnResolver.Resolve(Forms, null, "S", Installed, Store)!;
        Assert.Equal("17.1.2", r.Manifest!.Version);
        Assert.Equal("between 17.1.0 and 17.1.2", r.Detection!.Group!.RangeText);
    }

    [Fact]
    public void E_single_match_shows_the_single_version()
    {
        WriteForms();
        var r = AddOnResolver.Resolve(Forms, null, "T", Installed, Store)!;
        Assert.Equal("17.0.0", r.Detection!.Group!.RangeText);
        Assert.Equal("17.0.0", r.Manifest!.Version);
    }

    [Fact]
    public void F_unknown_step_compares_against_the_schema_match_without_state_check()
    {
        WriteForms();
        var r = AddOnResolver.Resolve(Forms, null, "Z", Installed, Store)!;
        Assert.Equal("17.1.2", r.Manifest!.Version);
        Assert.False(r.CheckState);
        Assert.Equal("schema-only", r.Detection!.Basis);
        var f = Assert.Single(r.Findings);
        Assert.Equal(FindingKind.AddOnVersionUnknown, f.Kind);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal(Forms.StateKey, f.Object);
        Assert.Equal("Z", f.Actual);
        Assert.Equal("a known Engage Forms upgrade step", f.Expected);
        Assert.Equal(Products.Forms, f.Product);
    }

    [Fact]
    public void G_table_without_step_reports_missing()
    {
        WriteForms();
        var r = AddOnResolver.Resolve(Forms, null, null, Installed, Store)!;
        Assert.Equal("17.1.2", r.Manifest!.Version);
        Assert.False(r.CheckState);
        Assert.Equal("(missing)", Assert.Single(r.Findings).Actual);
    }

    [Fact]
    public void Unknown_step_and_no_schema_match_leaves_the_version_unknown()
    {
        WriteForms();
        var other = SchemaBuilder.Db(SchemaBuilder.Table(Forms.Tables[0], [SchemaBuilder.Col("Other")]));
        var r = AddOnResolver.Resolve(Forms, null, "Z", other, Store)!;
        Assert.Null(r.Version);
        Assert.False(r.Detection!.Exact);
        Assert.Equal(FindingKind.AddOnVersionUnknown, Assert.Single(r.Findings).Kind);
    }

    [Fact]
    public void H_installed_without_any_manifest()
    {
        WriteForms();
        // Commerce manifests are embedded in the real build, so switch embedded manifests off to get a product with none.
        var r = AddOnResolver.Resolve(AddOnCatalog.Commerce, null, "X", Schema(AddOnCatalog.Commerce), new ManifestStore(_folder, includeEmbedded: false))!;
        Assert.Null(r.Manifest);
        Assert.Null(r.Version);
        Assert.False(r.CheckState);
        var f = Assert.Single(r.Findings);
        Assert.Equal(FindingKind.AddOnVersionUnknown, f.Kind);
        Assert.Equal(Products.Commerce, f.Product);
    }

    [Fact]
    public void Only_the_given_majors_are_considered()
    {
        // Forms 16.x to 18.x end on the same step with an identical table.
        Write(Forms, "16.5.0", "S");
        WriteForms();
        Write(Forms, "18.0.0", "S");
        Write(Forms, "18.1.0", "S");

        var v17 = AddOnResolver.Resolve(Forms, null, "S", Installed, Store, majors: [17])!;
        Assert.Equal("between 17.1.0 and 17.1.2", v17.Detection!.Group!.RangeText);
        Assert.Empty(v17.Detection.Alternatives);
        Assert.Equal("17.1.2", v17.Version);

        Assert.Equal("between 18.0.0 and 18.1.0", AddOnResolver.Resolve(Forms, null, "S", Installed, Store, majors: [18])!.Detection!.Group!.RangeText);
    }

    [Fact]
    public void A_step_known_only_in_another_major_is_unknown_here()
    {
        Write(Forms, "18.0.0", "NEW");
        WriteForms();
        var r = AddOnResolver.Resolve(Forms, null, "NEW", Installed, Store, majors: [17])!;
        Assert.Equal(FindingKind.AddOnVersionUnknown, Assert.Single(r.Findings).Kind);
        Assert.Equal(17, r.Detection!.Group!.Major);
    }
}
