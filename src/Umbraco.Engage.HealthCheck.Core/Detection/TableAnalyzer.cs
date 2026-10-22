using Umbraco.Engage.HealthCheck.Core.Comparison;
using Umbraco.Engage.HealthCheck.Core.Schema;

namespace Umbraco.Engage.HealthCheck.Core.Detection;

public static class TableStatus
{
    public const string Lagging = "lagging";
    public const string Leading = "leading";
    public const string Unknown = "unknown";
}

/// <summary>One table that does not sit on the detected release. MatchesVersions is null when no known release has this table's shape.</summary>
public sealed record TableAnalysisEntry(string Table, string Status, string? MatchesVersions);

public static class TableAnalyzer
{
    /// <summary>
    /// For a database that is not an exact match: every actual table that is not in the shape of the best group, with the
    /// groups of the same product, variant and major whose version of that table it does match. Tables that follow the best
    /// group are not listed. Other variants and majors are left out: lagging and leading are positions in one release order.
    /// Reporting tables are skipped, as the comparer only checks that they exist.
    /// </summary>
    /// <param name="groups">State groups in <see cref="VersionIndex.Build"/> order.</param>
    public static IReadOnlyList<TableAnalysisEntry> Analyze(DatabaseSchema actual, DetectionOutcome outcome, IEnumerable<StateGroup> groups)
    {
        if (outcome.Exact || outcome.Group is not { } best) return [];

        var inMajor = groups
            .Where(g => g.Product == best.Product && string.Equals(g.Variant, best.Variant, StringComparison.OrdinalIgnoreCase) && g.Major == best.Major)
            .ToList();
        var bestIndex = inMajor.FindIndex(g => g.Product == best.Product && string.Equals(g.Variant, best.Variant, StringComparison.OrdinalIgnoreCase) && g.Major == best.Major && g.FirstVersion == best.FirstVersion);
        if (bestIndex < 0) return [];

        var result = new List<TableAnalysisEntry>();
        foreach (var table in actual.Tables.Where(t => !EngageTables.IsReporting(t.Name)).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            // The same definition of "same" as the whole-schema comparison.
            var matching = Enumerable.Range(0, inMajor.Count)
                .Where(i => inMajor[i].Representative.Schema.Tables
                    .FirstOrDefault(t => string.Equals(t.Name, table.Name, StringComparison.OrdinalIgnoreCase)) is { } expected
                    && SchemaComparer.TableMatches(expected, table))
                .ToList();

            if (matching.Contains(bestIndex)) continue;
            if (matching.Count == 0) { result.Add(new TableAnalysisEntry(table.Name, TableStatus.Unknown, null)); continue; }

            // Normally all matches sit on one side. If not, the nearest one decides; a tie counts as lagging.
            var nearest = matching.OrderBy(i => Math.Abs(i - bestIndex)).ThenBy(i => i).First();
            result.Add(new TableAnalysisEntry(table.Name, nearest < bestIndex ? TableStatus.Lagging : TableStatus.Leading, RangeText(inMajor, matching)));
        }
        return result;
    }

    /// <summary>Runs of adjacent groups become one range each; runs are joined with ", ".</summary>
    private static string RangeText(List<StateGroup> groups, List<int> indexes)
    {
        var runs = new List<List<int>>();
        foreach (var i in indexes.Order())
        {
            if (runs.Count > 0 && runs[^1][^1] == i - 1) runs[^1].Add(i);
            else runs.Add([i]);
        }
        return string.Join(", ", runs.Select(run =>
        {
            var versions = run.SelectMany(i => groups[i].Versions).ToList();
            return versions.Count == 1 ? versions[0] : $"between {versions[0]} and {versions[^1]}";
        }));
    }
}
