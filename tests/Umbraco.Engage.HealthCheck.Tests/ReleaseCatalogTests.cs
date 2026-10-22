using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Schema;
using Umbraco.Engage.HealthCheck.Generator;
using Umbraco.Engage.HealthCheck.Generator.Catalog;

namespace Umbraco.Engage.HealthCheck.Tests;

public class ReleaseCatalogTests
{
    private const string Hosts = """
        "hosts": {
          "V13": { "project": "hosts/Host.V13", "umbraco": "13.16.2", "forms": "13.9.11", "commerce": "13.3.3" },
          "V17": { "project": "hosts/Host.V17", "umbraco": "17.5.3", "forms": "17.6.1", "commerce": "17.2.8" }
        }
        """;

    private static string Json(string releases) => $"{{ {Hosts}, \"releases\": [ {releases} ] }}";

    [Fact]
    public void Releases_are_ordered_by_version_not_text()
    {
        var catalog = ReleaseCatalog.Parse(Json("""
            { "product": "Engage", "version": "17.10.0", "major": "V17" },
            { "product": "Engage", "version": "17.2.0", "major": "V17" },
            { "product": "Engage", "version": "13.8.1", "major": "V13" }
            """));
        Assert.Equal(["13.8.1", "17.2.0", "17.10.0"], catalog.Select(Products.Engage).Select(r => r.Version));
    }

    [Fact]
    public void Select_narrows_by_product_and_major()
    {
        var catalog = ReleaseCatalog.Parse(Json("""
            { "product": "Engage", "version": "17.2.0", "major": "V17" },
            { "product": "Engage", "version": "13.8.1", "major": "V13" },
            { "product": "Engage.Forms", "version": "17.0.0", "major": "V17", "engage": "17.2.0" }
            """));
        Assert.Single(catalog.Select(Products.Engage, "V17"));
        Assert.Single(catalog.Select(Products.Forms));
        Assert.Equal(3, catalog.Select().Count);
    }

    [Fact]
    public void An_unknown_host_is_rejected()
        => Assert.Contains("unknown host", Assert.Throws<InvalidDataException>(() => ReleaseCatalog.Parse(Json("""{ "product": "Engage", "version": "16.0.0", "major": "V16" }"""))).Message);

    [Fact]
    public void A_duplicate_release_is_rejected()
        => Assert.Contains("Duplicate", Assert.Throws<InvalidDataException>(() => ReleaseCatalog.Parse(Json("""
            { "product": "Engage", "version": "17.0.0", "major": "V17" },
            { "product": "Engage", "version": "17.0.0", "major": "V17" }
            """))).Message);

    [Fact]
    public void An_add_on_without_engage_is_rejected()
        => Assert.Contains("engage", Assert.Throws<InvalidDataException>(() => ReleaseCatalog.Parse(Json("""{ "product": "Engage.Forms", "version": "17.0.0", "major": "V17" }"""))).Message);

    [Fact]
    public void An_unknown_product_is_rejected()
        => Assert.Contains("Unknown product", Assert.Throws<InvalidDataException>(() => ReleaseCatalog.Parse(Json("""{ "product": "Workflow", "version": "1.0.0", "major": "V17" }"""))).Message);

    [Fact]
    public void SpecFor_uses_host_versions_and_the_per_release_overrides()
    {
        var catalog = ReleaseCatalog.Parse(Json("""
            { "product": "Engage", "version": "13.0.0", "major": "V13", "umbraco": "13.5.0" },
            { "product": "Engage", "version": "17.3.1", "major": "V17" },
            { "product": "Engage", "version": "17.5.0", "major": "V17" },
            { "product": "Engage.Forms", "version": "17.1.0", "major": "V17", "engage": "17.3.1", "pinReason": "x" },
            { "product": "Engage.Commerce", "version": "17.0.0", "major": "V17", "engage": "17.5.0" }
            """));
        var root = Path.GetFullPath("root");

        var engage = catalog.SpecFor(catalog.Select(Products.Engage, "V13").Single(), root);
        Assert.Equal("13.0.0", engage.EngageVersion);
        Assert.Equal("13.5.0", engage.UmbracoVersion);
        Assert.Equal(Path.Combine(root, "hosts", "Host.V13"), engage.HostProject);
        Assert.Null(engage.FormsVersion);

        var forms = catalog.SpecFor(catalog.Select(Products.Forms).Single(), root);
        Assert.Equal(("17.3.1", "17.1.0", "17.6.1", "17.5.3"), (forms.EngageVersion, forms.FormsVersion, forms.UmbracoFormsVersion, forms.UmbracoVersion));

        var commerce = catalog.SpecFor(catalog.Select(Products.Commerce).Single(), root);
        Assert.Equal(("17.0.0", "17.2.8"), (commerce.CommerceVersion, commerce.UmbracoCommerceVersion));
        Assert.Null(commerce.FormsVersion);
    }

