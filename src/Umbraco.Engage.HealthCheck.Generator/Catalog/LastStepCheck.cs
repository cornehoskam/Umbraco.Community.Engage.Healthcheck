using Umbraco.Engage.HealthCheck.Core.Manifests;

namespace Umbraco.Engage.HealthCheck.Generator.Catalog;

public enum CheckStatus { Match, Mismatch, Unverified }

public sealed record CheckOutcome(CheckStatus Status, string Detail);

/// <summary>Compares a manifest's last migration step with the last step of the plan at the release tag.</summary>
public static class LastStepCheck
{
    public static CheckOutcome Compare(string? manifestStep, string? planText, string? problem)
    {
        if (planText is null) return new CheckOutcome(CheckStatus.Unverified, problem ?? "no plan text");
        if (!MigrationPlanReader.TryLastStep(planText, out var expected, out var unreadable) || expected is null)
            return new CheckOutcome(CheckStatus.Unverified, unreadable ?? "no last step in the plan");
        return string.Equals(expected.Trim(), manifestStep?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? new CheckOutcome(CheckStatus.Match, expected)
            : new CheckOutcome(CheckStatus.Mismatch, $"plan at tag ends at {expected}, manifest says {manifestStep ?? "(none)"}");
    }

    public static CheckOutcome Check(Manifest manifest, Release release, string reposRoot)
    {
        var (text, problem) = MigrationPlanReader.ReadAtTag(reposRoot, release);
        return Compare(manifest.LastMigrationStep, text, problem);
    }
}
