using Umbraco.Engage.HealthCheck.Core.Comparison;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Schema;

namespace Umbraco.Engage.HealthCheck.Core.Detection;

public static class VersionIndex
{
    /// <summary>
    /// Groups manifests into state groups. Per product, variant and major, ordered by <see cref="System.Version"/>,
    /// consecutive releases with the same last migration step, the same recorded schema status and an identical schema
    /// share a group.
    /// A manifest whose version does not parse is skipped.
    /// </summary>
    public static IReadOnlyList<StateGroup> Build(IEnumerable<Manifest> manifests)
    {
        var groups = new List<StateGroup>();
        var buckets = manifests
            .Select(m => (Manifest: m, Parsed: System.Version.TryParse(m.Version, out var v) ? v : null))
            .Where(x => x.Parsed is not null)
            .GroupBy(x => (x.Manifest.Product, Variant: x.Manifest.Variant.ToLowerInvariant(), x.Parsed!.Major))
            .OrderBy(b => b.Key.Product, StringComparer.Ordinal).ThenBy(b => b.Key.Variant, StringComparer.Ordinal).ThenBy(b => b.Key.Major);

        foreach (var bucket in buckets)
        {
            var run = new List<Manifest>();
            foreach (var (manifest, _) in bucket.OrderBy(x => x.Parsed))
            {
                if (run.Count > 0 && !SameState(run[^1], manifest))
                {
                    groups.Add(Close(run, bucket.Key.Major));
                    run = [];
                }
                run.Add(manifest);
            }
            if (run.Count > 0) groups.Add(Close(run, bucket.Key.Major));
        }
        return groups;
    }

    private static StateGroup Close(List<Manifest> run, int major)
        => new(run[^1].Product, run[^1].Variant, major, run.Select(m => m.Version).ToList(), run[^1].LastMigrationStep, run[^1]);

    private static bool SameState(Manifest a, Manifest b)
        => string.Equals(a.LastMigrationStep, b.LastMigrationStep, StringComparison.OrdinalIgnoreCase)
           && string.Equals(a.SchemaStatus, b.SchemaStatus, StringComparison.OrdinalIgnoreCase)
           && Identical(a.Schema, b.Schema);

    // Identical means the comparer finds nothing in either direction; there is no separate equality code.
    private static bool Identical(DatabaseSchema a, DatabaseSchema b)
        => SchemaComparer.Compare(a, b).Count == 0 && SchemaComparer.Compare(b, a).Count == 0;
}
