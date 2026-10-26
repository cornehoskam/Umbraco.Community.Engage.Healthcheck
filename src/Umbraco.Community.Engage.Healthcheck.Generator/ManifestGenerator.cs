using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.AddOns;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Schema;
using Umbraco.Community.Engage.Healthcheck.Core.State;

namespace Umbraco.Community.Engage.Healthcheck.Generator;

/// <summary>Installs one release on a fresh container and turns the result into manifests. Shared by <c>generate</c> and <c>generate-all</c>.</summary>
public static class ManifestGenerator
{
    /// <summary>Starts a fresh SQL Server container, installs <paramref name="spec"/> and reads the manifest(s) back.</summary>
    public static Task<IReadOnlyList<Manifest>> GenerateAsync(HostSpec spec, CancellationToken ct) => GenerateAsync([spec], null, ct);

    /// <summary>Like the single-spec overload, but installs the stages in order onto one database. The last stage is the release itself.</summary>
    public static Task<IReadOnlyList<Manifest>> GenerateAsync(IReadOnlyList<HostSpec> stages, string? installedFrom, CancellationToken ct)
        => GenerateAsync(stages, installedFrom, Variants.Fresh, null, ct);

    /// <summary>The status every aligned manifest must record: AlignSchema ran, CompleteAlignSchema.sql did not.</summary>
    public const string AlignedStatus = "Aligned";

    /// <summary>The status every complete manifest must record: CompleteAlignSchema.sql finished.</summary>
    public const string CompleteStatus = "Complete";

    /// <summary>
    /// Installs the stages onto one database and snapshots it as <paramref name="variant"/>.
    /// Aligned and complete runs accept the <see cref="EngageBootLog.SchemaAlignmentGuidance"/> lines and must end the
    /// stages with status "Aligned". A complete run then calls <paramref name="completeAlignment"/> (the release's
    /// alignment scripts) and must end with status "Complete" before the snapshot.
    /// <paramref name="afterSnapshot"/> runs on the same database after the snapshot, before the container goes away.
    /// </summary>
    public static async Task<IReadOnlyList<Manifest>> GenerateAsync(
        IReadOnlyList<HostSpec> stages, string? installedFrom, string variant, Func<string, CancellationToken, Task>? afterSnapshot, CancellationToken ct,
        string? databaseName = null, Func<string, CancellationToken, Task>? completeAlignment = null)
    {
        var complete = Variants.IsComplete(variant);
        var aligned = Variants.IsAligned(variant) || complete;
        var spec = stages[^1];
        if (aligned && (spec.FormsVersion is not null || spec.CommerceVersion is not null))
            throw new ArgumentException("Add-ons have only the fresh variant.", nameof(stages));
        if (complete && completeAlignment is null)
            throw new ArgumentException("The complete variant needs the alignment scripts to run.", nameof(completeAlignment));
        var name = complete ? Variants.Complete : aligned ? Variants.Aligned : Variants.Fresh;

        await using var provisioner = await EngageDatabaseProvisioner.StartAsync(ct);
        // The database name is in the host log name: an aligned, complete or verify run never shares a log with a fresh one.
        var cs = await provisioner.ProvisionAsync(stages, databaseName ?? name, aligned ? EngageBootLog.SchemaAlignmentGuidance : null, ct);

        var manifests = new List<Manifest>();
        if (spec.FormsVersion is null && spec.CommerceVersion is null)
        {
            var state = await MigrationStateReader.ReadAsync(cs, ct);
            if (aligned) RequireStatus(state, AlignedStatus, $"The {name} run of Engage {spec.EngageVersion} after its installs");
            if (complete)
            {
                await completeAlignment!(cs, ct);
                state = await MigrationStateReader.ReadAsync(cs, ct);
                RequireStatus(state, CompleteStatus, $"The complete run of Engage {spec.EngageVersion} after the alignment scripts");
            }
            var schema = await SqlServerSchemaReader.ReadAsync(cs, ct);
            manifests.Add(new Manifest(Manifest.CurrentFormatVersion, Products.Engage, spec.EngageVersion, name, spec.UmbracoVersion, MigrationState.StateKey, state.LastStep, state.SchemaStatus, EngageOnly(schema), null, installedFrom));
        }
        else
        {
            var schema = await SqlServerSchemaReader.ReadAsync(cs, ct);
            // Add-on runs write only the add-on manifests; the Engage manifest comes from a run without add-ons.
            var addOns = new List<(AddOn AddOn, string Version)>();
            if (spec.FormsVersion is not null) addOns.Add((AddOnCatalog.Forms, spec.FormsVersion));
            if (spec.CommerceVersion is not null) addOns.Add((AddOnCatalog.Commerce, spec.CommerceVersion));
            var kv = await MigrationStateReader.ReadKeysAsync(cs, addOns.Select(a => a.AddOn.StateKey).ToList(), ct);
            foreach (var (addOn, version) in addOns)
            {
                var addOnSchema = new DatabaseSchema(schema.Tables.Where(t => addOn.Tables.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList());
                manifests.Add(new Manifest(Manifest.CurrentFormatVersion, addOn.Product, version, "fresh", spec.UmbracoVersion, addOn.StateKey, kv.Get(addOn.StateKey), null, addOnSchema, spec.EngageVersion, installedFrom));
            }
        }
        if (afterSnapshot is not null) await afterSnapshot(cs, ct);
        return manifests;
    }

    private static void RequireStatus(MigrationState state, string expected, string what)
    {
        if (!string.Equals(state.SchemaStatus, expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"{what} recorded schema status '{state.SchemaStatus ?? "(none)"}', not '{expected}'.");
    }

    /// <summary>The tables that belong to Engage itself: add-on tables belong to the add-ons.</summary>
    public static DatabaseSchema EngageOnly(DatabaseSchema schema) => new(schema.Tables.Where(t => !AddOnCatalog.OwnsTable(t.Name)).ToList());

    public static string PathFor(Manifest manifest, string outFolder)
        => Path.GetFullPath(Path.Combine(outFolder, ManifestStore.RelativePath(manifest.Product, manifest.Version, manifest.Variant)));

    public static async Task<string> WriteAsync(Manifest manifest, string outFolder, CancellationToken ct)
    {
        var path = PathFor(manifest, outFolder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, ManifestSerializer.Serialize(manifest), ct);
        return path;
    }
}
