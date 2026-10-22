using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Core.Comparison;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Schema;
using Umbraco.Engage.HealthCheck.Core.State;
using Umbraco.Engage.HealthCheck.Generator;
using Umbraco.Engage.HealthCheck.Generator.Catalog;
using Umbraco.Engage.HealthCheck.Generator.Verify;

namespace Umbraco.Engage.HealthCheck.Tests;

public class AlignedCatalogTests
{
    private const string Json = """
        { "hosts": {
            "V17": { "project": "hosts/Host.V17", "umbraco": "17.5.3" },
            "V18": { "project": "hosts/Host.V18", "umbraco": "18.2.1" } },
          "releases": [
            { "product": "Engage", "version": "17.1.1", "major": "V17" },
            { "product": "Engage", "version": "17.2.0", "major": "V17", "alignFrom": "17.1.1" },
            { "product": "Engage", "version": "18.0.0", "major": "V18" },
            { "product": "Engage", "version": "18.1.0", "major": "V18", "alignFrom": "17.1.1" } ] }
        """;

    private static readonly ReleaseCatalog Catalog = ReleaseCatalog.Parse(Json);
    private static Release R(string v) => Catalog.Find(Products.Engage, v)!;

    [Fact]
    public void Aligned_stages_install_the_align_from_release_then_the_release()
    {
        var stages = Catalog.AlignedStagesFor(R("17.2.0"), Path.GetFullPath("r"));
        Assert.Equal(["17.1.1", "17.2.0"], stages.Select(s => s.EngageVersion));
        Assert.All(stages, s => Assert.EndsWith("Host.V17", s.HostProject));
    }

    [Fact]
    public void A_v18_aligned_release_starts_on_the_v17_host_and_cms()
    {
        var stages = Catalog.AlignedStagesFor(R("18.1.0"), Path.GetFullPath("r"));
        Assert.Equal(("17.1.1", "17.5.3"), (stages[0].EngageVersion, stages[0].UmbracoVersion));
        Assert.EndsWith("Host.V17", stages[0].HostProject);
        Assert.Equal(("18.1.0", "18.2.1"), (stages[1].EngageVersion, stages[1].UmbracoVersion));
        Assert.EndsWith("Host.V18", stages[1].HostProject);
    }

    [Fact]
    public void Upgrade_stages_take_any_older_catalog_release()
        => Assert.Equal(["17.1.1", "18.0.0"], Catalog.StagesFrom("17.1.1", R("18.0.0"), Path.GetFullPath("r")).Select(s => s.EngageVersion));

    [Fact]
    public void A_release_without_align_from_has_no_aligned_stages()
        => Assert.Throws<InvalidOperationException>(() => Catalog.AlignedStagesFor(R("18.0.0"), Path.GetFullPath("r")));

    [Theory]
    [InlineData("""{ "product": "Engage", "version": "17.2.0", "major": "V17", "alignFrom": "17.3.0" }""", "older")]
    [InlineData("""{ "product": "Engage", "version": "17.2.0", "major": "V17", "alignFrom": "17.0.0" }""", "not an Engage release")]
    public void Bad_align_from_is_rejected(string release, string messagePart)
        => Assert.Contains(messagePart, Assert.Throws<InvalidDataException>(() => ReleaseCatalog.Parse(
            $$"""{ "hosts": { "V17": { "project": "hosts/Host.V17", "umbraco": "17.5.3" } }, "releases": [ { "product": "Engage", "version": "17.1.1", "major": "V17" }, {{release}} ] }""")).Message);

    [Fact]
    public void The_real_catalog_has_the_17_aligned_releases_and_none_for_16_2_0_or_18_0_0()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "catalog", "releases.json"))) root = Path.GetDirectoryName(root)!;
        var real = ReleaseCatalog.Load(Path.Combine(root, "catalog", "releases.json"));
        var aligned = real.Select(Products.Engage).Where(r => r.AlignFrom is not null).ToList();

        // An upgrade to 16.2.0 records no DatabaseSchemaStatus; 18.0.0 refuses an Aligned database.
        Assert.Equal(17, aligned.Count);
        Assert.Equal(
            ["13.8.0", "13.8.1", "16.3.0", "16.3.1", "16.3.2", "16.3.3", "17.2.0", "17.2.1", "17.2.2", "17.2.3", "17.2.4", "17.3.0", "17.3.1", "17.4.0", "17.5.0", "18.1.0", "18.2.0"],
            aligned.Select(r => r.Version));
        Assert.All(aligned.Where(r => r.Major == "V13"), r => Assert.Equal("13.7.5", r.AlignFrom));
        Assert.All(aligned.Where(r => r.Major == "V16"), r => Assert.Equal("16.1.2", r.AlignFrom));
        Assert.All(aligned.Where(r => r.Major is "V17" or "V18"), r => Assert.Equal("17.1.1", r.AlignFrom));
    }
}

