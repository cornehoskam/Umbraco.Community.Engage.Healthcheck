using Microsoft.Data.SqlClient;
using Umbraco.Engage.HealthCheck.Core.Comparison;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Schema;
using Umbraco.Engage.HealthCheck.Core.State;
using Umbraco.Engage.HealthCheck.Generator.Catalog;

namespace Umbraco.Engage.HealthCheck.Generator.Verify;

/// <param name="Differences">The schema differences from the expected manifest to the database (the forward pass).</param>
/// <param name="StateMatches">Whether the recorded last step (and status, when asked) matched.</param>
public sealed record VerifyOutcome(bool Passed, string Summary, IReadOnlyList<string> Details, IReadOnlyList<Finding>? Differences = null, bool StateMatches = true)
{
    public void WriteTo(TextWriter output, string title)
    {
        output.WriteLine($"{title}: {(Passed ? "PASS" : "FAIL")} - {Summary}");
        foreach (var d in Details) output.WriteLine($"  {d}");
    }
}

/// <summary>
/// The verdict on one assumption. <see cref="Assumption"/> is against fresh; a failure that is exactly the recorded
/// known variant is still a failure, but a recorded one (<see cref="IsKnownVariant"/>). <see cref="Complete"/> checks that
/// the stored complete manifest reproduces, when there is one.
/// </summary>
public sealed record AssumptionVerdict(VerifyOutcome Assumption, KnownVariantEntry? Known, VerifyOutcome? Complete)
{
    public const int ExitPass = 0;
    public const int ExitFail = 1;
    public const int ExitKnownVariant = 4;

    /// <summary>The assumption fails only in the schema, with exactly the recorded number of differences per kind.</summary>
    public bool IsKnownVariant => !Assumption.Passed && Assumption.StateMatches && Known is not null && KnownVariants.Matches(Known, Assumption.Differences ?? []);

    /// <summary>0 when the assumption holds, 4 when it fails exactly as recorded, 1 otherwise (also when the complete manifest does not reproduce).</summary>
    public int ExitCode => Complete is { Passed: false } ? ExitFail : Assumption.Passed ? ExitPass : IsKnownVariant ? ExitKnownVariant : ExitFail;

    public string Result => Assumption.Passed ? "PASS" : IsKnownVariant ? "FAIL (known variant, recorded)" : "FAIL";

    public void WriteTo(TextWriter output, string title)
    {
        output.WriteLine($"{title}: {Result} - {Assumption.Summary}");
        foreach (var d in Assumption.Details) output.WriteLine($"  {d}");
        if (Complete is not null)
        {
            output.WriteLine($"complete manifest: {(Complete.Passed ? "PASS" : "FAIL")} (reproducible) - {Complete.Summary}");
            foreach (var d in Complete.Details) output.WriteLine($"  {d}");
        }
    }
}

/// <summary>Checks the "completed = fresh" and "upgraded = fresh" assumptions on a real database.</summary>
public static class Verifier
{
    public const string CompleteStatus = ManifestGenerator.CompleteStatus;

    /// <summary>Like <see cref="CompareWith"/> against a fresh manifest.</summary>
    public static VerifyOutcome CompareWithFresh(DatabaseSchema actual, MigrationState state, Manifest fresh, string? expectStatus)
        => CompareWith(actual, state, fresh, expectStatus);

    /// <summary>
    /// Compares a database with a manifest of the same release (fresh or complete), in both directions, plus the recorded
    /// last step, and the recorded status when <paramref name="expectStatus"/> is given. Any difference fails.
    /// </summary>
    public static VerifyOutcome CompareWith(DatabaseSchema actual, MigrationState state, Manifest expected, string? expectStatus)
    {
        var details = new List<string>();
        var forward = SchemaComparer.Compare(expected.Schema, actual);
        var reverse = SchemaComparer.Compare(actual, expected.Schema);
        details.AddRange(forward.Select(f => Describe(f, "")));
        // The reverse pass mostly mirrors the forward one; it is printed only when the forward pass finds nothing.
        if (forward.Count == 0) details.AddRange(reverse.Select(f => Describe(f, "(reverse) ")));
        var stateMatches = true;
        if (!string.Equals(state.LastStep, expected.LastMigrationStep, StringComparison.OrdinalIgnoreCase))
        {
            stateMatches = false;
            details.Add($"last step: {expected.Variant} {expected.LastMigrationStep ?? "(none)"}, this database {state.LastStep ?? "(none)"}");
        }
        if (expectStatus is not null && !string.Equals(state.SchemaStatus, expectStatus, StringComparison.Ordinal))
        {
            stateMatches = false;
            details.Add($"schema status: expected {expectStatus}, this database {state.SchemaStatus ?? "(none)"}");
        }

        var passed = details.Count == 0;
        var against = $"{expected.Variant} {expected.Version}";
        var summary = passed
            ? $"schema equals {against} both ways, last step {state.LastStep}, status {state.SchemaStatus ?? "(none)"}"
            : $"{forward.Count} difference(s) from {against}, {reverse.Count} the other way; status {state.SchemaStatus ?? "(none)"}";
        return new VerifyOutcome(passed, summary, details, forward, stateMatches);
    }

