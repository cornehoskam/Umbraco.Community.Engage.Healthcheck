using System.Text.Json;
using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Reporting;
using Umbraco.Community.Engage.Healthcheck.Core.Schema;
using Umbraco.Community.Engage.Healthcheck.Core.State;
using Umbraco.Community.Engage.Healthcheck.Generator.Catalog;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

/// <summary>The complete variant (aligned, then CompleteAlignSchema.sql) next to fresh and aligned, with synthetic manifests only.</summary>
public class CompleteVariantTests
{
    private static readonly DatabaseSchema Fresh = SchemaBuilder.Baseline();

    // Complete: like fresh, but without the child's index (a completed database has no IX_<table>_key indexes).
    private static readonly DatabaseSchema Complete = SchemaBuilder.Db(Fresh.Tables
        .Select(t => t.Name == "umbracoEngageChild" ? t with { Indexes = [] } : t).ToArray());

    // Aligned: complete plus a nullable contentTypeId.
    private static readonly DatabaseSchema Aligned = SchemaBuilder.Db(Complete.Tables
        .Select(t => t.Name == "umbracoEngageChild" ? t with { Columns = t.Columns.Select(c => c.Name == "contentTypeId" ? c with { IsNullable = true } : c).ToList() } : t).ToArray());

    private static Manifest M(string version, string step, DatabaseSchema schema, string variant, string? status)
        => new(1, Products.Engage, version, variant, "17.5.3", MigrationState.StateKey, step, status, schema);

    private static IReadOnlyList<StateGroup> Groups(params Manifest[] manifests)
        => [.. new[] { Variants.Fresh, Variants.Aligned, Variants.Complete }.SelectMany(v => VersionIndex.Build(manifests.Where(m => m.Variant == v)))];

    private static readonly IReadOnlyList<StateGroup> V17 = Groups(
        M("17.4.0", "S", Fresh, Variants.Fresh, "Complete"),
        M("17.4.0", "S", Aligned, Variants.Aligned, "Aligned"),
        M("17.4.0", "S", Complete, Variants.Complete, "Complete"));

    [Theory]
    [InlineData("fresh", "Complete")]
    [InlineData("aligned", "Aligned")]
    [InlineData("complete", "Complete")]
    public void Each_variant_is_detected_from_its_own_schema(string variant, string status)
    {
        var schema = variant switch { "fresh" => Fresh, "aligned" => Aligned, _ => Complete };
        var d = Detector.Detect(Products.Engage, schema, "S", V17, status);
        Assert.True(d.Exact);
        Assert.Equal(variant, d.ToResult().Variant);
    }

    [Theory]
    [InlineData("Complete", "complete")]
    [InlineData("Aligned", "aligned")]
    public void Ties_follow_the_recorded_status(string status, string expected)
    {
        // An aligned and a complete group with the same schema: the status decides.
        var groups = Groups(M("13.8.0", "S", Complete, Variants.Aligned, "Aligned"), M("13.8.0", "S", Complete, Variants.Complete, "Complete"));
        Assert.Equal(expected, Detector.Detect(Products.Engage, Complete, "S", groups, status).Group!.Variant);
    }

    [Fact]
    public void A_complete_status_prefers_fresh_over_aligned_even_when_the_fresh_manifest_records_no_status()
    {
        // Fresh installs of releases before the status key (e.g. 13.7.x, 17.1.x) record no status; V18 fresh records Complete.
        var groups = Groups(M("17.1.1", "S", Fresh, Variants.Fresh, null), M("17.1.1", "S", Fresh, Variants.Aligned, "Aligned"));
        Assert.Equal(Variants.Fresh, Detector.Detect(Products.Engage, Fresh, "S", groups, "Complete").Group!.Variant);
    }

    [Fact]
    public void Complete_alternatives_are_labelled()
    {
        var groups = Groups(M("13.8.0", "S", Fresh, Variants.Fresh, "Complete"), M("13.8.0", "S", Fresh, Variants.Complete, "Complete"));
        var r = Detector.Detect(Products.Engage, Fresh, "S", groups, "Complete").ToResult();
        // Same schema and status: fresh wins the last tie, the complete group is the labelled alternative.
        Assert.Equal(Variants.Fresh, r.Variant);
        Assert.Equal([new DetectionAlternative("13.8.0", Variants.Complete)], r.Alternatives);
    }

