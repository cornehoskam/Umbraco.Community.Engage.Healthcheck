using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Core.Detection;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Schema;
using Umbraco.Engage.HealthCheck.Core.State;

namespace Umbraco.Engage.HealthCheck.Tests;

public sealed class DetectionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "detect_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static readonly DatabaseSchema A = SchemaBuilder.Baseline();

    // A with one extra column on the parent table: a schema one release "ahead" of A.
    private static readonly DatabaseSchema B = SchemaBuilder.Db(
        A.Tables.Select(t => t.Name == "umbracoEngageParent" ? t with { Columns = [.. t.Columns, SchemaBuilder.Col("extra", nullable: true)] } : t).ToArray());

    // B with one more new table.
    private static readonly DatabaseSchema C = SchemaBuilder.Db([.. B.Tables, SchemaBuilder.Table("umbracoEngageNew", [SchemaBuilder.Col("id")])]);

    private static Manifest M(string version, string? step, DatabaseSchema schema, string product = Products.Engage, string variant = "fresh")
        => new(1, product, version, variant, "17.5.3", MigrationState.StateKey, step, null, schema);

    // 17.0.0-17.0.2 share step and schema; 17.1.0 is alone; 17.2.0 shares 17.1.0's step but not its schema.
    private static readonly Manifest[] Releases =
    [
        M("17.0.0", "{s1}", A), M("17.0.1", "{s1}", A), M("17.0.2", "{s1}", A),
        M("17.1.0", "{s2}", B),
        M("17.2.0", "{s2}", C),
    ];

    [Fact]
    public void Consecutive_releases_with_same_step_and_schema_form_one_range()
    {
        var groups = VersionIndex.Build(Releases);

        Assert.Equal(["between 17.0.0 and 17.0.2", "17.1.0", "17.2.0"], groups.Select(g => g.RangeText));
        Assert.Equal(["17.0.0", "17.0.1", "17.0.2"], groups[0].Versions);
        Assert.Equal("17.0.2", groups[0].Representative.Version);
        Assert.Equal("{s1}", groups[0].LastMigrationStep);
        Assert.Equal(17, groups[0].Major);
    }

    [Fact]
    public void Versions_are_ordered_by_system_version_not_text()
    {
        var groups = VersionIndex.Build([M("17.10.0", "{x}", A), M("17.9.0", "{x}", A), M("17.2.0", "{x}", A)]);
        Assert.Equal(["17.2.0", "17.9.0", "17.10.0"], Assert.Single(groups).Versions);
    }

    [Fact]
    public void Same_step_and_schema_across_majors_are_separate_groups()
    {
        var groups = VersionIndex.Build([M("16.5.0", "{x}", A), M("17.0.0", "{x}", A), M("18.0.0", "{x}", A)]);
        Assert.Equal(["16.5.0", "17.0.0", "18.0.0"], groups.Select(g => g.RangeText));
    }

    [Fact]
    public void Same_step_that_comes_back_later_is_not_merged()
    {
        var groups = VersionIndex.Build([M("17.0.0", "{x}", A), M("17.0.1", "{y}", A), M("17.0.2", "{x}", A)]);
        Assert.Equal(3, groups.Count);
    }

    [Fact]
    public void A_different_schema_status_starts_a_new_group()
    {
        var groups = VersionIndex.Build([M("17.0.0", "{x}", A), M("17.0.1", "{x}", A) with { SchemaStatus = "Complete" }, M("17.0.2", "{x}", A) with { SchemaStatus = "complete" }]);
        Assert.Equal(["17.0.0", "between 17.0.1 and 17.0.2"], groups.Select(g => g.RangeText));
    }

    [Fact]
    public void Products_and_variants_get_their_own_groups()
    {
        var groups = VersionIndex.Build([M("17.0.0", "{x}", A), M("17.0.1", "{x}", A, variant: "upgraded"), M("17.0.2", "{x}", A, product: Products.Forms)]);
        Assert.Equal(3, groups.Count);
    }

    [Fact]
    public void Unparseable_versions_are_skipped()
        => Assert.Equal("17.0.0", Assert.Single(VersionIndex.Build([M("17.0.0", "{x}", A), M("junk", "{x}", A)])).RangeText);

    [Fact]
    public void Range_is_detected_when_record_and_schema_agree()
    {
        var d = Detector.Detect(Products.Engage, A, "{s1}", VersionIndex.Build(Releases));

        Assert.Equal(DetectionBasis.RecordAndSchema, d.Basis);
        Assert.Equal("between 17.0.0 and 17.0.2", d.Group!.RangeText);
        Assert.True(d.Exact);
        Assert.True(d.RecordMatchesSchema);
        Assert.Empty(d.Alternatives);
    }

    [Fact]
    public void Single_release_is_detected()
    {
        var d = Detector.Detect(Products.Engage, B, "{s2}", VersionIndex.Build(Releases));

        Assert.Equal("17.1.0", d.Group!.RangeText);
        Assert.True(d.Exact);
        Assert.True(d.RecordMatchesSchema);
        Assert.Equal(DetectionBasis.RecordAndSchema, d.Basis);
    }

    [Fact]
    public void Same_step_split_by_schema_lets_the_schema_choose()
    {
        var d = Detector.Detect(Products.Engage, C, "{s2}", VersionIndex.Build(Releases));
        Assert.Equal("17.2.0", d.Group!.RangeText);
        Assert.True(d.RecordMatchesSchema);
        Assert.Equal("between 17.1.0 and 17.2.0", d.ToResult().RecordedVersions);
    }

    [Fact]
    public void No_recorded_step_is_schema_only()
    {
        var d = Detector.Detect(Products.Engage, B, null, VersionIndex.Build(Releases));

        Assert.Equal(DetectionBasis.SchemaOnly, d.Basis);
        Assert.Equal("17.1.0", d.Group!.RangeText);
        Assert.True(d.Exact);
        Assert.False(d.RecordMatchesSchema);
    }

    [Fact]
    public void Unknown_recorded_step_is_schema_only()
    {
        var d = Detector.Detect(Products.Engage, A, "{nobody}", VersionIndex.Build(Releases));

        Assert.Equal(DetectionBasis.SchemaOnly, d.Basis);
        Assert.Equal("between 17.0.0 and 17.0.2", d.Group!.RangeText);
        Assert.True(d.Exact);
        Assert.Null(d.ToResult().RecordedVersions);
    }

    [Fact]
    public void Record_behind_the_schema_is_a_contradiction_issue_99()
    {
        // The upgrade record says 17.0.x, but the schema already has 17.2.0's tables.
        var d = Detector.Detect(Products.Engage, C, "{s1}", VersionIndex.Build(Releases));

        Assert.Equal(DetectionBasis.RecordAndSchema, d.Basis);
        Assert.Equal("17.2.0", d.Group!.RangeText);
        Assert.True(d.Exact);
        Assert.False(d.RecordMatchesSchema);
        Assert.Equal("between 17.0.0 and 17.0.2", d.ToResult().RecordedVersions);
    }

    [Fact]
    public void No_exact_match_gives_the_closest_with_counts()
    {
        // A without the parent's unique constraint: one critical difference from 17.0.x.
        var damaged = SchemaBuilder.Db(A.Tables.Select(t => t.Name == "umbracoEngageParent" ? t with { UniqueConstraints = [] } : t).ToArray());

        var d = Detector.Detect(Products.Engage, damaged, "{s1}", VersionIndex.Build(Releases));

        Assert.False(d.Exact);
        Assert.Equal("between 17.0.0 and 17.0.2", d.Group!.RangeText);
        Assert.Equal(1, d.Critical);
        Assert.Equal(0, d.Warning);
        Assert.True(d.RecordMatchesSchema);
    }

    [Fact]
    public void Closest_group_that_is_not_the_records_still_reports_the_disagreement()
    {
        // 17.2.0's schema without the parent's unique constraint, while the record says 17.0.x.
        var damaged = SchemaBuilder.Db(C.Tables.Select(t => t.Name == "umbracoEngageParent" ? t with { UniqueConstraints = [] } : t).ToArray());

        var d = Detector.Detect(Products.Engage, damaged, "{s1}", VersionIndex.Build(Releases));

        Assert.False(d.Exact);
        Assert.Equal("17.2.0", d.Group!.RangeText);
        Assert.False(d.RecordMatchesSchema);
        Assert.Equal(DetectionBasis.RecordAndSchema, d.Basis);
        Assert.Equal("between 17.0.0 and 17.0.2", d.ToResult().RecordedVersions);
    }

    [Fact]
    public void Recorded_versions_only_merge_adjacent_groups_of_one_major()
    {
        var gap = VersionIndex.Build([M("17.0.0", "{x}", A), M("17.0.1", "{y}", A), M("17.0.2", "{x}", A)]);
        Assert.Equal("17.0.0, 17.0.2", Detector.Detect(Products.Engage, A, "{x}", gap).ToResult().RecordedVersions);

        var majors = VersionIndex.Build([M("16.5.0", "{x}", A), M("17.0.0", "{x}", A)]);
        Assert.Equal("16.5.0, 17.0.0", Detector.Detect(Products.Engage, A, "{x}", majors).ToResult().RecordedVersions);
    }

    [Fact]
    public void Record_wider_than_the_match_says_how_far_it_reaches()
    {
        // {s2} covers 17.1.0 and 17.2.0; the schema is 17.1.0.
        var r = Detector.Detect(Products.Engage, B, "{s2}", VersionIndex.Build(Releases)).ToResult();
        Assert.Equal("17.1.0", r.Versions);
        Assert.True(r.RecordMatchesSchema);
        Assert.Equal("17.2.0", r.RecordedUpTo);
    }

    [Fact]
    public void Tie_on_differences_goes_to_the_recorded_step()
    {
        // 17.0.0 and 17.0.1 have the same schema but different steps.
        var groups = VersionIndex.Build([M("17.0.0", "{x}", A), M("17.0.1", "{y}", A)]);
        var damaged = SchemaBuilder.Db(A.Tables.Where(t => t.Name != "umbracoEngageChild").ToArray());

        Assert.Equal("17.0.0", Detector.Detect(Products.Engage, damaged, "{x}", groups).Group!.RangeText);
        Assert.Equal("17.0.1", Detector.Detect(Products.Engage, damaged, "{y}", groups).Group!.RangeText);
    }

    [Fact]
    public void Matches_in_two_majors_are_listed_as_alternatives()
    {
        var groups = VersionIndex.Build([M("16.5.0", "{x}", A), M("17.0.0", "{x}", A), M("17.1.0", "{z}", B)]);

        var d = Detector.Detect(Products.Engage, A, "{x}", groups);

        Assert.True(d.Exact);
        // Identical across majors: the older major is named, the newer is the exact alternative.
        Assert.Equal("16.5.0", d.Group!.RangeText);
        Assert.Equal(["17.0.0"], d.Alternatives.Select(a => a.RangeText));
        Assert.Equal([16, 17], d.Majors);
    }

    [Fact]
    public void Empty_actual_schema_with_a_known_record_is_record_only()
    {
        var d = Detector.Detect(Products.Engage, new DatabaseSchema([]), "{s2}", VersionIndex.Build(Releases));

        Assert.Equal(DetectionBasis.RecordOnly, d.Basis);
        Assert.Equal("17.2.0", d.Group!.RangeText);
        Assert.False(d.Exact);
    }

    [Fact]
    public void No_groups_detects_nothing()
    {
        var d = Detector.Detect(Products.Engage, A, "{s1}", []);
        Assert.Null(d.Group);
        Assert.False(d.Exact);
    }

    [Fact]
    public void Result_carries_no_schema_and_names_the_group()
    {
        var r = Detector.Detect(Products.Engage, A, "{s1}", VersionIndex.Build(Releases)).ToResult();
        Assert.Equal("record+schema", r.Basis);
        Assert.Equal("between 17.0.0 and 17.0.2", r.Versions);
        Assert.Equal("17.0.0", r.FirstVersion);
        Assert.Equal("17.0.2", r.LastVersion);
        Assert.Equal("17.0.2", r.ComparedWith);
    }

    private void Write(Manifest manifest)
    {
        var path = Path.Combine(_folder, Products.Folder(manifest.Product), ManifestStore.FileName(manifest.Version, manifest.Variant));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ManifestSerializer.Serialize(manifest));
    }

    [Fact]
    public void Store_builds_the_index_once_per_product_and_variant()
    {
        foreach (var m in Releases) Write(m);
        var store = new ManifestStore(_folder, includeEmbedded: false);

        var first = store.Groups(Products.Engage, "fresh");
        var second = store.Groups(Products.Engage, "fresh");

        Assert.Same(first, second);
        Assert.Equal(3, first.Count);
        Assert.Empty(store.Groups(Products.Engage, "upgraded"));
        // Finding a manifest after the index was built reuses the loaded one.
        Assert.Same(first[0].Representative, store.Find(Products.Engage, "17.0.2", "fresh"));
    }
}
