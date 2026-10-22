using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Umbraco.Engage.HealthCheck.Core.Detection;

namespace Umbraco.Engage.HealthCheck.Generator.Catalog;

/// <summary>
/// Builds <c>cms-states.json</c>: reads the user-strings heap of each host CMS version's Umbraco.Infrastructure.dll from the
/// local NuGet cache (read-only) and keeps every string that is exactly a braced GUID. The CMS upgrade plan's states are
/// such literals; other braced GUIDs only add harmless entries, because a state is looked up, never enumerated.
/// </summary>
public static partial class CmsStateExtractor
{
    [GeneratedRegex(@"^\{[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\}$")]
    private static partial Regex BracedGuid();

    /// <summary>Every user string in the assembly that is exactly a braced GUID, lower case, sorted.</summary>
    public static IReadOnlyList<string> BracedGuids(string dllPath)
    {
        using var stream = File.OpenRead(dllPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var handle = MetadataTokens.UserStringHandle(1);
        while (!handle.IsNil)
        {
            var text = reader.GetUserString(handle);
            if (BracedGuid().IsMatch(text)) found.Add(text.ToLowerInvariant());
            handle = reader.GetNextHandle(handle);
        }
        return found.ToList();
    }

    /// <summary>The Umbraco.Infrastructure.dll of one CMS version in the NuGet cache, or null when it is not there.</summary>
    public static string? DllFor(string nugetRoot, string cmsVersion)
    {
        var lib = Path.Combine(nugetRoot, "umbraco.cms.infrastructure", cmsVersion.ToLowerInvariant(), "lib");
        if (!Directory.Exists(lib)) return null;
        return Directory.GetFiles(lib, "Umbraco.Infrastructure.dll", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal).LastOrDefault();
    }

    private const byte LdStr = 0x72;
    private const byte UserStringTable = 0x70;

    /// <summary>
    /// The final state of the CMS upgrade plan in one Umbraco.Infrastructure.dll: the last braced GUID string literal loaded
    /// in <c>UmbracoPlan.DefinePlan</c> (each <c>To&lt;T&gt;("{guid}")</c> loads its state with an ldstr, in plan order).
    /// Null when the type or method is not found.
    /// </summary>
    public static string? LastPlanState(string dllPath)
    {
        using var stream = File.OpenRead(dllPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var rva = reader.TypeDefinitions.Select(reader.GetTypeDefinition)
            .Where(t => reader.GetString(t.Name) == "UmbracoPlan")
            .SelectMany(t => t.GetMethods().Select(reader.GetMethodDefinition))
            .Where(m => reader.GetString(m.Name) == "DefinePlan")
            .Select(m => m.RelativeVirtualAddress)
            .FirstOrDefault(a => a != 0);
        if (rva == 0) return null;

        var il = pe.GetMethodBody(rva).GetILBytes();
        if (il is null) return null;
        string? last = null;
        // An ldstr is 0x72 plus a user-string token (table 0x70 in the high byte). A stray match inside another operand
        // would have to point at a braced GUID string as well, so it is ignored in practice.
        for (var i = 0; i + 4 < il.Length; i++)
        {
            if (il[i] != LdStr || il[i + 4] != UserStringTable) continue;
            var offset = il[i + 1] | il[i + 2] << 8 | il[i + 3] << 16;
            string text;
            try { text = reader.GetUserString(MetadataTokens.UserStringHandle(offset)); }
            catch (BadImageFormatException) { continue; }
            if (BracedGuid().IsMatch(text)) last = text.ToLowerInvariant();
        }
        return last;
    }

    /// <summary>
    /// Checks that each host's CMS version's final plan state maps to the host's own major in <paramref name="data"/>.
    /// Returns one message per host that does not; empty when all do.
    /// </summary>
    public static IReadOnlyList<string> CheckHostsMapToOwnMajor(IReadOnlyDictionary<string, HostEntry> hosts, string nugetRoot, CmsStateData data)
    {
        var problems = new List<string>();
        foreach (var (major, host) in hosts)
        {
            var expected = Version.Parse(host.Umbraco).Major;
            var last = DllFor(nugetRoot, host.Umbraco) is { } dll ? LastPlanState(dll) : null;
            if (last is null)
            {
                problems.Add($"{major}: no final upgrade plan state found in CMS {host.Umbraco}");
                continue;
            }
            var mapped = CmsStates.MajorOf(last, data);
            if (mapped != expected)
                problems.Add($"{major}: CMS {host.Umbraco}'s final state {last} maps to {(mapped is null ? "no major" : $"CMS {mapped}")}, not CMS {expected}");
        }
        return problems;
    }

    /// <summary>
    /// One entry per catalog host major (V13 -> "13"): the union of the states of the host's CMS version and every other
    /// stable version of that major in the cache. A newer patch of a lower major can add states that the next major also
    /// carries (CMS 17.6.0's AddContentTypeIdIndexForContent is in 18.2.1, not in 17.5.3), so the host version alone would
    /// map a 17.6 site to 18. Each entry lists the versions read.
    /// </summary>
    public static CmsStateData Build(IReadOnlyDictionary<string, HostEntry> hosts, string nugetRoot)
    {
        var majors = new SortedDictionary<string, CmsMajorStates>(StringComparer.Ordinal);
        foreach (var (major, host) in hosts)
        {
            if (DllFor(nugetRoot, host.Umbraco) is null)
                throw new FileNotFoundException($"Umbraco.Infrastructure.dll for CMS {host.Umbraco} is not in {nugetRoot}. Build Host.{major} once to restore it.");
            var number = Version.Parse(host.Umbraco).Major;
            var versions = Directory.GetDirectories(Path.Combine(nugetRoot, "umbraco.cms.infrastructure"))
                .Select(Path.GetFileName)
                .Where(v => Version.TryParse(v, out var parsed) && parsed.Major == number) // stable versions only: a pre-release has a suffix
                .OrderBy(v => Version.Parse(v!))
                .ToList();
            var states = versions.SelectMany(v => DllFor(nugetRoot, v!) is { } dll ? BracedGuids(dll) : []).Distinct().Order(StringComparer.Ordinal).ToList();
            majors[major.TrimStart('V', 'v')] = new CmsMajorStates(string.Join(", ", versions), states);
        }
        return new CmsStateData("Braced GUID user strings of Umbraco.Infrastructure.dll (NuGet package umbraco.cms.infrastructure), union over the listed stable versions per major from the local NuGet cache; built by 'generate cms-states'.", majors);
    }
}
