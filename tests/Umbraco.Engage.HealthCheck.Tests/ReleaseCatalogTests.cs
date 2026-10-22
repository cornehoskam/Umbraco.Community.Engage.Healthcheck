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

public class PackagePlanReaderTests
{
    private static PlanInstruction S(string text) => new PlanInstruction.Literal(text);
    private static PlanInstruction C(string method) => new PlanInstruction.Call(method);

    [Fact]
    public void The_last_To_call_takes_the_literal_loaded_since_the_call_before_it()
    {
        PlanInstruction[] plan =
        [
            S("Umbraco.Engage"), C(".ctor"), C("From"),
            S("{11111111-1111-1111-1111-111111111111}"), C("To"),
            S("{22222222-2222-2222-2222-222222222222}"), C("To"),
        ];
        Assert.Equal(("{22222222-2222-2222-2222-222222222222}", null), PackagePlanReader.LastState(plan));
    }

    [Fact]
    public void A_name_state_is_returned_as_is()
        => Assert.Equal(("AlignCultureColumnLengths", null), PackagePlanReader.LastState([S("First"), C("To"), S("AlignCultureColumnLengths"), C("To")]));

    [Fact]
    public void A_last_call_without_its_own_literal_is_unverified()
    {
        var (step, problem) = PackagePlanReader.LastState([S("{a}"), C("To"), C("get_Last"), C("To")]);
        Assert.Null(step);
        Assert.Contains("does not take a string literal", problem);
    }

    [Fact]
    public void A_plan_without_To_calls_gives_a_reason()
        => Assert.Equal((null, "no To(...) step found in the plan"), PackagePlanReader.LastState([S("x"), C("From")]));

    [Fact]
    public void Literals_after_the_last_step_do_not_count()
        => Assert.Equal(("{z}", null), PackagePlanReader.LastState([S("{z}"), C("To"), S("trailing"), C("Log")]));

    [Fact]
    public void Package_ids_follow_the_product()
    {
        Assert.Equal("umbraco.engage.web", PackagePlanReader.PackageId(Products.Engage));
        Assert.Equal("umbraco.engage.forms", PackagePlanReader.PackageId(Products.Forms));
        Assert.Equal("umbraco.engage.commerce", PackagePlanReader.PackageId(Products.Commerce));
    }

    [Fact]
    public void A_package_missing_from_the_cache_is_unverified()
    {
        var release = new Release(Products.Engage, "99.0.0", "V99");
        var (step, problem) = PackagePlanReader.LastStep(Path.Combine(Path.GetTempPath(), "hc-no-nuget-" + Guid.NewGuid().ToString("N")), release);
        Assert.Null(step);
        Assert.Contains("is not in", problem);
    }

    [Fact]
    public void Compare_reports_match_mismatch_and_unverified()
    {
        Assert.Equal(CheckStatus.Match, LastStepCheck.Compare("{AAA}", "{aaa}", null).Status);
        Assert.Equal(CheckStatus.Mismatch, LastStepCheck.Compare("{bbb}", "{aaa}", null).Status);
        Assert.Equal(CheckStatus.Mismatch, LastStepCheck.Compare(null, "{aaa}", null).Status);
        Assert.Equal(CheckStatus.Unverified, LastStepCheck.Compare("{aaa}", null, "package missing").Status);
    }

    /// <summary>
    /// Every shipped manifest's recorded last step equals the last step read from that release's public NuGet package.
    /// Releases whose package is not in the local cache are listed, not failed: the cache fills as the hosts are built.
    /// </summary>
    [Fact]
    public void Every_shipped_manifest_matches_its_package_plan()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "catalog", "releases.json"))) root = Path.GetDirectoryName(root)!;
        var catalog = ReleaseCatalog.Load(Path.Combine(root, "catalog", "releases.json"));
        var store = new ManifestStore(Path.Combine(root, "manifests"), includeEmbedded: false);
        var nuget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        var mismatches = new List<string>();
        var checkedCount = 0;
        foreach (var product in new[] { Products.Engage, Products.Forms, Products.Commerce })
        {
            foreach (var manifest in store.All(product))
            {
                var release = catalog.Find(product, manifest.Version);
                Assert.NotNull(release);
                var (step, problem) = PackagePlanReader.LastStep(nuget, release);
                if (problem is not null && problem.Contains("is not in", StringComparison.Ordinal)) continue;
                checkedCount++;
                if (LastStepCheck.Compare(manifest.LastMigrationStep, step, problem).Status != CheckStatus.Match)
                    mismatches.Add($"{product} {manifest.Version} {manifest.Variant}: manifest {manifest.LastMigrationStep}, package {step ?? problem}");
            }
        }
        Assert.Empty(mismatches);
        Assert.True(checkedCount > 0 || !Directory.Exists(Path.Combine(nuget, "umbraco.engage.web")));
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
