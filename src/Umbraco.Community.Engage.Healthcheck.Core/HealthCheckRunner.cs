using Umbraco.Community.Engage.Healthcheck.Core.AddOns;
using Umbraco.Community.Engage.Healthcheck.Core.Comparison;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Reporting;
using Umbraco.Community.Engage.Healthcheck.Core.Schema;
using Umbraco.Community.Engage.Healthcheck.Core.State;

namespace Umbraco.Community.Engage.Healthcheck.Core;

/// <param name="ExpectVersion">The Engage version to check against; null detects it from the database.</param>
/// <param name="Variant">
/// The manifest variant. Null means not given: fresh, except that with <paramref name="ExpectVersion"/> the detected variant
/// is used when the expected release has a manifest in that variant.
/// </param>
public sealed record HealthCheckOptions(string EngageConnectionString, string? UmbracoConnectionString, string? ExpectVersion, string? Variant, string? ManifestFolder, IReadOnlyDictionary<string, string>? AddOnVersions = null, bool IncludeEmbeddedManifests = true);

public sealed class ManifestNotFoundException(string message) : Exception(message);

public sealed class NoEngageTablesException()
    : Exception("No Engage tables found in this database. Check the --engage-db connection string and that the login has VIEW DEFINITION.");

public static class HealthCheckRunner
{
    public static async Task<Report> RunAsync(HealthCheckOptions options, CancellationToken ct = default)
    {
        var store = new ManifestStore(options.ManifestFolder, options.IncludeEmbeddedManifests);
        var variant = options.Variant ?? Variants.Fresh;
        var expected = options.ExpectVersion is null ? null
            : store.Find(Products.Engage, options.ExpectVersion, variant)
              ?? throw new ManifestNotFoundException($"No manifest for Engage {options.ExpectVersion} ({variant}). Available: {string.Join(", ", store.Available(Products.Engage))}");

        // Refuse an unknown requested add-on version before touching the database.
        foreach (var addOn in AddOnCatalog.All)
        {
            if (options.AddOnVersions is not null && options.AddOnVersions.TryGetValue(addOn.Product, out var wanted) && wanted is not null)
                AddOnResolver.EnsureRequestedExists(addOn, wanted, store);
        }

        // Two separate connections on purpose: never join across the Engage and Umbraco databases.
        var schema = await SqlServerSchemaReader.ReadAsync(options.EngageConnectionString, ct);
        // Wrong database or no VIEW DEFINITION: comparing would report every table as missing. Add-on tables alone don't count.
        if (!schema.Tables.Any(t => !AddOnCatalog.OwnsTable(t.Name))) throw new NoEngageTablesException();
        var keys = await MigrationStateReader.ReadKeysAsync(
            options.UmbracoConnectionString ?? options.EngageConnectionString,
            [MigrationState.StateKey, MigrationState.SchemaStatusKey, CmsStates.StateKey, .. AddOnCatalog.All.Select(a => a.StateKey)], ct);
        // The CMS upgrade state tells CMS majors apart, which some Engage states cannot; an unknown state is no evidence.
        var cmsMajor = CmsStates.MajorOf(keys.Get(CmsStates.StateKey));
        var state = keys.TableFound
            ? new MigrationState(true, keys.Get(MigrationState.StateKey), keys.Get(MigrationState.SchemaStatusKey))
            : new MigrationState(false, null, null);

        // Add-on tables belong to the add-ons, never to Engage.
        var engageActual = new DatabaseSchema(schema.Tables.Where(t => !AddOnCatalog.OwnsTable(t.Name)).ToList());

        // Detection runs either way; --expect only decides what the findings are measured against.
        var detectionGroups = DetectionGroups(store, variant);
        var detection = Detector.Detect(Products.Engage, engageActual, state.LastStep, detectionGroups, state.SchemaStatus, cmsMajor);
        // --expect without --variant: a database detected as aligned or complete is checked against the expected release's
        // manifest in that variant, when there is one; otherwise against fresh.
        if (expected is not null && options.Variant is null && detection.Group is { } detected && !Variants.IsFresh(detected.Variant)
            && store.Find(Products.Engage, options.ExpectVersion!, detected.Variant) is { } sameVariant)
            expected = sameVariant;
        var manifest = expected ?? detection.Group?.Representative
            ?? throw new ManifestNotFoundException($"No Engage manifests ({variant}) to detect against. Give --expect <version> or --manifests <folder>.");
        var findings = SchemaComparer.Compare(manifest.Schema, engageActual).Concat(StateChecker.Check(manifest, state)).ToList();

        // Add-on releases of different majors look alike, so stay inside the major(s) of the Engage being checked.
        IReadOnlyCollection<int>? majors = options.ExpectVersion is not null
            ? MajorOf(options.ExpectVersion) is { } m ? [m] : null
            : cmsMajor is { } cms ? [cms] : detection.Majors.Count > 0 ? detection.Majors : null;

        var addOns = new List<AddOnResult>();
        foreach (var addOn in AddOnCatalog.All)
        {
            var actual = new DatabaseSchema(schema.Tables.Where(t => addOn.Tables.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList());
            string? requested = null;
            options.AddOnVersions?.TryGetValue(addOn.Product, out requested);
            var recordedStep = keys.Get(addOn.StateKey);
            var resolution = AddOnResolver.Resolve(addOn, requested, recordedStep, actual, store, majors, cmsMajor);
            if (resolution is null) continue;

            addOns.Add(new AddOnResult(addOn.Product, addOn.DisplayName, resolution.Version, resolution.Source, recordedStep, resolution.Detection?.ToResult()));
            // A missing umbracoKeyValue table is already reported once, for Engage.
            findings.AddRange(state.KeyValueTableFound ? resolution.Findings : resolution.Findings.Where(f => f.Kind != FindingKind.AddOnVersionUnknown));
            if (resolution.Manifest is not null)
                findings.AddRange(SchemaComparer.Compare(resolution.Manifest.Schema, actual).Select(f => f with { Product = addOn.Product }));
            if (resolution.CheckState && resolution.Manifest is not null && state.KeyValueTableFound)
                findings.AddRange(StateChecker.Check(resolution.Manifest, new MigrationState(true, recordedStep, null)).Select(f => f with { Product = addOn.Product }));
        }

        return Report.Create(manifest, state, addOns, findings, DateTimeOffset.Now, detection.ToResult() with
            {
                TableAnalysis = TableAnalyzer.Analyze(engageActual, detection, detectionGroups),
                Notice = UpgradeBlocks.NoticeFor(detection),
            },
            expected is not null ? Report.FromOption : Report.FromDetection);
    }

    /// <summary>
    /// The groups detection chooses from. An upgraded align-era database looks like no fresh install, before
    /// CompleteAlignSchema.sql (aligned) and after it (complete), so the fresh, aligned and complete groups are considered
    /// together; another variant is considered on its own.
    /// </summary>
    private static IReadOnlyList<StateGroup> DetectionGroups(ManifestStore store, string variant)
        => Variants.Detectable.Contains(variant, StringComparer.OrdinalIgnoreCase)
            ? [.. Variants.Detectable.SelectMany(v => store.Groups(Products.Engage, v))]
            : store.Groups(Products.Engage, variant);

    private static int? MajorOf(string version)
        => System.Version.TryParse(version, out var v) ? v.Major : int.TryParse(version.Split('.')[0], out var major) ? major : null;
}

public static class ExitCodes
{
    public const int Healthy = 0;
    public const int Warnings = 1;
    public const int Critical = 2;
    public const int Failed = 3;
}