    /// <summary>
    /// The verdict for a database: the assumption is judged against <paramref name="fresh"/>, the recorded known variant
    /// tells a recorded failure from a new one, and the stored <paramref name="complete"/> manifest, when given, must
    /// reproduce exactly.
    /// </summary>
    public static AssumptionVerdict Judge(DatabaseSchema actual, MigrationState state, Manifest fresh, Manifest? complete, KnownVariantEntry? known, string? expectStatus)
        => new(CompareWith(actual, state, fresh, expectStatus), known, complete is null ? null : CompareWith(actual, state, complete, expectStatus));

    private static string Describe(Finding f, string prefix)
    {
        var where = f.Object is null ? f.Table : $"{f.Table}.{f.Object}";
        var text = $"{prefix}[{f.Severity}] {f.Kind} {where}";
        if (f.Expected is not null) text += $" expected: {f.Expected}";
        if (f.Actual is not null) text += $" actual: {f.Actual}";
        return text;
    }

    /// <summary>
    /// Runs the documented prerequisite EnsureDataConsistency.sql (when the folder has it) and then CompleteAlignSchema.sql
    /// for the release's major from <paramref name="scriptsFolder"/>, in single-database mode on <paramref name="cs"/>.
    /// Returns the path of the CompleteAlignSchema.sql it ran. Throws when the script is missing or a batch fails; the
    /// generator and verify share this one path.
    /// </summary>
    public static async Task<string> RunAlignScriptsAsync(string cs, Release release, string scriptsFolder, TextWriter log, CancellationToken ct)
    {
        var completePath = AlignScripts.Find(scriptsFolder, release.Major, AlignScripts.CompleteAlignSchema)
            ?? throw new InvalidOperationException($"No {AlignScripts.CompleteAlignSchema} for {release.Major} in {scriptsFolder}.");
        var complete = await File.ReadAllTextAsync(completePath, ct);
        if (AlignScripts.SingleDatabaseProblem(complete) is { } problem) throw new InvalidOperationException($"{completePath}: {problem}");

        if (AlignScripts.Find(scriptsFolder, release.Major, AlignScripts.EnsureDataConsistency) is { } ensurePath)
            await RunScriptAsync(cs, await File.ReadAllTextAsync(ensurePath, ct), ensurePath, log, ct);
        else
            log.WriteLine($"Note: no {AlignScripts.EnsureDataConsistency} for {release.Major} in {scriptsFolder}; running {AlignScripts.CompleteAlignSchema} only.");
        await RunScriptAsync(cs, complete, completePath, log, ct);
        return completePath;
    }

    /// <summary>Runs the alignment scripts (<see cref="RunAlignScriptsAsync"/>) on the aligned database, then <see cref="Judge"/>s the result.</summary>
    public static async Task<AssumptionVerdict> CompleteAndJudgeAsync(
        string cs, Release release, string scriptsFolder, Manifest fresh, Manifest? complete, KnownVariantEntry? known, TextWriter log, CancellationToken ct)
    {
        string script;
        try { script = await RunAlignScriptsAsync(cs, release, scriptsFolder, log, ct); }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            return new AssumptionVerdict(new VerifyOutcome(false, $"scripts failed: {ex.Message.ReplaceLineEndings(" ")}", [], null, false), null, null);
        }

        var schema = ManifestGenerator.EngageOnly(await SqlServerSchemaReader.ReadAsync(cs, ct));
        var state = await MigrationStateReader.ReadAsync(cs, ct);
        var verdict = Judge(schema, state, fresh, complete, known, CompleteStatus);
        return verdict with { Assumption = verdict.Assumption with { Summary = $"{verdict.Assumption.Summary}; scripts from {script}" } };
    }

    /// <summary>Runs a script batch by batch on one connection, so its ## temp tables live across batches. PRINT output goes to the log.</summary>
    public static async Task RunScriptAsync(string cs, string sql, string name, TextWriter log, CancellationToken ct)
    {
        var batches = AlignScripts.SplitBatches(sql);
        log.WriteLine($"Running {name} ({batches.Count} batches)");
        await using var conn = new SqlConnection(cs);
        conn.InfoMessage += (_, e) => log.WriteLine($"  sql: {e.Message}");
        await conn.OpenAsync(ct);
        foreach (var batch in batches)
        {
            await using var cmd = new SqlCommand(batch, conn) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
