using System.Reflection;
using System.Text.Json;

namespace Umbraco.Community.Engage.Healthcheck.Core.Detection;

/// <summary>The CMS upgrade states one Umbraco.Infrastructure.dll knows, for one CMS major.</summary>
public sealed record CmsMajorStates(string Cms, IReadOnlyList<string> States);

/// <summary>The contents of <c>cms-states.json</c>: per CMS major, every braced GUID string literal in that major's Umbraco.Infrastructure.dll.</summary>
public sealed record CmsStateData(string Source, IReadOnlyDictionary<string, CmsMajorStates> Majors);

/// <summary>
/// Which CMS major a recorded <c>Umbraco.Core.Upgrader.State+Umbraco.Core</c> value belongs to. The CMS plan is cumulative,
/// so the answer is the lowest major whose states contain the value. An unknown value is no evidence.
/// </summary>
public static class CmsStates
{
    public const string StateKey = "Umbraco.Core.Upgrader.State+Umbraco.Core";
    public const string ResourceName = "cms-states.json";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<CmsStateData> EmbeddedData = new(() =>
    {
        using var stream = typeof(CmsStates).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded {ResourceName} is missing.");
        return Parse(new StreamReader(stream).ReadToEnd());
    });

    public static CmsStateData Embedded => EmbeddedData.Value;

    public static CmsStateData Parse(string json)
        => JsonSerializer.Deserialize<CmsStateData>(json, Options) ?? throw new InvalidDataException($"{ResourceName} is empty.");

    public static string Serialize(CmsStateData data)
        => JsonSerializer.Serialize(data, new JsonSerializerOptions(Options) { WriteIndented = true });

    /// <summary>The lowest CMS major whose states contain <paramref name="state"/>; null for a missing or unknown state.</summary>
    public static int? MajorOf(string? state, CmsStateData? data = null)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;
        var majors = (data ?? Embedded).Majors
            .Where(m => m.Value.States.Contains(state.Trim(), StringComparer.OrdinalIgnoreCase))
            .Select(m => int.TryParse(m.Key, out var major) ? major : (int?)null)
            .Where(m => m is not null)
            .ToList();
        return majors.Count == 0 ? null : majors.Min();
    }
}
