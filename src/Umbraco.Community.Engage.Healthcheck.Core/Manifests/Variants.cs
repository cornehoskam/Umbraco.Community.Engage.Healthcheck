namespace Umbraco.Community.Engage.Healthcheck.Core.Manifests;

/// <summary>Manifest variants: how the database got into the recorded state.</summary>
public static class Variants
{
    /// <summary>A clean install of the release.</summary>
    public const string Fresh = "fresh";

    /// <summary>
    /// An older release upgraded to an align-era release: the automatic AlignSchema step ran, CompleteAlignSchema.sql did
    /// not, and the recorded DatabaseSchemaStatus is "Aligned".
    /// </summary>
    public const string Aligned = "aligned";

    /// <summary>
    /// An aligned database after EnsureDataConsistency.sql and CompleteAlignSchema.sql: status "Complete". Its schema is
    /// not the fresh one (see catalog/known-variants.json), so it has its own manifests.
    /// </summary>
    public const string Complete = "complete";

    /// <summary>The variants detection considers together, in the order a tie between equal schemas prefers them.</summary>
    public static readonly IReadOnlyList<string> Detectable = [Fresh, Complete, Aligned];

    public static bool IsAligned(string? variant) => string.Equals(variant, Aligned, StringComparison.OrdinalIgnoreCase);

    public static bool IsComplete(string? variant) => string.Equals(variant, Complete, StringComparison.OrdinalIgnoreCase);

    public static bool IsFresh(string? variant) => string.Equals(variant, Fresh, StringComparison.OrdinalIgnoreCase);
}
