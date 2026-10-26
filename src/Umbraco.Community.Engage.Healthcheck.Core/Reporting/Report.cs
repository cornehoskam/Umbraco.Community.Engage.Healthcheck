using System.Reflection;
using Umbraco.Community.Engage.Healthcheck.Core.Comparison;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.State;

namespace Umbraco.Community.Engage.Healthcheck.Core.Reporting;

public sealed record AddOnResult(string Product, string DisplayName, string? Version, string Source, string? LastStep, DetectionResult? Detection = null);

public sealed record ReportSummary(int Critical, int Warning, int Info);

public sealed record Report(
    int FormatVersion,
    string ToolVersion,
    DateTimeOffset CheckedAtUtc,
    string TargetVersion,
    string TargetVariant,
    string TargetSource,
    DetectionResult? Detection,
    MigrationState State,
    IReadOnlyList<AddOnResult> AddOns,
    ReportSummary Summary,
    IReadOnlyList<Finding> Findings)
{
    public const int CurrentFormatVersion = 1;

    /// <summary>The target came from --expect.</summary>
    public const string FromOption = "option";

    /// <summary>The target is the newest release of the detected state group.</summary>
    public const string FromDetection = "detected";

    public int ExitCode => Summary.Critical > 0 ? ExitCodes.Critical : Summary.Warning > 0 ? ExitCodes.Warnings : ExitCodes.Healthy;

    public static Report Create(Manifest manifest, MigrationState state, IReadOnlyList<AddOnResult> addOns, IReadOnlyList<Finding> findings, DateTimeOffset now,
        DetectionResult? detection = null, string targetSource = FromOption) => new(
        CurrentFormatVersion,
        Banner.CurrentVersion(typeof(Report).Assembly),
        now.ToUniversalTime(),
        manifest.Version,
        manifest.Variant,
        targetSource,
        detection,
        state,
        addOns,
        new ReportSummary(
            findings.Count(f => f.Severity == Severity.Critical),
            findings.Count(f => f.Severity == Severity.Warning),
            findings.Count(f => f.Severity == Severity.Info)),
        findings);
}