public class AlignedBootLogTests
{
    private const string Guidance =
        "[10:00:00 FTL] Umbraco.Engage.Web.Migrations.EngageMigrationsComponent: Umbraco Engage is disabled: the database schema alignment introduced in v17.2.0 is not complete " +
        "(current DatabaseSchemaStatus: \"Aligned\"). Run the CompleteAlignSchema.sql script against this database during a maintenance window, then restart. See https://docs.umbraco.com/x for instructions.";

    private const string Pending =
        "[10:00:00 FTL] Umbraco.Engage.Web.Migrations.EngageMigrationsComponent: Umbraco Engage is disabled: a previous run of the CompleteAlignSchema.sql script started but did not finish (current DatabaseSchemaStatus: \"Pending\").";

    [Fact]
    public void The_aligned_guidance_is_a_failure_unless_it_is_expected()
    {
        Assert.Equal(Guidance, EngageBootLog.FindFailure([Guidance], ["Umbraco.Engage"]));
        Assert.Null(EngageBootLog.FindFailure([Guidance], ["Umbraco.Engage"], EngageBootLog.SchemaAlignmentGuidance));
    }

    [Fact]
    public void Other_guidance_stays_a_failure_in_the_aligned_variant()
        => Assert.Equal(Pending, EngageBootLog.FindFailure([Guidance, Pending], ["Umbraco.Engage"], EngageBootLog.SchemaAlignmentGuidance));

    [Fact]
    public void Expected_text_in_a_line_from_another_logger_is_not_special()
    {
        // A non-Engage logger is never a failure anyway; an INF Engage line with the text is not either.
        var info = Guidance.Replace("FTL", "INF");
        Assert.Null(EngageBootLog.FindFailure([info], ["Umbraco.Engage"]));
    }
}

public class AlignScriptTests
{
    [Fact]
    public void Batches_split_on_GO_lines_only()
    {
        var sql = "﻿SELECT 1\nGO\nPRINT 'GOOD'\n  go  \r\nSELECT 'GO'\nGO -- end\n\nGO\n";
        Assert.Equal(["SELECT 1", "PRINT 'GOOD'", "SELECT 'GO'"], AlignScripts.SplitBatches(sql));
    }

    [Fact]
    public void A_script_without_the_config_table_is_single_database_only()
        => Assert.Null(AlignScripts.SingleDatabaseProblem("IF OBJECT_ID('[umbracoKeyValue]') IS NULL RAISERROR('x', 16, 1);"));

    [Fact]
    public void A_script_with_the_config_table_must_default_to_single_database_mode()
    {
        Assert.Null(AlignScripts.SingleDatabaseProblem("CREATE TABLE ##EngageAlignConfig ([separateDatabaseMode] BIT NOT NULL);\nINSERT INTO ##EngageAlignConfig VALUES (0);  /* <<< Change to 1 */"));
        Assert.NotNull(AlignScripts.SingleDatabaseProblem("CREATE TABLE ##EngageAlignConfig ([separateDatabaseMode] BIT NOT NULL);\nINSERT INTO ##EngageAlignConfig VALUES (1);"));
    }

    [Fact]
    public void A_script_in_the_major_subfolder_wins_over_one_in_the_folder_itself()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hc-scripts-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "V17"));
            File.WriteAllText(Path.Combine(folder, AlignScripts.CompleteAlignSchema), "flat");
            File.WriteAllText(Path.Combine(folder, "V17", AlignScripts.CompleteAlignSchema), "v17");
            Assert.Equal(Path.Combine(folder, "V17", AlignScripts.CompleteAlignSchema), AlignScripts.Find(folder, "V17", AlignScripts.CompleteAlignSchema));
            Assert.Equal(Path.Combine(folder, AlignScripts.CompleteAlignSchema), AlignScripts.Find(folder, "V13", AlignScripts.CompleteAlignSchema));
            Assert.Null(AlignScripts.Find(folder, "V17", AlignScripts.EnsureDataConsistency));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

public class VerifyComparisonTests
{
    private static readonly DatabaseSchema Schema = SchemaBuilder.Baseline();
    private static readonly Manifest Fresh = new(1, Products.Engage, "17.2.0", "fresh", "17.5.3", MigrationState.StateKey, "{s}", "Complete", Schema);

    [Fact]
    public void Same_schema_and_complete_status_passes()
    {
        var r = Verifier.CompareWithFresh(Schema, new MigrationState(true, "{s}", "Complete"), Fresh, expectStatus: "Complete");
        Assert.True(r.Passed);
        Assert.Empty(r.Details);
    }

