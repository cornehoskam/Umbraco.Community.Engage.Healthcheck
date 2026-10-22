using Umbraco.Engage.HealthCheck.Core.Comparison;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Schema;

namespace Umbraco.Engage.HealthCheck.Core.Detection;

/// <summary>Another group that matches as well (exact) or nearly as well (closest).</summary>
/// <param name="Identical">
/// Same step, variant and schema as the detected group, in another major, with no CMS evidence to choose: the database
/// alone cannot tell the two apart.
/// </param>
public sealed record DetectionAlternative(string Version, string Variant, bool Identical = false);

/// <summary>The report shape of a detection: version text only, never a schema or a connection detail.</summary>
/// <param name="RecordedVersions">The releases the recorded step points to, per run of adjacent groups, joined with ", ".</param>
/// <param name="RecordedUpTo">The newest release of the detected group's major that the recorded step points to.</param>
/// <param name="CmsMajor">The CMS major the recorded CMS upgrade state belongs to; null when there is no such evidence.</param>
public sealed record DetectionResult(
    string Basis,
    string? Versions,
    string? FirstVersion,
    string? LastVersion,
    string? ComparedWith,
    bool Exact,
    bool RecordMatchesSchema,
    string? RecordedVersions,
    int Critical,
    int Warning,
    IReadOnlyList<DetectionAlternative> Alternatives,
    string? RecordedUpTo = null,
    IReadOnlyList<TableAnalysisEntry>? TableAnalysis = null,
    string? Variant = null,
    string? Notice = null,
    int? CmsMajor = null);

public static class DetectionBasis
{
    public const string RecordAndSchema = "record+schema";
    public const string SchemaOnly = "schema-only";
    public const string RecordOnly = "record-only";
}

public sealed record DetectionOutcome(
    string Basis,
    StateGroup? Group,
    bool Exact,
    IReadOnlyList<StateGroup> Alternatives,
    bool RecordMatchesSchema,
    IReadOnlyList<StateGroup> RecordGroups,
    int Critical,
    int Warning,
    string? RecordedVersions = null,
    int? CmsMajor = null)
{
    /// <summary>Majors of the detected group and its alternatives, the detected group's first.</summary>
    public IReadOnlyList<int> Majors => Group is null ? [] : new[] { Group }.Concat(Alternatives).Select(g => g.Major).Distinct().ToList();

    /// <summary>The newest release the recorded step points to within the detected group's major; other majors only show as alternatives.</summary>
    public string? RecordedUpTo => RecordGroups.Where(g => Group is null || g.Major == Group.Major)
        .Select(g => g.LastVersion).OrderBy(System.Version.Parse).LastOrDefault();

    /// <summary>An exact alternative in another major with the detected group's step and variant, when no CMS evidence chose between them.</summary>
    public bool IsIdentical(StateGroup alternative)
        => Exact && CmsMajor is null && Group is not null && alternative.Major != Group.Major
           && string.Equals(alternative.Variant, Group.Variant, StringComparison.OrdinalIgnoreCase)
           && string.Equals(alternative.LastMigrationStep, Group.LastMigrationStep, StringComparison.OrdinalIgnoreCase);

    public DetectionResult ToResult() => new(
        Basis,
        Group?.RangeText,
        Group?.FirstVersion,
        Group?.LastVersion,
        Group?.Representative.Version,
        Exact,
        RecordMatchesSchema,
        RecordedVersions,
        Critical,
        Warning,
        Alternatives.Select(a => new DetectionAlternative(a.RangeText, a.Variant.ToLowerInvariant(), IsIdentical(a))).ToList(),
        RecordedUpTo,
        [],
        Group?.Variant,
        null,
        CmsMajor);
}

public static class Detector
{
    // A non-exact best match also lists groups with the same critical count and at most this many more warnings.
    private const int WarningMargin = 1;

