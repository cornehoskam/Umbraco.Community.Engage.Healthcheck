using Umbraco.Community.Engage.Healthcheck.Core.Manifests;

namespace Umbraco.Community.Engage.Healthcheck.Core.Detection;

/// <summary>
/// A release whose first migration step refuses to run on an aligned database: its plan stops and the Engage schema keeps
/// the older major's aligned shape, while the CMS under it has already moved to the new major.
/// </summary>
public sealed record UpgradeBlock(string Product, string BlockedRelease)
{
    public int BlockedMajor => Version.Parse(BlockedRelease).Major;
}

public static class UpgradeBlocks
{
    /// <summary>
    /// Engage 18.0.0 refuses to upgrade (see the startup log) while DatabaseSchemaStatus is
    /// "Aligned", so the Engage record stays at the 17.2.0 state. From 18.1.0 the check is a startup check instead.
    /// </summary>
    public static readonly IReadOnlyList<UpgradeBlock> Known = [new(Products.Engage, "18.0.0")];

    /// <summary>
    /// The block message, only when the CMS evidence says the blocked major and the Engage schema is the aligned shape of
    /// an older major. Without CMS evidence nothing is said: an aligned 17.2.x database on CMS 17 is not blocked.
    /// </summary>
    public static string? NoticeFor(DetectionOutcome outcome)
    {
        if (outcome.Group is not { } group || !outcome.Exact || !Variants.IsAligned(group.Variant) || outcome.CmsMajor is not { } cms) return null;
        var block = Known.FirstOrDefault(b => b.Product == group.Product && b.BlockedMajor == cms && group.Major < cms);
        return block is null
            ? null
            : $"Engage v{cms} upgrade blocked: CompleteAlignSchema.sql has not been run (Umbraco is on v{cms}, Engage is still on the {group.Major}.x schema)";
    }
}