    [Fact]
    public void A_schema_difference_fails_and_is_listed()
    {
        var other = SchemaBuilder.Db(Schema.Tables.Select(t => t.Name == "umbracoEngageChild" ? t with { CheckConstraints = [] } : t).ToArray());
        var r = Verifier.CompareWithFresh(other, new MigrationState(true, "{s}", "Complete"), Fresh, expectStatus: "Complete");
        Assert.False(r.Passed);
        Assert.Contains(r.Details, d => d.Contains(nameof(FindingKind.CheckConstraintMissing)) && d.Contains("umbracoEngageChild"));
    }

    [Fact]
    public void An_extra_object_on_the_actual_side_fails_too()
    {
        var extra = SchemaBuilder.Db([.. Schema.Tables, SchemaBuilder.Table("umbracoEngageLeftover", [SchemaBuilder.Col("id")])]);
        Assert.False(Verifier.CompareWithFresh(extra, new MigrationState(true, "{s}", "Complete"), Fresh, expectStatus: "Complete").Passed);
    }

    [Fact]
    public void A_status_that_is_not_complete_fails()
    {
        var r = Verifier.CompareWithFresh(Schema, new MigrationState(true, "{s}", "Pending"), Fresh, expectStatus: "Complete");
        Assert.False(r.Passed);
        Assert.Contains(r.Details, d => d.Contains("Pending"));
    }

    [Fact]
    public void A_different_last_step_fails()
        => Assert.False(Verifier.CompareWithFresh(Schema, new MigrationState(true, "{other}", "Complete"), Fresh, expectStatus: null).Passed);
}

