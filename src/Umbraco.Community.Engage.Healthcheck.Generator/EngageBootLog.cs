using System.Text.RegularExpressions;

namespace Umbraco.Community.Engage.Healthcheck.Generator;

/// <summary>Where one migration plan stands in a boot's log lines.</summary>
public enum PlanRun { NotStarted, Running, Done }

/// <summary>
/// Log patterns that tell the generator how a host boot went. Hosts write console lines as
/// <c>[HH:mm:ss LVL] SourceContext: Message</c> (see the Serilog outputTemplate in each host's appsettings.json).
/// </summary>
/// <remarks>
/// Engage logs nothing on a successful migration, so success is read from the CMS MigrationPlanExecutor
/// (CMS V17 src/Umbraco.Infrastructure/Migrations/MigrationPlanExecutor.cs:190, 220, 264) and the ASP.NET
/// "Application started." line. The plan name "Umbraco.Engage" is the same in every major. The add-ons run their
/// plans ("Umbraco.Engage.Forms", "Umbraco.Engage.UmbracoCommerce") through the same CMS executor. Verified against real boot logs of CMS 13.16.2, 16.5.1, 17.5.3 and 18.2.1: the
/// "Starting '{plan}'..." and "Done" lines read the same in every major. Only the failure line differs: CMS 13 logs
/// "Plan failed at step {TargetState}" without the plan name, CMS 16 and later "Plan {PlanName} failed at step
/// {TargetState}" (templates read from each Umbraco.Infrastructure.dll), so both wordings are in
/// <see cref="PlanFailedWordings"/>.
/// </remarks>
public static partial class EngageBootLog
{
    public const string ApplicationStarted = "Application started.";
    public const string EngagePlan = "Umbraco.Engage";
    public const string EngagePlanStarting = "Starting 'Umbraco.Engage'...";
    private const string PlanPlaceholder = "{plan}";

    /// <summary>Lines that mean the Engage migration failed or Engage refused to start.</summary>
    public static readonly string[] FailureMarkers =
    [
        "Plan Umbraco.Engage failed at step",           // CMS 16+ MigrationPlanExecutor
        "Engage migration plan failed at step",         // Engage 17 and 18
        "Engage schema readiness check failed",         // Engage 17 schema check at startup
        "Failed to initialize Umbraco Engage",          // Engage 13 and 16, missing tables
        "SQL error executing migration",                // Engage 13 and 16 SQL step (logged as Umbraco.Cms...MigrationContext)
        "Error executing migration",                    // Engage 13 and 16, non-SQL exception (same logger)
    ];

    /// <summary>
    /// How the CMS MigrationPlanExecutor says a plan step threw, per CMS major. <c>{plan}</c> stands for the plan name;
    /// a wording without it (CMS 13) is blamed on the plan whose "Starting" line came last before it.
    /// </summary>
    public static readonly string[] PlanFailedWordings =
    [
        "Plan {plan} failed at step",                   // CMS 16, 17, 18
        "Plan failed at step",                          // CMS 13
    ];

    /// <summary>
    /// WRN lines that some hosts repeat on a timer for as long as they run. They are not boot activity, so they must
    /// not keep the generator's quiet window open.
    /// </summary>
    public static readonly string[] PeriodicNoise =
    [
        "Server role is Unknown during NodeLeaseHostedService startup",   // Umbraco Commerce 16, every 10 seconds
        "No umbracoApplicationUrl for service (yet), skip.",              // CMS TouchServerJob, every minute
    ];

    /// <summary>
    /// What Engage 18.1.0 and later log as Critical on every boot while DatabaseSchemaStatus is "Aligned": it disables
    /// itself and the site keeps running. Expected in the aligned variant only. The text is the same in the 18.1.0 and
    /// 18.2.0 boot logs. The "Pending" and unexpected-status texts stay failures.
    /// </summary>
    public static readonly string[] SchemaAlignmentGuidance =
    [
        "Umbraco Engage is disabled: the database schema alignment introduced in v17.2.0 is not complete (current DatabaseSchemaStatus: \"Aligned\").",
    ];

    /// <summary>The CMS MigrationPlanExecutor's message when it starts <paramref name="plan"/>.</summary>
    public static string PlanStarting(string plan) => $"Starting '{plan}'...";

    /// <summary>The CMS 16+ MigrationPlanExecutor's message when a step of <paramref name="plan"/> throws.</summary>
    public static string PlanFailed(string plan) => PlanFailedWordings[0].Replace(PlanPlaceholder, plan);

    /// <summary>A WRN line from <see cref="PeriodicNoise"/>.</summary>
    public static bool IsPeriodicNoise(string line)
        => WarningLine().IsMatch(line) && PeriodicNoise.Any(n => line.Contains(n, StringComparison.Ordinal));

    [GeneratedRegex(@"^\[\d{2}:\d{2}:\d{2} WRN\] ")]
    private static partial Regex WarningLine();

    /// <summary>An ERR or FTL line from any Umbraco.Engage* logger.</summary>
    [GeneratedRegex(@"^\[\d{2}:\d{2}:\d{2} (ERR|FTL)\] (?<logger>Umbraco\.Engage[^\s:]*)")]
    private static partial Regex EngageErrorLine();

    /// <summary>The CMS MigrationPlanExecutor's "Starting" line that begins a run of any plan.</summary>
    [GeneratedRegex(@"^\[\d{2}:\d{2}:\d{2} INF\] \S*MigrationPlanExecutor: Starting '(?<plan>.+)'\.\.\.\s*$")]
    private static partial Regex PlanStartingLine();

