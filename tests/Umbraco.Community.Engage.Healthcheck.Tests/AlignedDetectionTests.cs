using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Reporting;
using Umbraco.Community.Engage.Healthcheck.Core.Schema;
using Umbraco.Community.Engage.Healthcheck.Core.State;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

/// <summary>Detection over the fresh and aligned groups together, with synthetic manifests only.</summary>
public class AlignedDetectionTests
{
    private const string BeforeGate = "{4e7a3b9c-8d2f-41e5-a6f7-5c1b3d9e2a8f}";

    private static readonly DatabaseSchema Fresh = SchemaBuilder.Baseline();

    // Aligned: the child keeps a nullable contentTypeId and has no check constraint, like a database CompleteAlignSchema.sql has not touched.
    private static readonly DatabaseSchema Aligned = SchemaBuilder.Db(Fresh.Tables
        .Select(t => t.Name == "umbracoEngageChild"
            ? t with { Columns = t.Columns.Select(c => c.Name == "contentTypeId" ? c with { IsNullable = true } : c).ToList(), CheckConstraints = [] }
            : t).ToArray());

    private static Manifest M(string version, string step, DatabaseSchema schema, string variant, string? status)
        => new(1, Products.Engage, version, variant, "17.5.3", MigrationState.StateKey, step, status, schema);

    private static IReadOnlyList<StateGroup> Groups(params Manifest[] manifests)
        => [.. VersionIndex.Build(manifests.Where(m => m.Variant == Variants.Fresh)), .. VersionIndex.Build(manifests.Where(m => m.Variant == Variants.Aligned))];

    private static readonly IReadOnlyList<StateGroup> V17 = Groups(
        M("17.2.0", BeforeGate, Fresh, Variants.Fresh, "Complete"),
        M("17.2.1", BeforeGate, Fresh, Variants.Fresh, "Complete"),
        M("17.2.0", BeforeGate, Aligned, Variants.Aligned, "Aligned"),
        M("17.2.1", BeforeGate, Aligned, Variants.Aligned, "Aligned"));

    [Fact]
    public void An_aligned_database_is_detected_as_the_aligned_variant()
    {
        var d = Detector.Detect(Products.Engage, Aligned, BeforeGate, V17, "Aligned");

        Assert.True(d.Exact);
        Assert.Equal(Variants.Aligned, d.Group!.Variant);
        Assert.Equal("between 17.2.0 and 17.2.1", d.Group.RangeText);
        Assert.Equal(Variants.Aligned, d.ToResult().Variant);
    }

    [Fact]
    public void A_fresh_database_is_still_detected_as_fresh()
    {
        var d = Detector.Detect(Products.Engage, Fresh, BeforeGate, V17, "Complete");
        Assert.True(d.Exact);
        Assert.Equal(Variants.Fresh, d.ToResult().Variant);
        Assert.Empty(d.Alternatives);
    }

    [Fact]
    public void The_recorded_versions_name_each_release_once_across_variants()
        => Assert.Equal("between 17.2.0 and 17.2.1", Detector.Detect(Products.Engage, Aligned, BeforeGate, V17, "Aligned").ToResult().RecordedVersions);

    [Fact]
    public void Identical_fresh_and_aligned_schemas_are_told_apart_by_the_recorded_status()
    {
        var groups = Groups(M("13.8.0", "S", Fresh, Variants.Fresh, "Complete"), M("13.8.0", "S", Fresh, Variants.Aligned, "Aligned"));

        Assert.Equal(Variants.Aligned, Detector.Detect(Products.Engage, Fresh, "S", groups, "Aligned").Group!.Variant);
        Assert.Equal(Variants.Fresh, Detector.Detect(Products.Engage, Fresh, "S", groups, "Complete").Group!.Variant);
        // The other one is still an exact alternative, labelled with its variant.
        Assert.Equal([new DetectionAlternative("13.8.0", Variants.Aligned)], Detector.Detect(Products.Engage, Fresh, "S", groups, "Complete").ToResult().Alternatives);
    }