    private static string Render(DetectionResult d)
    {
        var report = Report.Create(M("17.4.0", "S", Complete, Variants.Complete, "Complete"), new(true, "S", "Complete"), [], [], DateTimeOffset.UnixEpoch, d, Report.FromDetection);
        var sw = new StringWriter();
        ConsoleReporter.Write(report, sw, new ConsoleContext("db", null, null));
        return sw.ToString();
    }

    [Fact]
    public void Console_names_a_completed_alignment()
    {
        var d = new DetectionResult("record+schema", "17.4.0", "17.4.0", "17.4.0", "17.4.0", true, true, "17.4.0", 0, 0, [], null, [], Variants.Complete);
        var text = Render(d);
        Assert.Contains("Detected: Engage 17.4.0 (alignment completed)", text);
        Assert.DoesNotContain("CompleteAlignSchema.sql has not been run", text);
    }

    [Fact]
    public void Upgraded_17_2_3_and_17_2_4_report_the_older_schema_with_the_record_reaching_further()
    {
        // 17.2.0-17.2.4 share one step; fresh 17.2.3+ has contentTypeId NOT NULL, an upgrade from 17.2.2 or older keeps it NULL.
        var nullable = SchemaBuilder.Db(Fresh.Tables.Select(t => t.Name == "umbracoEngageChild"
            ? t with { Columns = t.Columns.Select(c => c.Name == "contentTypeId" ? c with { IsNullable = true } : c).ToList() } : t).ToArray());
        var groups = Groups(
            M("17.2.0", "S", nullable, Variants.Fresh, "Complete"), M("17.2.2", "S", nullable, Variants.Fresh, "Complete"),
            M("17.2.3", "S", Fresh, Variants.Fresh, "Complete"), M("17.2.4", "S", Fresh, Variants.Fresh, "Complete"));

        var d = Detector.Detect(Products.Engage, nullable, "S", groups, "Complete").ToResult();

        Assert.True(d.Exact);
        Assert.Equal("between 17.2.0 and 17.2.2", d.Versions);
        Assert.Equal("17.2.4", d.RecordedUpTo);
        Assert.Contains("Detected: Engage between 17.2.0 and 17.2.2 (upgrade record and schema agree) (upgrade record allows up to 17.2.4)", Render(d));
    }
}

public class KnownVariantsTests
{
    private static string Root()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "catalog", "releases.json"))) root = Path.GetDirectoryName(root)!;
        return root;
    }

    [Fact]
    public void Every_align_release_has_a_complete_entry_and_17_2_3_and_17_2_4_have_an_upgraded_one()
    {
        var root = Root();
        var catalog = ReleaseCatalog.Load(Path.Combine(root, "catalog", "releases.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "catalog", "known-variants.json")));
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();

        string[] Versions(JsonElement e) => e.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!).ToArray();
        var complete = entries.Where(e => e.GetProperty("assumption").GetString() == "complete = fresh").ToList();
        Assert.Equal(catalog.Select(Products.Engage).Where(r => r.AlignFrom is not null).Select(r => r.Version), complete.SelectMany(Versions));
        Assert.All(complete, e => Assert.Equal("complete", e.GetProperty("stored").GetString()));

        var upgraded = Assert.Single(entries, e => e.GetProperty("assumption").GetString() == "upgraded = fresh");
        Assert.Equal(["17.2.3", "17.2.4"], Versions(upgraded));
        Assert.Equal(JsonValueKind.Null, upgraded.GetProperty("stored").ValueKind);
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("reason").GetString())));
    }

    [Fact]
    public void A_database_completed_with_the_published_script_matches_the_next_complete_group_exactly()
    {
        // The published CompleteAlignSchema.sql makes contentTypeId NOT NULL, which the older own-release scripts did not:
        // a 16.3.0-16.3.2 or 17.2.0-17.2.2 database completed with it has the 16.3.3 or 17.2.3 complete shape.
        var file = Umbraco.Community.Engage.Healthcheck.Generator.Verify.KnownVariants.Parse(File.ReadAllText(Path.Combine(Root(), "catalog", "known-variants.json")));
        var entry = Assert.Single(file.Entries, e => e.Assumption == Umbraco.Community.Engage.Healthcheck.Generator.Verify.KnownVariants.PublishedScriptEqualsComplete);
        Assert.Equal(["16.3.0", "16.3.1", "16.3.2", "17.2.0", "17.2.1", "17.2.2"], entry.Versions);
        Assert.Null(entry.Stored);

        var store = new ManifestStore(null);
        foreach (var version in entry.Versions)
        {
            var stored = store.Find(Products.Engage, version, Variants.Complete)!;
            var published = store.Find(Products.Engage, version.StartsWith("16.", StringComparison.Ordinal) ? "16.3.3" : "17.2.3", Variants.Complete)!;
            var differences = Umbraco.Community.Engage.Healthcheck.Core.Comparison.SchemaComparer.Compare(published.Schema, stored.Schema);
            Assert.True(Umbraco.Community.Engage.Healthcheck.Generator.Verify.KnownVariants.Matches(entry, differences), $"{version}: {differences.Count} differences");
            Assert.Equal(published.LastMigrationStep, stored.LastMigrationStep);
        }
    }
}