    /// <summary>The CMS MigrationPlanExecutor's "Done" line that ends a plan run.</summary>
    [GeneratedRegex(@"^\[\d{2}:\d{2}:\d{2} INF\] \S*MigrationPlanExecutor: Done\s*$")]
    private static partial Regex PlanDoneLine();

    /// <summary>
    /// The first line that means an expected plan failed: an ERR/FTL line from any Umbraco.Engage* logger
    /// (Engage and its add-ons alike), one of the <see cref="FailureMarkers"/>, or the CMS "failed at step"
    /// line of one of the <paramref name="plans"/>. Engage ERR lines that only mean "skipped" fail too, on
    /// purpose: a manifest must come from a boot without Engage errors.
    /// </summary>
    /// <param name="expected">
    /// Engage ERR/FTL texts that this run expects and that are not a failure, such as <see cref="SchemaAlignmentGuidance"/>
    /// for the aligned variant. Null for a fresh run: then every Engage error fails.
    /// </param>
    public static string? FindFailure(IReadOnlyList<string> lines, IReadOnlyCollection<string> plans, IReadOnlyCollection<string>? expected = null)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var engageError = EngageErrorLine().IsMatch(line);
            if (engageError && expected is not null && expected.Any(e => line.Contains(e, StringComparison.Ordinal))) continue;
            if (engageError || FailureMarkers.Any(m => line.Contains(m, StringComparison.Ordinal))) return line;
            if (FailedPlan(lines, i) is { } failed && (failed.Plan is null || plans.Contains(failed.Plan))) return line;
        }
        return null;
    }

    /// <summary>
    /// Like <see cref="FailureSource(string, IReadOnlyCollection{string})"/>, but also places a CMS 13 nameless
    /// "Plan failed at step" line: it blames the plan that started last before it, or "an unnamed plan".
    /// </summary>
    public static string FailureSource(IReadOnlyList<string> lines, string line, IReadOnlyCollection<string> plans)
    {
        var index = lines.ToList().IndexOf(line);
        if (index >= 0 && FailedPlan(lines, index) is { } failed)
            return failed.Plan is null ? "an unnamed plan" : $"plan '{failed.Plan}'";
        return FailureSource(line, plans);
    }

    private sealed record PlanFailure(string? Plan);

    /// <summary>
    /// Whether line <paramref name="index"/> is a CMS "failed at step" line (any of <see cref="PlanFailedWordings"/>),
    /// and for which plan. Plan is null for a nameless line with no "Starting" line before it.
    /// </summary>
    private static PlanFailure? FailedPlan(IReadOnlyList<string> lines, int index)
    {
        var line = lines[index];
        foreach (var wording in PlanFailedWordings)
        {
            var at = wording.IndexOf(PlanPlaceholder, StringComparison.Ordinal);
            if (at >= 0)
            {
                var named = Regex.Match(line, Regex.Escape(wording[..at]) + @"(?<plan>\S+)" + Regex.Escape(wording[(at + PlanPlaceholder.Length)..]));
                if (named.Success) return new PlanFailure(named.Groups["plan"].Value);
            }
            else if (line.Contains(wording, StringComparison.Ordinal))
            {
                for (var i = index - 1; i >= 0; i--)
                    if (StartingPlan(lines[i]) is { } plan) return new PlanFailure(plan);
                return new PlanFailure(null);
            }
        }
        return null;
    }

    /// <summary>
    /// Who a <see cref="FindFailure"/> line blames: the expected plan whose CMS "failed at step" line it is, else the
    /// Umbraco.Engage* logger of an ERR/FTL line, else the Engage plan (the remaining <see cref="FailureMarkers"/>
    /// are all Engage's own).
    /// </summary>
    public static string FailureSource(string line, IReadOnlyCollection<string> plans)
    {
        var plan = plans.FirstOrDefault(p => line.Contains(PlanFailed(p), StringComparison.Ordinal));
        if (plan is not null) return $"plan '{plan}'";
        var error = EngageErrorLine().Match(line);
        return error.Success ? $"logger {error.Groups["logger"].Value}" : $"plan '{EngagePlan}'";
    }

    /// <summary>The <paramref name="plans"/> whose last start has no matching "Done" yet, in the given order.</summary>
    public static IReadOnlyList<string> RunningPlans(IReadOnlyList<string> lines, IEnumerable<string> plans)
        => plans.Where(p => PlanRunOf(lines, p) == PlanRun.Running).ToList();

    public static bool IsPlanDone(string line) => PlanDoneLine().IsMatch(line);

    /// <summary>
    /// A plan is done when its last "Starting" line is followed by a "Done" line before the next "Starting"
    /// line of any plan. The CMS "Done" line does not name its plan, so this is the only safe pairing.
    /// </summary>
    public static PlanRun PlanRunOf(IReadOnlyList<string> lines, string plan)
    {
        var start = -1;
        for (var i = lines.Count - 1; i >= 0 && start < 0; i--)
            if (StartingPlan(lines[i]) == plan) start = i;
        if (start < 0) return PlanRun.NotStarted;

        for (var i = start + 1; i < lines.Count; i++)
        {
            if (IsPlanDone(lines[i])) return PlanRun.Done;
            if (StartingPlan(lines[i]) is not null) return PlanRun.Running;
        }
        return PlanRun.Running;
    }

    private static string? StartingPlan(string line)
    {
        var match = PlanStartingLine().Match(line);
        return match.Success ? match.Groups["plan"].Value : null;
    }
}