    /// <summary>
    /// The recorded step chooses the candidate groups and the schema decides between them. When no candidate matches the
    /// schema exactly, every group of the product is scored. Each representative is compared once.
    /// </summary>
    /// <param name="groups">State groups in <see cref="VersionIndex.Build"/> order; fresh, complete and aligned groups may come together.</param>
    /// <param name="recordedStatus">The recorded DatabaseSchemaStatus. It breaks ties between groups with equal differences, such as a fresh and an aligned group with the same schema.</param>
    /// <param name="cmsMajor">
    /// The CMS major from the recorded CMS upgrade state (<see cref="CmsStates.MajorOf"/>). With it, ties go to the groups of
    /// that major and groups of other majors are not listed; without it, the older major wins a tie.
    /// </param>
    public static DetectionOutcome Detect(string product, DatabaseSchema actual, string? recordedStep, IEnumerable<StateGroup> groups, string? recordedStatus = null, int? cmsMajor = null)
    {
        var all = groups.Where(g => g.Product == product).ToList();
        var recordGroups = recordedStep is null ? [] : all.Where(g => RecordedHere(g, recordedStep)).ToList();
        var recorded = RecordedText(all, recordGroups);
        var basis = recordGroups.Count > 0 ? DetectionBasis.RecordAndSchema : DetectionBasis.SchemaOnly;

        if (all.Count == 0) return new DetectionOutcome(basis, null, false, [], false, recordGroups, 0, 0, recorded, cmsMajor);
        if (actual.Tables.Count == 0)
        {
            // Nothing to compare: only the record can say which release this is.
            return recordGroups.Count > 0
                ? new DetectionOutcome(DetectionBasis.RecordOnly, recordGroups[^1], false, [], false, recordGroups, 0, 0, recorded, cmsMajor)
                : new DetectionOutcome(DetectionBasis.SchemaOnly, null, false, [], false, recordGroups, 0, 0, recorded, cmsMajor);
        }

        var scores = new Dictionary<StateGroup, (int Critical, int Warning)>(ReferenceEqualityComparer.Instance);
        (int Critical, int Warning) Score(StateGroup g)
        {
            if (scores.TryGetValue(g, out var s)) return s;
            var findings = SchemaComparer.Compare(g.Representative.Schema, actual);
            return scores[g] = (findings.Count(f => f.Severity == Severity.Critical), findings.Count(f => f.Severity == Severity.Warning));
        }
        StateGroup? Best(IEnumerable<StateGroup> candidates) => candidates
            .OrderBy(g => Score(g).Critical)
            .ThenBy(g => Score(g).Warning)
            .ThenBy(g => recordedStep is not null && RecordedHere(g, recordedStep) ? 0 : 1)
            .ThenBy(g => StatusRank(g, recordedStatus))
            // Identical states across majors (fresh 17.4.0/18.1.0, 17.5.0/18.2.0, Forms 17.1.x/18.0.0) differ only in the CMS
            // under them: the CMS evidence decides, and without it the older major is named.
            .ThenBy(g => cmsMajor is null || g.Major == cmsMajor ? 0 : 1)
            .ThenBy(g => g.Major)
            .ThenByDescending(g => System.Version.Parse(g.LastVersion))
            .ThenBy(g => VariantRank(g.Variant))
            .FirstOrDefault();
        static bool IsExact((int Critical, int Warning) s) => s is (0, 0);

        var pool = recordGroups;
        var best = Best(pool);
        if (best is null || !IsExact(Score(best)))
        {
            pool = all;
            best = Best(pool)!;
        }

        var bestScore = Score(best);
        var exact = IsExact(bestScore);
        // With CMS evidence for the detected major, a group of another major cannot be this database.
        var evidence = cmsMajor is not null && best.Major == cmsMajor;
        var alternatives = pool
            .Where(g => !ReferenceEquals(g, best))
            .Where(g => !evidence || g.Major == cmsMajor)
            .Where(g => exact ? IsExact(Score(g)) : Score(g).Critical == bestScore.Critical && Score(g).Warning - bestScore.Warning <= WarningMargin)
            .OrderByDescending(g => System.Version.Parse(g.LastVersion))
            .ToList();

        return new DetectionOutcome(basis, best, exact, alternatives, recordGroups.Any(g => ReferenceEquals(g, best)), recordGroups,
            bestScore.Critical, bestScore.Warning, recorded, cmsMajor);
    }

    /// <summary>
    /// The releases of the record groups, each named once even when a fresh and an aligned group share them. Releases that
    /// sit next to each other in the product's release list, in one major, become one range; ranges are joined with ", ".
    /// </summary>
    private static string? RecordedText(List<StateGroup> all, List<StateGroup> recordGroups)
    {
        if (recordGroups.Count == 0) return null;
        var ordered = all.SelectMany(g => g.Versions).Distinct().OrderBy(System.Version.Parse).ToList();
        var recorded = recordGroups.SelectMany(g => g.Versions).ToHashSet();
        var runs = new List<List<string>>();
        var previous = -2;
        for (var i = 0; i < ordered.Count; i++)
        {
            if (!recorded.Contains(ordered[i])) continue;
            if (i == previous + 1 && System.Version.Parse(runs[^1][^1]).Major == System.Version.Parse(ordered[i]).Major) runs[^1].Add(ordered[i]);
            else runs.Add([ordered[i]]);
            previous = i;
        }
        return string.Join(", ", runs.Select(run => run.Count == 1 ? run[0] : $"between {run[0]} and {run[^1]}"));
    }

    /// <summary>
    /// 0 when the group's manifest recorded the same status; 1 when only its variant fits the status ("Aligned" fits
    /// aligned, "Complete" fits fresh and complete, e.g. a fresh manifest of a release older than the status key); 2 otherwise.
    /// </summary>
    private static int StatusRank(StateGroup g, string? recordedStatus)
    {
        if (recordedStatus is null) return 0;
        if (string.Equals(g.Representative.SchemaStatus, recordedStatus, StringComparison.OrdinalIgnoreCase)) return 0;
        var fits = string.Equals(recordedStatus, "Aligned", StringComparison.OrdinalIgnoreCase) ? Variants.IsAligned(g.Variant)
            : string.Equals(recordedStatus, "Complete", StringComparison.OrdinalIgnoreCase) && !Variants.IsAligned(g.Variant);
        return fits ? 1 : 2;
    }

    private static int VariantRank(string variant)
    {
        var index = Variants.Detectable.ToList().FindIndex(v => string.Equals(v, variant, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? int.MaxValue : index;
    }

    private static bool RecordedHere(StateGroup g, string recordedStep)
        => string.Equals(g.LastMigrationStep, recordedStep, StringComparison.OrdinalIgnoreCase);
}