    [Fact]
    public void The_shipped_catalog_has_the_expected_release_counts()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "catalog", "releases.json"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var catalog = ReleaseCatalog.Load(Path.Combine(dir, "catalog", "releases.json"));
        Assert.Equal((50, 14, 6), (catalog.Select(Products.Engage).Count, catalog.Select(Products.Forms).Count, catalog.Select(Products.Commerce).Count));
    }
}

public class MigrationPlanReaderTests
{
    [Fact]
    public void Guid_form_takes_the_last_target_and_ignores_comments()
    {
        const string plan = """
            To<AddA>("{11111111-1111-1111-1111-111111111111}");
            To<AddB>("{22222222-2222-2222-2222-222222222222}");
            // To<Old>("{99999999-9999-9999-9999-999999999999}");
            To("{33333333-3333-3333-3333-333333333333}"); // reserved
            // V17.5.0 contains
            """;
        Assert.Equal("{33333333-3333-3333-3333-333333333333}", MigrationPlanReader.LastStep(plan));
    }

    [Fact]
    public void Nameof_form_returns_the_name()
    {
        const string plan = """
            To<First>(nameof(First));
            To<AlignCultureColumnLengths>(nameof(AlignCultureColumnLengths));
            """;
        Assert.Equal("AlignCultureColumnLengths", MigrationPlanReader.LastStep(plan));
        Assert.Equal("Last", MigrationPlanReader.LastStep("To(nameof(Last));"));
    }

    [Fact]
    public void Plain_string_form_is_returned()
        => Assert.Equal("my-state", MigrationPlanReader.LastStep("From(\"\");\nTo(\"a\");\nTo<X>(\"my-state\");"));

    [Fact]
    public void Text_without_a_To_call_gives_null()
        => Assert.Null(MigrationPlanReader.LastStep("var x = list.ToList(); // nothing"));

    [Fact]
    public void Plan_paths_follow_the_major()
    {
        Assert.EndsWith("UmsMigrationPlan.cs", MigrationPlanReader.PlanPath(Products.Engage, "V13"));
        Assert.EndsWith("UmsMigrationPlan.cs", MigrationPlanReader.PlanPath(Products.Engage, "V16"));
        Assert.EndsWith("EngageMigrationPlan.cs", MigrationPlanReader.PlanPath(Products.Engage, "V17"));
        Assert.EndsWith("EngageMigrationPlan.cs", MigrationPlanReader.PlanPath(Products.Engage, "V18"));
        Assert.Equal(Path.Combine("r", "Umbraco.Engage.Forms", "Umbraco.Engage.Forms.V17"), MigrationPlanReader.RepoFolder("r", Products.Forms, "V17"));
    }

    [Fact]
    public void Engage_tags_are_read_at_both_plan_names_with_and_without_the_src_folder()
    {
        // Engage tags 13.0.0-13.4.0 have no src/ folder; V17 tags up to 17.1.1 still use UmsMigrationPlan.cs.
        Assert.Equal(
            [
                "src/Umbraco.Engage.Web/Migrations/UmsMigrationPlan.cs",
                "src/Umbraco.Engage.Web/Migrations/EngageMigrationPlan.cs",
                "Umbraco.Engage.Web/Migrations/UmsMigrationPlan.cs",
                "Umbraco.Engage.Web/Migrations/EngageMigrationPlan.cs",
            ],
            MigrationPlanReader.CandidatePaths(Products.Engage, "V13"));
        Assert.Equal("src/Umbraco.Engage.Web/Migrations/EngageMigrationPlan.cs", MigrationPlanReader.CandidatePaths(Products.Engage, "V18")[0]);
        Assert.Equal(["Umbraco.Engage.Forms/Migrations/UmbracoFormsMigrationPlan.cs"], MigrationPlanReader.CandidatePaths(Products.Forms, "V13"));
    }

    [Fact]
    public void Compare_reports_match_mismatch_and_unverified()
    {
        const string plan = "To<X>(\"{aaa}\");";
        Assert.Equal(CheckStatus.Match, LastStepCheck.Compare("{AAA}", plan, null).Status);
        Assert.Equal(CheckStatus.Mismatch, LastStepCheck.Compare("{bbb}", plan, null).Status);
        Assert.Equal(CheckStatus.Mismatch, LastStepCheck.Compare(null, plan, null).Status);
        Assert.Equal(CheckStatus.Unverified, LastStepCheck.Compare("{aaa}", null, "tag missing").Status);
        Assert.Equal(CheckStatus.Unverified, LastStepCheck.Compare("{aaa}", "nothing here", null).Status);
    }
}

