using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Schema;
using Umbraco.Engage.HealthCheck.Generator;
using Umbraco.Engage.HealthCheck.Generator.Catalog;

namespace Umbraco.Engage.HealthCheck.Tests;

public class StagedCatalogTests
{
    private const string Hosts = """
        "hosts": { "V17": { "project": "hosts/Host.V17", "umbraco": "17.5.3", "forms": "17.6.1", "commerce": "17.2.8" } }
        """;

    private static string Json(string releases) => $"{{ {Hosts}, \"releases\": [ {releases} ] }}";

    private const string Engages = """
        { "product": "Engage", "version": "17.0.4", "major": "V17" },
        { "product": "Engage", "version": "17.1.0", "major": "V17", "installFrom": "17.0.4" },
        { "product": "Engage", "version": "17.5.0", "major": "V17" },
        """;

    [Fact]
    public void An_engage_install_from_gives_two_stages_on_the_same_host()
    {
        var catalog = ReleaseCatalog.Parse(Json(Engages.TrimEnd(',')));
        var stages = catalog.StagesFor(catalog.Select(Products.Engage, "V17").Single(r => r.Version == "17.1.0"), Path.GetFullPath("r"));
        Assert.Equal(["17.0.4", "17.1.0"], stages.Select(s => s.EngageVersion));
        Assert.All(stages, s => Assert.Equal("17.5.3", s.UmbracoVersion));
    }

    [Fact]
    public void An_add_on_engage_first_installs_engage_alone_then_the_add_on()
    {
        var catalog = ReleaseCatalog.Parse(Json(Engages + """
            { "product": "Engage.Forms", "version": "17.1.0", "major": "V17", "engage": "17.5.0", "installFrom": "engage-first" }
            """));
        var stages = catalog.StagesFor(catalog.Select(Products.Forms).Single(), Path.GetFullPath("r"));
        Assert.Equal(2, stages.Count);
        Assert.Null(stages[0].FormsVersion);
        Assert.Equal("17.5.0", stages[0].EngageVersion);
        Assert.Equal("17.1.0", stages[1].FormsVersion);
    }

    [Fact]
    public void Without_install_from_there_is_one_stage()
    {
        var catalog = ReleaseCatalog.Parse(Json(Engages.TrimEnd(',')));
        Assert.Single(catalog.StagesFor(catalog.Select(Products.Engage).First(), Path.GetFullPath("r")));
    }

    [Theory]
    [InlineData("""{ "product": "Engage", "version": "17.1.0", "major": "V17", "installFrom": "17.2.0" }""", "older")]
    [InlineData("""{ "product": "Engage.Forms", "version": "17.1.0", "major": "V17", "engage": "17.5.0", "installFrom": "17.0.4" }""", "engage-first")]
    [InlineData("""{ "product": "Engage.Forms", "version": "17.1.0", "major": "V17", "engage": "17.9.9" }""", "not an Engage release")]
    [InlineData("""{ "product": "Engage", "version": "17.1.0", "major": "V17", "umbraco": "banana" }""", "not a version")]
    public void Bad_entries_are_rejected(string release, string messagePart)
        => Assert.Contains(messagePart, Assert.Throws<InvalidDataException>(() => ReleaseCatalog.Parse(Json("""{ "product": "Engage", "version": "17.5.0", "major": "V17" },""" + release))).Message);

    [Fact]
    public void An_add_on_engage_from_another_major_is_rejected()
    {
        const string json = """
            { "hosts": {
                "V16": { "project": "hosts/Host.V16", "umbraco": "16.5.1", "forms": "16.5.3" },
                "V17": { "project": "hosts/Host.V17", "umbraco": "17.5.3", "forms": "17.6.1" } },
              "releases": [
                { "product": "Engage", "version": "16.3.3", "major": "V16" },
                { "product": "Engage.Forms", "version": "17.0.0", "major": "V17", "engage": "16.3.3" } ] }
            """;
        Assert.Contains("not an Engage release of V17", Assert.Throws<InvalidDataException>(() => ReleaseCatalog.Parse(json)).Message);
    }

    [Fact]
    public void Hosts_that_differ_only_by_case_are_an_InvalidDataException()
    {
        const string json = """
            { "hosts": {
                "V17": { "project": "a", "umbraco": "17.5.3" },
                "v17": { "project": "b", "umbraco": "17.5.3" } }, "releases": [] }
            """;
        Assert.Throws<InvalidDataException>(() => ReleaseCatalog.Parse(json));
    }
}