    [Fact]
    public void An_aligned_17x_schema_under_CMS_18_says_the_v18_upgrade_is_blocked()
    {
        var d = Detector.Detect(Products.Engage, Aligned, BeforeGate, V17, "Aligned", cmsMajor: 18);
        Assert.Equal("Engage v18 upgrade blocked: CompleteAlignSchema.sql has not been run (Umbraco is on v18, Engage is still on the 17.x schema)",
            UpgradeBlocks.NoticeFor(d));
    }

    [Theory]
    [InlineData(17)]
    [InlineData(null)]
    public void An_aligned_17x_schema_under_CMS_17_or_without_CMS_evidence_has_no_notice(int? cms)
        => Assert.Null(UpgradeBlocks.NoticeFor(Detector.Detect(Products.Engage, Aligned, BeforeGate, V17, "Aligned", cms)));

    [Fact]
    public void A_fresh_17x_schema_under_CMS_18_has_no_notice()
        => Assert.Null(UpgradeBlocks.NoticeFor(Detector.Detect(Products.Engage, Fresh, BeforeGate, V17, "Complete", cmsMajor: 18)));

    [Fact]
    public void An_aligned_schema_that_also_exists_in_18_goes_to_18_under_CMS_18_and_has_no_notice()
    {
        // Aligned 17.4.0 and 18.1.0 are identical; with CMS 18 the database is 18.1.0, which migrated fine and only disables Engage.
        var groups = Groups(M("17.4.0", "S4", Aligned, Variants.Aligned, "Aligned"), M("18.1.0", "S4", Aligned, Variants.Aligned, "Aligned"));
        var d = Detector.Detect(Products.Engage, Aligned, "S4", groups, "Aligned", cmsMajor: 18);
        Assert.Equal("18.1.0", d.Group!.RangeText);
        Assert.Null(UpgradeBlocks.NoticeFor(d));
    }

    // Console

    private static readonly Manifest Target = M("17.4.0", "S", Fresh, Variants.Aligned, "Aligned");

    private static string Render(DetectionResult d)
    {
        var report = Report.Create(Target, new(true, "S", "Aligned"), [], [], DateTimeOffset.UnixEpoch, d, Report.FromDetection);
        var sw = new StringWriter();
        ConsoleReporter.Write(report, sw, new ConsoleContext("db", null, null));
        return sw.ToString();
    }

    private static DetectionResult D(string versions, string? variant = Variants.Aligned, string? notice = null, bool exact = true)
        => new("record+schema", versions, versions, versions, versions, exact, true, versions, exact ? 0 : 1, 0, [], null, [], variant, notice);

    [Fact]
    public void Console_names_an_aligned_detection()
    {
        var text = Render(D("17.4.0"));
        Assert.Contains("Detected: Engage 17.4.0 (aligned: CompleteAlignSchema.sql has not been run yet)", text);
        Assert.DoesNotContain("Engage is disabled until CompleteAlignSchema.sql is run", text);
    }

    [Fact]
    public void Console_says_engage_18_1_and_later_is_disabled_while_aligned()
        => Assert.Contains("Detected: Engage 18.1.0 (aligned: CompleteAlignSchema.sql has not been run yet)\n  Engage is disabled until CompleteAlignSchema.sql is run",
            Render(D("18.1.0")).ReplaceLineEndings("\n"));

    [Fact]
    public void Console_shows_the_upgrade_notice()
        => Assert.Contains("  Engage v18 upgrade blocked: CompleteAlignSchema.sql has not been run",
            Render(D("between 17.2.0 and 17.2.4", notice: "Engage v18 upgrade blocked: CompleteAlignSchema.sql has not been run")));

    [Fact]
    public void Console_fresh_detection_is_unchanged()
        => Assert.Contains("Detected: Engage 17.4.0 (upgrade record and schema agree)", Render(D("17.4.0", Variants.Fresh)));

    [Fact]
    public void Console_closest_aligned_match_names_the_variant()
        => Assert.Contains("closest: Engage 17.4.0 (aligned)", Render(D("17.4.0", exact: false)));
}

