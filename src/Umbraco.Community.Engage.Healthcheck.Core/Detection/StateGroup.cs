using Umbraco.Community.Engage.Healthcheck.Core.Manifests;

namespace Umbraco.Community.Engage.Healthcheck.Core.Detection;

/// <summary>Consecutive releases of one product, variant and major that end on the same migration step and schema status with an identical schema.</summary>
public sealed class StateGroup(string product, string variant, int major, IReadOnlyList<string> versions, string? lastMigrationStep, Manifest representative)
{
    public string Product { get; } = product;
    public string Variant { get; } = variant;
    public int Major { get; } = major;

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<string> Versions { get; } = versions;

    public string? LastMigrationStep { get; } = lastMigrationStep;

    /// <summary>The newest manifest in the group; detection and comparison use its schema.</summary>
    public Manifest Representative { get; } = representative;

    public string FirstVersion => Versions[0];
    public string LastVersion => Versions[^1];
    public string RangeText => Versions.Count == 1 ? Versions[0] : $"between {FirstVersion} and {LastVersion}";

    public override string ToString() => $"{Product} {RangeText}";
}
