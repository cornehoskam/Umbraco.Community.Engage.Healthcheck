using System.Text.Json;
using Umbraco.Community.Engage.Healthcheck.Core.Comparison;

namespace Umbraco.Community.Engage.Healthcheck.Generator.Verify;

/// <summary>One recorded case where an assumption ("complete = fresh", "upgraded = fresh") does not hold for some releases.</summary>
/// <param name="Kinds">How many differences of each <see cref="FindingKind"/>, from fresh to this variant.</param>
public sealed record KnownVariantEntry(
    string Product,
    IReadOnlyList<string> Versions,
    string Assumption,
    bool Holds,
    string? Stored,
    string? InstalledFrom,
    int Differences,
    IReadOnlyDictionary<string, int> Kinds,
    string Reason,
    IReadOnlyList<string> Sources);

public sealed record KnownVariantsFile(IReadOnlyList<KnownVariantEntry> Entries);

/// <summary><c>catalog/known-variants.json</c>: the assumptions that fail on purpose, so verify can tell a recorded failure from a new one.</summary>
public static class KnownVariants
{
    public const string CompleteEqualsFresh = "complete = fresh";
    public const string UpgradedEqualsFresh = "upgraded = fresh";
    public const string PublishedScriptEqualsComplete = "complete with the published script = complete";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    public static KnownVariantsFile Parse(string json)
        => JsonSerializer.Deserialize<KnownVariantsFile>(json, Options) ?? throw new InvalidDataException("known-variants.json is empty.");

    /// <summary>The file next to the release catalog, or an empty list when it is not there.</summary>
    public static KnownVariantsFile LoadNextTo(string catalogFile)
    {
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(catalogFile))!, "known-variants.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : new KnownVariantsFile([]);
    }

    /// <summary>The recorded failure of <paramref name="assumption"/> for this release, if any.</summary>
    public static KnownVariantEntry? Find(KnownVariantsFile file, string product, string version, string assumption)
        => file.Entries.FirstOrDefault(e => !e.Holds && e.Product == product && e.Assumption == assumption && e.Versions.Contains(version));

    /// <summary>Whether the differences found are exactly the recorded ones: the same total and the same count per kind.</summary>
    public static bool Matches(KnownVariantEntry entry, IReadOnlyList<Finding> differences)
    {
        if (entry.Holds || differences.Count != entry.Differences) return false;
        var found = differences.GroupBy(f => f.Kind.ToString()).ToDictionary(g => g.Key, g => g.Count());
        return found.Count == entry.Kinds.Count && found.All(k => entry.Kinds.TryGetValue(k.Key, out var n) && n == k.Value);
    }
}
