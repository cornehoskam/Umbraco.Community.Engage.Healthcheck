using Umbraco.Engage.HealthCheck.Core.Comparison;
using Umbraco.Engage.HealthCheck.Core.Detection;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Schema;

namespace Umbraco.Engage.HealthCheck.Core.AddOns;

/// <param name="Manifest">What the add-on tables are compared against.</param>
/// <param name="Version">The version shown in the report; null when neither the record nor the schema names one.</param>
/// <param name="Detection">Null only when a version was asked for and the add-on is not installed.</param>
public sealed record AddOnResolution(
    AddOn AddOn,
    Manifest? Manifest,
    string? Version,
    string Source,
    bool CheckState,
    IReadOnlyList<Finding> Findings,
    DetectionOutcome? Detection);

public static class AddOnResolver
{
    private const string Variant = "fresh";

    /// <summary>Throws when the store has no manifest for the requested add-on version. Needs no database.</summary>
    public static Manifest EnsureRequestedExists(AddOn addOn, string requestedVersion, ManifestStore store)
    {
        var requested = store.Find(addOn.Product, requestedVersion, Variant);
        if (requested is not null) return requested;
        var available = store.Available(addOn.Product).Select(n => n.EndsWith("." + Variant, StringComparison.Ordinal) ? n[..^(Variant.Length + 1)] : n);
        throw new ManifestNotFoundException($"No manifest for {addOn.DisplayName} {requestedVersion}. Available: {string.Join(", ", available)}");
    }

    /// <param name="actual">The add-on's own tables as found in the database.</param>
    /// <param name="majors">Only state groups of these majors are considered; null considers all.</param>
    /// <returns>null when the add-on is not installed and no version was asked for.</returns>
    /// <param name="cmsMajor">The CMS major from the recorded CMS upgrade state, when known; it breaks ties between majors.</param>
    public static AddOnResolution? Resolve(AddOn addOn, string? requestedVersion, string? recordedStep, DatabaseSchema actual, ManifestStore store, IReadOnlyCollection<int>? majors = null, int? cmsMajor = null)
    {
        var installed = recordedStep is not null || actual.Tables.Count > 0;
        // Add-on releases of different majors share step and schema, so detection stays inside the given majors.
        var groups = store.Groups(addOn.Product, Variant).Where(g => majors is null || majors.Contains(g.Major)).ToList();
        var detection = installed ? Detector.Detect(addOn.Product, actual, recordedStep, groups, cmsMajor: cmsMajor) : null;

        if (requestedVersion is not null)
        {
            var requested = EnsureRequestedExists(addOn, requestedVersion, store);
            return new AddOnResolution(addOn, requested, requested.Version, "option", true, [], detection);
        }

        if (detection is null) return null;

        if (detection.RecordGroups.Count > 0)
        {
            var manifest = detection.Group!.Representative;
            return new AddOnResolution(addOn, manifest, manifest.Version, "detected", true, [], detection);
        }

        // The record is missing or unknown here: warn, compare against the schema's best match, skip the state check.
        var finding = Finding.Create(FindingKind.AddOnVersionUnknown, null, addOn.StateKey,
            $"a known {addOn.DisplayName} upgrade step", recordedStep ?? "(missing)", addOn.Product);
        var target = detection.Group?.Representative ?? (groups.Count > 0 ? groups[^1].Representative : null);
        var version = detection.Exact ? detection.Group!.Representative.Version : null;
        return new AddOnResolution(addOn, target, version, "detected", false, [finding], detection);
    }
}
