using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.AddOns;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Reporting;
using Umbraco.Community.Engage.Healthcheck.Generator;
using Umbraco.Community.Engage.Healthcheck.Generator.Catalog;
using Umbraco.Community.Engage.Healthcheck.Generator.Verify;
using Xunit.Abstractions;

namespace Umbraco.Community.Engage.Healthcheck.Tests.EndToEnd;

/// <summary>
/// Every catalog release, provisioned on a real database the way the catalog says (host, CMS, installFrom staging,
/// add-on hosting), then checked with the real runner: no --expect, embedded manifests. Plus one aligned and one
/// complete case per major. Takes about two hours, so it has its own category:
/// <c>dotnet test --filter "Category=E2EMatrix"</c>. Results go to <c>docs/verification/all-releases-&lt;dd-MM-yyyy&gt;.md</c>.
/// </summary>
[Trait("Category", "E2EMatrix")]
[Collection("E2E")]
public class AllReleasesMatrixTests(AllReleasesMatrixTests.MatrixFixture fixture, ITestOutputHelper output)
    : IClassFixture<AllReleasesMatrixTests.MatrixFixture>
{
    private static readonly Lazy<string> RootPath = new(() =>
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "catalog", "releases.json"))) dir = Path.GetDirectoryName(dir)!;
        return dir;
    });

    private static readonly Lazy<ReleaseCatalog> CatalogValue = new(() => ReleaseCatalog.Load(Path.Combine(RootPath.Value, "catalog", "releases.json")));
    private static readonly ManifestStore Store = new(null);

    private static string Root => RootPath.Value;
    private static ReleaseCatalog Catalog => CatalogValue.Value;

    /// <summary>Every catalog release, Engage first, then Forms and Commerce, each in version order.</summary>
    public static TheoryData<string, string> AllReleases()
    {
        var data = new TheoryData<string, string>();
        foreach (var r in Catalog.Releases.OrderBy(r => ResultRow.ProductOrder(r.Product)).ThenBy(r => r.Parsed)) data.Add(r.Product, r.Version);
        return data;
    }

    /// <summary>The newest catalog release with an alignFrom in each major, in the aligned and the complete variant.</summary>
    public static TheoryData<string, string> AlignedAndComplete()
    {
        var data = new TheoryData<string, string>();
        foreach (var r in NewestAlignReleases())
        {
            data.Add(r.Version, Variants.Aligned);
            data.Add(r.Version, Variants.Complete);
        }
        return data;
    }

    private static IEnumerable<Release> NewestAlignReleases()
        => Catalog.Select(Products.Engage).Where(r => r.AlignFrom is not null).GroupBy(r => r.Parsed.Major).Select(g => g.MaxBy(r => r.Parsed)!).OrderBy(r => r.Parsed);

    [Fact]
    public void The_alignment_cases_are_the_newest_align_release_of_each_major()
        => Assert.Equal(["13.8.1", "16.3.3", "17.5.0", "18.2.0"], NewestAlignReleases().Select(r => r.Version));

    [Theory]
    [MemberData(nameof(AllReleases))]
    public async Task Release_is_detected_on_a_real_database(string product, string version)
    {
        var release = Catalog.Find(product, version)!;
        var stages = Catalog.StagesFor(release, Root);
        var target = stages[^1];
        var problems = new List<string>();
        var clock = Stopwatch.StartNew();
        Report? report = null;
        string detected = "-";

        var database = $"m_{Products.Folder(product)}_{version.Replace('.', '_')}";
        try
        {
            var cs = await (await fixture.ProvisionerAsync()).ProvisionAsync(stages, database);
            try
            {
                report = await HealthCheckRunner.RunAsync(new HealthCheckOptions(cs, null, null, null, null));
                var cmsState = await ReadKeyAsync(cs, CmsStates.StateKey);
                var console = Render(report);
                output.WriteLine(console);
                detected = release.IsAddOn ? AddOnLine(report, product) : ConsoleReporter.Describe("Engage", report.Detection!, report.State.LastStep);
                if (release.IsAddOn) CheckAddOn(release, report, problems);
                else CheckEngage(release, report, cmsState, console, problems);
            }
            finally { await fixture.DropAsync(database); }
        }
        catch (Exception ex)
        {
            problems.Add($"{ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
        }

        var staged = release.InstallFrom is null ? "no" : release.IsAddOn ? "Engage first" : $"from {release.InstallFrom}";
        Record(new ResultRow(product, version, Variants.Fresh, $"{target.UmbracoVersion} ({release.Major})", staged, detected,
            report?.Detection?.CmsMajor, report?.ExitCode, problems, clock.Elapsed));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [MemberData(nameof(AlignedAndComplete))]
    public async Task Aligned_and_complete_databases_report_their_variant(string version, string variant)
    {
        var release = Catalog.Find(Products.Engage, version)!;
        var complete = Variants.IsComplete(variant);
        var problems = new List<string>();
        var clock = Stopwatch.StartNew();
        Report? report = null;
        string detected = "-";
        try
        {
            string? scripts = null;
            if (complete)
            {
                scripts = Environment.GetEnvironmentVariable(AlignScripts.FolderVariable);
                if (string.IsNullOrWhiteSpace(scripts))
                    throw new InvalidOperationException($"The complete case runs CompleteAlignSchema.sql. Download it from the Engage schema alignment guide and set {AlignScripts.FolderVariable} to its folder.");
            }
            var scriptLog = new StringWriter();
            string? cmsState = null;
            string console = "";
            // The generator's own aligned staging (alignFrom, then the release). For complete, its Verifier script path runs
            // the published EnsureDataConsistency.sql and CompleteAlignSchema.sql; the generator checks the status.
            await ManifestGenerator.GenerateAsync(Catalog.AlignedStagesFor(release, Root), release.AlignFrom, variant,
                async (cs, ct) =>
                {
                    report = await HealthCheckRunner.RunAsync(new HealthCheckOptions(cs, null, null, null, null), ct);
                    cmsState = await ReadKeyAsync(cs, CmsStates.StateKey);
                    console = Render(report);
                },
                CancellationToken.None,
                databaseName: $"m_{variant}_{version.Replace('.', '_')}",
                completeAlignment: complete ? (cs, ct) => Verifier.RunAlignScriptsAsync(cs, release, scripts!, scriptLog, ct) : null);
            output.WriteLine(scriptLog.ToString());
            output.WriteLine(console);

            var d = report!.Detection!;
            detected = ConsoleReporter.Describe("Engage", d, report.State.LastStep);
            if (!d.Exact) problems.Add($"not an exact match: {detected}");
            if (!string.Equals(d.Variant, variant, StringComparison.OrdinalIgnoreCase)) problems.Add($"variant is {d.Variant ?? "(none)"}, not {variant}");
            CheckGroup(Products.Engage, variant, version, d, problems);
            CheckCmsMajor(release, d, cmsState, problems);
            if (!d.RecordMatchesSchema) problems.Add("the upgrade record does not match the schema");
            if (!complete && release.Parsed.Major >= 18
                && !console.Split('\n').Any(l => l.Trim() == "Engage is disabled until CompleteAlignSchema.sql is run"))
                problems.Add("missing the line \"Engage is disabled until CompleteAlignSchema.sql is run\"");
        }
        catch (Exception ex)
        {
            problems.Add($"{ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
        }

        var host = Catalog.SpecFor(release, Root).UmbracoVersion;
        Record(new ResultRow(Products.Engage, version, variant, $"{host} ({release.Major})", $"from {release.AlignFrom}", detected,
            report?.Detection?.CmsMajor, report?.ExitCode, problems, clock.Elapsed));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private void Record(ResultRow row)
    {
        fixture.Rows.Enqueue(row);
        output.WriteLine(row.ToMarkdown());
    }

    private static void CheckEngage(Release release, Report report, string? cmsState, string console, List<string> problems)
    {
        var d = report.Detection!;
        if (!d.Exact) problems.Add($"not an exact match: {ConsoleReporter.Describe("Engage", d, report.State.LastStep)}");
        if (!Variants.IsFresh(d.Variant)) problems.Add($"variant is {d.Variant ?? "(none)"}, not fresh");
        var group = CheckGroup(Products.Engage, Variants.Fresh, release.Version, d, problems);
        if (!d.RecordMatchesSchema) problems.Add("the upgrade record does not match the schema");
        CheckCmsMajor(release, d, cmsState, problems);
        if (report.ExitCode != ExitCodes.Healthy) problems.Add($"exit code {report.ExitCode}, not 0");
        if (group is { Versions.Count: > 1 } && !console.Contains($"between {group.FirstVersion} and {group.LastVersion}", StringComparison.Ordinal))
            problems.Add($"the console output does not say \"between {group.FirstVersion} and {group.LastVersion}\"");
    }

    private static void CheckAddOn(Release release, Report report, List<string> problems)
    {
        var addOn = report.AddOns.SingleOrDefault(a => a.Product == release.Product);
        if (addOn is null) problems.Add($"{release.Product} is not in AddOns");
        else
        {
            if (addOn.Source != Report.FromDetection) problems.Add($"{release.Product} source is {addOn.Source}, not detected");
            if (addOn.Detection is null) problems.Add($"{release.Product} has no detection");
            else CheckGroup(release.Product, Variants.Fresh, release.Version, addOn.Detection, problems);
        }
        if (report.Detection is not { Exact: true }) problems.Add($"Engage is not an exact match: {(report.Detection is null ? "(none)" : ConsoleReporter.Describe("Engage", report.Detection, report.State.LastStep))}");
        if (report.ExitCode != ExitCodes.Healthy) problems.Add($"exit code {report.ExitCode}, not 0");
    }

    /// <summary>The detected group must be the shipped state group that holds <paramref name="version"/>.</summary>
    private static StateGroup? CheckGroup(string product, string variant, string version, DetectionResult d, List<string> problems)
    {
        var group = Store.Groups(product, variant).SingleOrDefault(g => g.Versions.Contains(version));
        if (group is null)
        {
            problems.Add($"no {variant} state group of {product} holds {version}");
            return null;
        }
        if (d.FirstVersion != group.FirstVersion || d.LastVersion != group.LastVersion)
            problems.Add($"detected {product} {d.Versions ?? "(none)"}, but {version} is in the group {group.RangeText}");
        return group;
    }

    /// <summary>The CMS major must come from the database: the recorded CMS upgrade state, mapped to the release's major.</summary>
    private static void CheckCmsMajor(Release release, DetectionResult d, string? cmsState, List<string> problems)
    {
        var major = release.Parsed.Major;
        if (cmsState is null) problems.Add("no CMS upgrade state in umbracoKeyValue");
        else if (CmsStates.MajorOf(cmsState) != major) problems.Add($"the recorded CMS state maps to CMS {CmsStates.MajorOf(cmsState)?.ToString() ?? "(unknown)"}, not {major}");
        if (d.CmsMajor != major) problems.Add($"cmsMajor is {d.CmsMajor?.ToString() ?? "(none)"}, not {major}");
    }

    private static string AddOnLine(Report report, string product)
        => report.AddOns.SingleOrDefault(a => a.Product == product) is { Detection: { } detection } a
            ? ConsoleReporter.Describe(a.DisplayName, detection, a.LastStep)
            : "(not detected)";

    private static string Render(Report report)
    {
        var writer = new StringWriter();
        ConsoleReporter.Write(report, writer, new ConsoleContext("matrix database", null, null));
        return writer.ToString();
    }

    private static async Task<string?> ReadKeyAsync(string cs, string key)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT [value] FROM umbracoKeyValue WHERE [key] = @key", conn);
        cmd.Parameters.AddWithValue("@key", key);
        return await cmd.ExecuteScalarAsync() as string;
    }

    /// <summary>One SQL Server container for the fresh matrix, a new database per release. Writes the results file at the end.</summary>
    public sealed class MatrixFixture : IAsyncLifetime
    {
        private readonly Stopwatch _clock = new();
        private EngageDatabaseProvisioner? _provisioner;
        private readonly SemaphoreSlim _start = new(1, 1);

        public ConcurrentQueue<ResultRow> Rows { get; } = new();

        /// <summary>Started on first use, so a run of only the alignment cases (which use the generator's own container) starts none.</summary>
        public async Task<EngageDatabaseProvisioner> ProvisionerAsync()
        {
            await _start.WaitAsync();
            try { return _provisioner ??= await EngageDatabaseProvisioner.StartAsync(); }
            finally { _start.Release(); }
        }

        public Task InitializeAsync()
        {
            _clock.Start();
            return Task.CompletedTask;
        }

        public async Task DropAsync(string database)
        {
            if (_provisioner is null) return;
            try
            {
                SqlConnection.ClearAllPools();
                await using var conn = new SqlConnection(_provisioner.ConnectionStringFor("master"));
                await conn.OpenAsync();
                await using var cmd = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];", conn);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException) { /* Only frees space in the throwaway container. */ }
        }

        public async Task DisposeAsync()
        {
            try
            {
                if (!Rows.IsEmpty) ResultsFile.Write(Path.Combine(Root, "docs", "verification"), Rows.ToList(), _clock.Elapsed);
            }
            finally
            {
                if (_provisioner is not null) await _provisioner.DisposeAsync();
            }
        }
    }

    public sealed record ResultRow(
        string Product, string Version, string Variant, string HostCms, string Staged, string Detected,
        int? CmsMajor, int? ExitCode, IReadOnlyList<string> Problems, TimeSpan Duration)
    {
        public bool Passed => Problems.Count == 0;

        public static int ProductOrder(string product) => product switch { Products.Engage => 0, Products.Forms => 1, _ => 2 };

        public static string DisplayName(string product) => product switch
        {
            Products.Forms => AddOnCatalog.Forms.DisplayName,
            Products.Commerce => AddOnCatalog.Commerce.DisplayName,
            _ => "Engage",
        };

        public string VersionCell => Variants.IsFresh(Variant) ? Version : $"{Version} ({Variant})";

        public string ToMarkdown()
        {
            var result = Passed ? "pass" : "FAIL: " + string.Join("; ", Problems);
            if (result.Length > 400) result = result[..400] + "...";
            string[] cells =
            [
                DisplayName(Product), VersionCell, HostCms, Staged, Detected,
                CmsMajor?.ToString(CultureInfo.InvariantCulture) ?? "-", ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-",
                result, $"{(int)Duration.TotalMinutes}:{Duration.Seconds:00}",
            ];
            return "| " + string.Join(" | ", cells.Select(Cell)) + " |";
        }

        private static string Cell(string text) => text.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal);
    }

    /// <summary>
    /// The markdown results file for today. A rerun of some cases replaces their rows and keeps the others, so a rerun
    /// of only the failed releases still leaves one complete table.
    /// </summary>
    public static class ResultsFile
    {
        public const string Command = "dotnet test --filter \"Category=E2EMatrix\"";
        private const string TableHeader = "| Product | Version | Host CMS | Staged? | Detected | cmsMajor | Exit code | Result | Duration |";

        public static void Write(string folder, IReadOnlyList<ResultRow> rows, TimeSpan runTime)
        {
            Directory.CreateDirectory(folder);
            var date = DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            var path = Path.Combine(folder, $"all-releases-{date}.md");

            // Key: product and version cell. New rows win over the ones already in the file.
            var lines = new Dictionary<string, string>(StringComparer.Ordinal);
            if (File.Exists(path))
            {
                foreach (var line in File.ReadAllLines(path).Where(l => l.StartsWith("| ", StringComparison.Ordinal) && l != TableHeader))
                {
                    var cells = line.Split(" | ");
                    if (cells.Length >= 9 && !cells[0].StartsWith("| ---", StringComparison.Ordinal)) lines[Key(cells[0][2..], cells[1])] = line;
                }
            }
            foreach (var row in rows) lines[Key(ResultRow.DisplayName(row.Product), row.VersionCell)] = row.ToMarkdown();

            var ordered = lines.OrderBy(kv => SortKey(kv.Key)).Select(kv => kv.Value).ToList();
            var passed = ordered.Count(l => l.Contains(" | pass | ", StringComparison.Ordinal));

            var sb = new StringBuilder();
            sb.AppendLine($"# All releases verification, {date}");
            sb.AppendLine();
            sb.AppendLine("Produced by `AllReleasesMatrixTests` (tests/Umbraco.Community.Engage.Healthcheck.Tests/EndToEnd). For every release in `catalog/releases.json` it provisions a real SQL Server database the way the catalog says: the release's host and CMS version, `installFrom` staging, and add-ons installed next to their `engage` release (Engage first for `engage-first`). It then runs the real health check with no `--expect` and the embedded manifests, and checks the detection, the variant, the upgrade record, the CMS major read from the database and the exit code. The aligned and complete rows use the generator's aligned staging, and for complete the alignment scripts.");
            sb.AppendLine();
            sb.AppendLine("Command (the complete rows need `ENGAGE_ALIGN_SCRIPTS`, the folder with the alignment scripts from the Engage schema alignment guide):");
            sb.AppendLine();
            sb.AppendLine("```bash");
            sb.AppendLine(Command);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine($"Result: {passed} of {ordered.Count} passed. Last run took {(int)runTime.TotalHours}:{runTime.Minutes:00}:{runTime.Seconds:00} for {rows.Count} case(s).");
            sb.AppendLine();
            sb.AppendLine(TableHeader);
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var line in ordered) sb.AppendLine(line);
            File.WriteAllText(path, sb.ToString().ReplaceLineEndings("\n"));
        }

        private static string Key(string product, string versionCell) => $"{product.Trim()}|{versionCell.Trim()}";

        private static (int, int, Version, int) SortKey(string key)
        {
            var (product, versionCell) = (key.Split('|')[0], key.Split('|')[1]);
            var productOrder = product == AddOnCatalog.Forms.DisplayName ? 1 : product == AddOnCatalog.Commerce.DisplayName ? 2 : 0;
            var space = versionCell.IndexOf(' ');
            var version = System.Version.TryParse(space < 0 ? versionCell : versionCell[..space], out var v) ? v : new Version(0, 0);
            var variant = versionCell.Contains("(aligned)", StringComparison.Ordinal) ? 1 : versionCell.Contains("(complete)", StringComparison.Ordinal) ? 2 : 0;
            // Fresh rows first, then the aligned and complete rows together.
            return (variant == 0 ? 0 : 1, productOrder, version, variant);
        }
    }
}