public sealed class AlignedGenerateAllTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "hc-al-" + Guid.NewGuid().ToString("N"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hc-al-root-" + Guid.NewGuid().ToString("N"));

    public AlignedGenerateAllTests() => Directory.CreateDirectory(Path.Combine(_root, "hosts", "Host.V17"));

    public void Dispose()
    {
        foreach (var d in new[] { _out, _root }) if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    private static readonly ReleaseCatalog Catalog = ReleaseCatalog.Parse("""
        { "hosts": { "V17": { "project": "hosts/Host.V17", "umbraco": "17.5.3" } },
          "releases": [
            { "product": "Engage", "version": "17.1.1", "major": "V17" },
            { "product": "Engage", "version": "17.2.0", "major": "V17", "alignFrom": "17.1.1", "knownFailure": "fresh only" } ] }
        """);

    [Fact]
    public async Task Aligned_runs_only_align_releases_with_their_stages_and_writes_the_aligned_file()
    {
        var seen = new List<(string[] Stages, string? From)>();
        var results = await GenerateAllRunner.RunAsync(Catalog, Catalog.Select(), _out, false, _root,
            (stages, from, ct) =>
            {
                seen.Add((stages.Select(s => s.EngageVersion).ToArray(), from));
                return Task.FromResult<IReadOnlyList<Manifest>>([new(1, Products.Engage, stages[^1].EngageVersion, Variants.Aligned, "17.5.3", "k", "s", "Aligned", SchemaBuilder.Baseline(), null, from)]);
            },
            (_, _) => new CheckOutcome(CheckStatus.Match, ""), TextWriter.Null, CancellationToken.None, Variants.Aligned);

        var only = Assert.Single(results);
        Assert.Equal(ReleaseStatus.Done, only.Status);
        var run = Assert.Single(seen);
        Assert.Equal(["17.1.1", "17.2.0"], run.Stages);
        Assert.Equal("17.1.1", run.From);
        Assert.True(File.Exists(Path.Combine(_out, "engage", "17.2.0.aligned.json")));
    }
}

public sealed class CompleteGenerateAllTests : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "hc-co-" + Guid.NewGuid().ToString("N"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hc-co-root-" + Guid.NewGuid().ToString("N"));

    public CompleteGenerateAllTests() => Directory.CreateDirectory(Path.Combine(_root, "hosts", "Host.V17"));

    public void Dispose()
    {
        foreach (var d in new[] { _out, _root }) if (Directory.Exists(d)) Directory.Delete(d, true);
    }

    [Fact]
    public async Task Complete_uses_the_aligned_stages_and_writes_the_complete_file()
    {
        var catalog = ReleaseCatalog.Parse("""
            { "hosts": { "V17": { "project": "hosts/Host.V17", "umbraco": "17.5.3" } },
              "releases": [
                { "product": "Engage", "version": "17.1.1", "major": "V17" },
                { "product": "Engage", "version": "17.2.0", "major": "V17", "alignFrom": "17.1.1" } ] }
            """);
        string? seenFrom = null;
        var results = await GenerateAllRunner.RunAsync(catalog, catalog.Select(), _out, false, _root,
            (stages, from, ct) =>
            {
                seenFrom = from;
                return Task.FromResult<IReadOnlyList<Manifest>>([new(1, Products.Engage, stages[^1].EngageVersion, Variants.Complete, "17.5.3", "k", "s", "Complete", SchemaBuilder.Baseline(), null, from)]);
            },
            (_, _) => new CheckOutcome(CheckStatus.Match, ""), TextWriter.Null, CancellationToken.None, Variants.Complete);

        Assert.Equal(ReleaseStatus.Done, Assert.Single(results).Status);
        Assert.Equal("17.1.1", seenFrom);
        Assert.True(File.Exists(Path.Combine(_out, "engage", "17.2.0.complete.json")));
    }
}

public class AlignScriptGoCountTests
{
    [Fact]
    public void A_GO_with_a_repeat_count_is_not_supported_and_stays_in_the_batch()
    {
        // sqlcmd's "GO n" runs a batch n times. No published align script uses it, so it is not a separator here:
        // the line stays in the batch and SQL Server rejects it, which fails loudly instead of running a batch once.
        Assert.Equal(["SELECT 1\nGO 2"], AlignScripts.SplitBatches("SELECT 1\nGO 2\n"));
    }
}

public class VerdictTests
{
    private static readonly DatabaseSchema Fresh = SchemaBuilder.Baseline();
    private static readonly DatabaseSchema Complete = SchemaBuilder.Db(Fresh.Tables.Select(t => t.Name == "umbracoEngageChild" ? t with { Indexes = [] } : t).ToArray());
    private static Manifest M(string variant, DatabaseSchema schema) => new(1, Products.Engage, "17.4.0", variant, "x", MigrationState.StateKey, "S", "Complete", schema);
    private static readonly MigrationState Done = new(true, "S", "Complete");

    private static KnownVariantEntry Entry(int differences, params (string Kind, int Count)[] kinds)
        => new(Products.Engage, ["17.4.0"], KnownVariants.CompleteEqualsFresh, false, "complete", "17.1.1", differences, kinds.ToDictionary(k => k.Kind, k => k.Count), "r", []);

    [Fact]
    public void Equal_to_fresh_passes_with_exit_0()
    {
        var v = Verifier.Judge(Fresh, Done, M("fresh", Fresh), null, null, "Complete");
        Assert.Equal(("PASS", 0), (v.Result, v.ExitCode));
    }

    [Fact]
    public void The_recorded_difference_is_a_known_variant_with_exit_4_and_the_complete_manifest_reproduces()
    {
        var v = Verifier.Judge(Complete, Done, M("fresh", Fresh), M("complete", Complete), Entry(1, ("IndexMissing", 1)), "Complete");
        Assert.Equal(("FAIL (known variant, recorded)", 4), (v.Result, v.ExitCode));
        var sw = new StringWriter();
        v.WriteTo(sw, "verify 17.4.0");
        Assert.Contains("verify 17.4.0: FAIL (known variant, recorded)", sw.ToString());
        Assert.Contains("complete manifest: PASS (reproducible)", sw.ToString());
    }

    [Theory]
    [InlineData(2, "IndexMissing", 2)]           // another count
    [InlineData(1, "ColumnDefaultDifferent", 1)] // another kind
    public void A_difference_that_is_not_the_recorded_one_fails_with_exit_1(int total, string kind, int count)
    {
        var v = Verifier.Judge(Complete, Done, M("fresh", Fresh), null, Entry(total, (kind, count)), "Complete");
        Assert.Equal(("FAIL", 1), (v.Result, v.ExitCode));
    }

    [Fact]
    public void A_status_that_is_not_complete_is_never_a_known_variant()
    {
        var v = Verifier.Judge(Complete, new MigrationState(true, "S", "Pending"), M("fresh", Fresh), null, Entry(1, ("IndexMissing", 1)), "Complete");
        Assert.Equal(1, v.ExitCode);
    }

    [Fact]
    public void A_complete_manifest_that_does_not_reproduce_fails_with_exit_1()
    {
        var v = Verifier.Judge(Complete, Done, M("fresh", Fresh), M("complete", Fresh), Entry(1, ("IndexMissing", 1)), "Complete");
        Assert.Equal(1, v.ExitCode);
        var sw = new StringWriter();
        v.WriteTo(sw, "verify 17.4.0");
        Assert.Contains("complete manifest: FAIL (reproducible)", sw.ToString());
    }
}