public class StatusAcrossMajorsTests
{
    private static readonly DatabaseSchema Schema = SchemaBuilder.Baseline();
    private static Manifest M(string v, string? status) => new(1, Products.Engage, v, Variants.Fresh, "x", MigrationState.StateKey, "S", status, Schema);

    [Fact]
    public void A_matching_status_beats_a_newer_major_with_another_status()
        => Assert.Equal("18.1.0", Detector.Detect(Products.Engage, Schema, "S", VersionIndex.Build([M("17.4.0", null), M("18.1.0", "Complete")]), "Complete").Group!.RangeText);

    [Fact]
    public void Identical_states_in_two_majors_name_the_older_major_with_the_newer_as_alternative()
    {
        // Real case: fresh 17.4.0 and 18.1.0 record the same step, status and schema.
        var d = Detector.Detect(Products.Engage, Schema, "S", VersionIndex.Build([M("17.4.0", "Complete"), M("18.1.0", "Complete")]), "Complete").ToResult();
        Assert.Equal("17.4.0", d.Versions);
        Assert.Equal([new DetectionAlternative("18.1.0", Variants.Fresh, Identical: true)], d.Alternatives);
    }
}

public class KnownVariantsAgainstManifestsTests
{
    private static string Root()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "catalog", "releases.json"))) root = Path.GetDirectoryName(root)!;
        return root;
    }

    [Fact]
    public void Each_stored_complete_entry_has_an_embedded_manifest_and_the_recorded_differences_recompute()
    {
        var file = Umbraco.Community.Engage.Healthcheck.Generator.Verify.KnownVariants.Parse(File.ReadAllText(Path.Combine(Root(), "catalog", "known-variants.json")));
        var store = new ManifestStore(null);
        foreach (var entry in file.Entries.Where(e => e.Stored == Variants.Complete))
        {
            foreach (var version in entry.Versions)
            {
                var fresh = store.Find(Products.Engage, version, Variants.Fresh)!;
                var complete = store.Find(Products.Engage, version, Variants.Complete);
                Assert.True(complete is not null, $"no embedded complete manifest for {version}");
                var differences = Umbraco.Community.Engage.Healthcheck.Core.Comparison.SchemaComparer.Compare(fresh.Schema, complete!.Schema);
                Assert.True(Umbraco.Community.Engage.Healthcheck.Generator.Verify.KnownVariants.Matches(entry, differences),
                    $"{version}: {differences.Count} differences ({string.Join(", ", differences.GroupBy(d => d.Kind).Select(g => $"{g.Key} {g.Count()}"))}), recorded {entry.Differences}");
            }
        }
    }

    [Fact]
    public void The_releases_without_an_aligned_variant_say_why()
    {
        var catalog = ReleaseCatalog.Load(Path.Combine(Root(), "catalog", "releases.json"));
        Assert.Equal(["16.2.0", "18.0.0"], catalog.Select(Products.Engage).Where(r => r.AlignNote is not null).Select(r => r.Version));
        Assert.All(catalog.Select(Products.Engage).Where(r => r.AlignNote is not null), r => Assert.Null(r.AlignFrom));
    }
}
