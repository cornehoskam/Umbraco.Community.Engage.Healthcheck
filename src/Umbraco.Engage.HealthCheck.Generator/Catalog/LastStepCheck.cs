using Umbraco.Engage.HealthCheck.Core.Manifests;

namespace Umbraco.Engage.HealthCheck.Generator.Catalog;

public enum CheckStatus { Match, Mismatch, Unverified }

public sealed record CheckOutcome(CheckStatus Status, string Detail);

/// <summary>Compares a manifest's last migration step with the last step of the plan in the release's NuGet package.</summary>
public static class LastStepCheck
{
    public static CheckOutcome Compare(string? manifestStep, string? planStep, string? problem)
    {
        if (planStep is null) return new CheckOutcome(CheckStatus.Unverified, problem ?? "no last step in the plan");
        return string.Equals(planStep.Trim(), manifestStep?.Trim(), StringComparison.OrdinalIgnoreCase)
            ? new CheckOutcome(CheckStatus.Match, planStep)
            : new CheckOutcome(CheckStatus.Mismatch, $"package plan ends at {planStep}, manifest says {manifestStep ?? "(none)"}");
    }

    public static CheckOutcome Check(Manifest manifest, Release release, string nugetRoot)
    {
        var (step, problem) = PackagePlanReader.LastStep(nugetRoot, release);
        return Compare(manifest.LastMigrationStep, step, problem);
    }
}