public class GenerateAllRobustnessTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "hc-rob-" + Guid.NewGuid().ToString("N"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hc-rob-root-" + Guid.NewGuid().ToString("N"));

    public GenerateAllRobustnessTests() => Directory.CreateDirectory(Path.Combine(_root, "hosts", "Host.V17"));

    public void Dispose()
    {
        foreach (var d in new[] { _out, _root }) if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    private const string Json = """
        { "hosts": { "V17": { "project": "hosts/Host.V17", "umbraco": "17.5.3" } },
          "releases": [
            { "product": "Engage", "version": "17.0.0", "major": "V17" },
            { "product": "Engage", "version": "17.1.0", "major": "V17", "installFrom": "17.0.0", "knownFailure": "cannot boot" },
            { "product": "Engage", "version": "17.2.0", "major": "V17" } ] }
        """;

    private static Manifest Fake(string version) => new(1, Products.Engage, version, "fresh", "17.5.3", "k", "s", null, new DatabaseSchema([]));

    private Task<IReadOnlyList<ReleaseResult>> Run(GenerateAllRunner.Generate generate, bool force = false)
    {
        var catalog = ReleaseCatalog.Parse(Json);
        return GenerateAllRunner.RunAsync(catalog, catalog.Select(), _out, force, _root, generate,
            (_, _) => new CheckOutcome(CheckStatus.Match, ""), TextWriter.Null, CancellationToken.None);
    }

    private static Task<IReadOnlyList<Manifest>> Ok(IReadOnlyList<HostSpec> stages, string? from, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Manifest>>([Fake(stages[^1].EngageVersion) with { InstalledFrom = from }]);

    [Fact]
    public async Task A_garbage_existing_manifest_is_a_failed_result_and_the_run_continues()
    {
        var path = Path.Combine(_out, "engage", "17.0.0.fresh.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "this is not json {{{");

        var results = await Run(Ok);

        Assert.Equal(ReleaseStatus.Failed, results.Single(r => r.Release.Version == "17.0.0").Status);
        Assert.Contains("unreadable", results.Single(r => r.Release.Version == "17.0.0").Reason);
        Assert.Equal(ReleaseStatus.Done, results.Single(r => r.Release.Version == "17.2.0").Status);
        Assert.True(GenerateAllRunner.HasFailures(results));
    }

    [Fact]
    public async Task A_known_failure_is_not_retried_and_does_not_fail_the_run_unless_forced()
    {
        var calls = new List<string>();
        Task<IReadOnlyList<Manifest>> Gen(IReadOnlyList<HostSpec> stages, string? from, CancellationToken ct)
        {
            calls.Add(stages[^1].EngageVersion);
            return Ok(stages, from, ct);
        }

        var results = await Run(Gen);
        Assert.DoesNotContain("17.1.0", calls);
        Assert.Equal(ReleaseStatus.KnownFailure, results.Single(r => r.Release.Version == "17.1.0").Status);
        Assert.False(GenerateAllRunner.HasFailures(results));
        Assert.Contains("known failures 1", GenerateAllRunner.Summary(results));

        calls.Clear();
        var forced = await Run((s, f, ct) => throw new InvalidOperationException("still broken"), force: true);
        Assert.Equal(ReleaseStatus.KnownFailure, forced.Single(r => r.Release.Version == "17.1.0").Status);
        Assert.False(forced.Single(r => r.Release.Version == "17.1.0").Reason!.Length == 0);
    }

    [Fact]
    public async Task The_stages_and_installed_from_reach_the_generator()
    {
        IReadOnlyList<HostSpec>? seen = null;
        string? seenFrom = null;
        await Run((stages, from, ct) =>
        {
            if (stages[^1].EngageVersion == "17.2.0") return Ok(stages, from, ct);
            seen = stages; seenFrom = from;
            return Ok(stages, from, ct);
        }, force: true);
        Assert.Equal(["17.0.0", "17.1.0"], seen!.Select(s => s.EngageVersion));
        Assert.Equal("17.0.0", seenFrom);
    }

    [Fact]
    public void All_unverified_prints_a_loud_warning()
    {
        var release = new Release(Products.Engage, "17.0.0", "V17");
        var summary = GenerateAllRunner.Summary([new ReleaseResult(release, ReleaseStatus.Done, null, new CheckOutcome(CheckStatus.Unverified, "no repo"))]);
        Assert.Contains("WARNING", summary);
        var ok = GenerateAllRunner.Summary([new ReleaseResult(release, ReleaseStatus.Done, null, new CheckOutcome(CheckStatus.Match, ""))]);
        Assert.DoesNotContain("WARNING", ok);
    }

    [Fact]
    public void InstalledFrom_is_written_only_when_set()
    {
        Assert.DoesNotContain("installedFrom", ManifestSerializer.Serialize(Fake("17.0.0")));
        Assert.Contains("\"installedFrom\": \"engage-first\"", ManifestSerializer.Serialize(Fake("17.0.0") with { InstalledFrom = "engage-first" }));
    }
}