public class GenerateAllRunnerTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "hc-genall-" + Guid.NewGuid().ToString("N"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hc-genall-root-" + Guid.NewGuid().ToString("N"));

    public GenerateAllRunnerTests() => Directory.CreateDirectory(Path.Combine(_root, "hosts", "Host.V17"));

    public void Dispose()
    {
        foreach (var d in new[] { _out, _root }) if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    private const string Json = """
        { "hosts": {
            "V13": { "project": "hosts/Host.V13", "umbraco": "13.16.2" },
            "V17": { "project": "hosts/Host.V17", "umbraco": "17.5.3" } },
          "releases": [
            { "product": "Engage", "version": "17.0.0", "major": "V17" },
            { "product": "Engage", "version": "17.1.0", "major": "V17" },
            { "product": "Engage", "version": "13.8.1", "major": "V13" } ] }
        """;

    private static Manifest Fake(string version, string step = "s") => new(1, Products.Engage, version, "fresh", "17.5.3", "k", step, null, new DatabaseSchema([]));

    private Task<IReadOnlyList<ReleaseResult>> Run(Func<HostSpec, Task<IReadOnlyList<Manifest>>> generate, bool force = false, Func<Manifest, Release, CheckOutcome>? check = null)
    {
        var catalog = ReleaseCatalog.Parse(Json);
        return GenerateAllRunner.RunAsync(catalog, catalog.Select(), _out, force, _root, (stages, _, _) => generate(stages[^1]),
            check ?? ((_, _) => new CheckOutcome(CheckStatus.Match, "")), TextWriter.Null, CancellationToken.None);
    }

    [Fact]
    public async Task Existing_manifests_are_skipped_unless_forced_and_a_missing_host_fails_without_stopping()
    {
        var existing = Path.Combine(_out, "engage", "17.0.0.fresh.json");
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, ManifestSerializer.Serialize(Fake("17.0.0") with { Schema = new DatabaseSchema([new TableSchema("t", [], null, [], [], [], [])]) }));

        var generated = new List<string>();
        Task<IReadOnlyList<Manifest>> Generate(HostSpec spec)
        {
            generated.Add(spec.EngageVersion);
            return Task.FromResult<IReadOnlyList<Manifest>>([Fake(spec.EngageVersion)]);
        }

        var results = await Run(Generate);

        // Version order: 13.8.1 (host missing), 17.0.0 (exists), 17.1.0 (generated).
        Assert.Equal(["13.8.1", "17.0.0", "17.1.0"], results.Select(r => r.Release.Version));
        Assert.Equal([ReleaseStatus.Failed, ReleaseStatus.Skipped, ReleaseStatus.Done], results.Select(r => r.Status));
        Assert.Contains("host missing", results[0].Reason);
        Assert.Equal(["17.1.0"], generated);
        Assert.True(File.Exists(Path.Combine(_out, "engage", "17.1.0.fresh.json")));
        Assert.True(GenerateAllRunner.HasFailures(results));

        generated.Clear();
        await Run(Generate, force: true);
        Assert.Equal(["17.0.0", "17.1.0"], generated);
    }

    [Fact]
    public async Task A_failing_release_does_not_stop_the_run_and_a_mismatch_counts_as_a_failure()
    {
        var results = await Run(
            spec => spec.EngageVersion == "17.0.0" ? throw new InvalidOperationException("boom") : Task.FromResult<IReadOnlyList<Manifest>>([Fake(spec.EngageVersion)]),
            check: (_, _) => new CheckOutcome(CheckStatus.Mismatch, "differs"));

        Assert.Equal("boom", results.Single(r => r.Release.Version == "17.0.0").Reason);
        Assert.True(results.Single(r => r.Release.Version == "17.1.0").IsMismatch);
        Assert.Contains("last-step mismatches 1", GenerateAllRunner.Summary(results));
    }

    [Fact]
    public async Task Unverified_is_not_a_failure()
    {
        Directory.CreateDirectory(Path.Combine(_root, "hosts", "Host.V13"));
        var results = await Run(spec => Task.FromResult<IReadOnlyList<Manifest>>([Fake(spec.EngageVersion)]), check: (_, _) => new CheckOutcome(CheckStatus.Unverified, "no tag"));
        Assert.False(GenerateAllRunner.HasFailures(results));
        Assert.Contains("unverified 3", GenerateAllRunner.Summary(results));
    }
}
