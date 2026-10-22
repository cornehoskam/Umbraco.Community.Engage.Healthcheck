using System.Text.Json;
using Umbraco.Engage.HealthCheck.Core;

namespace Umbraco.Engage.HealthCheck.Generator.Catalog;

/// <summary>Where one major is hosted: the host project and the CMS / Forms / Commerce versions it runs on.</summary>
public sealed record HostEntry(string Project, string Umbraco, string? Forms = null, string? Commerce = null);

/// <summary>One supported product release. Add-on releases also name the Engage version to install next to them.</summary>
public sealed record Release(
    string Product,
    string Version,
    string Major,
    string? Umbraco = null,
    string? Engage = null,
    string? PinReason = null,
    string? InstallFrom = null,
    string? KnownFailure = null,
    string? AlignFrom = null,
    string? AlignNote = null)
{
    public Version Parsed => System.Version.Parse(Version);
    public bool IsAddOn => Product != Products.Engage;
    public override string ToString() => $"{Product} {Version}";
}

/// <summary>
/// <c>catalog/releases.json</c>: the single source of truth for every supported release. Loading validates it, so a
/// bad catalog fails before any container starts.
/// </summary>
public sealed class ReleaseCatalog
{
    public const string EngageFirst = "engage-first";
    private static readonly string[] KnownProducts = [Products.Engage, Products.Forms, Products.Commerce];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { ReadCommentHandling = JsonCommentHandling.Skip };

    private sealed record Raw(Dictionary<string, HostEntry>? Hosts, List<Release>? Releases);

    public IReadOnlyDictionary<string, HostEntry> Hosts { get; }
    public IReadOnlyList<Release> Releases { get; }

    private ReleaseCatalog(IReadOnlyDictionary<string, HostEntry> hosts, IReadOnlyList<Release> releases)
    {
        Hosts = hosts;
        Releases = releases;
    }

    public static ReleaseCatalog Load(string path) => Parse(File.ReadAllText(path));

    public static ReleaseCatalog Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<Raw>(json, Options) ?? throw new InvalidDataException("The release catalog is empty.");
        Dictionary<string, HostEntry> hosts;
        try { hosts = new Dictionary<string, HostEntry>(raw.Hosts ?? [], StringComparer.OrdinalIgnoreCase); }
        catch (ArgumentException ex) { throw new InvalidDataException($"Two hosts differ only by case: {ex.Message}", ex); }
        var releases = raw.Releases ?? [];

        foreach (var (major, host) in hosts)
        {
            if (string.IsNullOrWhiteSpace(host.Project) || string.IsNullOrWhiteSpace(host.Umbraco))
                throw new InvalidDataException($"Host {major} needs a project and an umbraco version.");
        }

        var seen = new HashSet<string>();
        foreach (var r in releases)
        {
            if (!KnownProducts.Contains(r.Product)) throw new InvalidDataException($"Unknown product '{r.Product}' in release {r.Version}.");
            if (!System.Version.TryParse(r.Version, out _)) throw new InvalidDataException($"{r}: '{r.Version}' is not a version.");
            if (!hosts.TryGetValue(r.Major ?? "", out var host)) throw new InvalidDataException($"{r}: unknown host '{r.Major}'.");
            if (!seen.Add($"{r.Product}|{r.Version}")) throw new InvalidDataException($"Duplicate release {r}.");
            if (r.Umbraco is not null && !System.Version.TryParse(r.Umbraco, out _)) throw new InvalidDataException($"{r}: umbraco override '{r.Umbraco}' is not a version.");
            if (r.InstallFrom is not null)
            {
                if (r.IsAddOn ? r.InstallFrom != EngageFirst : !System.Version.TryParse(r.InstallFrom, out var from) || from >= r.Parsed)
                    throw new InvalidDataException(r.IsAddOn
                        ? $"{r}: installFrom for an add-on must be '{EngageFirst}'."
                        : $"{r}: installFrom '{r.InstallFrom}' must be an older Engage version.");
            }
            if (r.IsAddOn)
            {
                if (string.IsNullOrWhiteSpace(r.Engage)) throw new InvalidDataException($"{r}: an add-on release needs 'engage', the Engage version to install with it.");
                if (!releases.Any(e => e.Product == Products.Engage && e.Version == r.Engage && string.Equals(e.Major, r.Major, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"{r}: engage '{r.Engage}' is not an Engage release of {r.Major} in the catalog.");
                var addOnHostVersion = r.Product == Products.Forms ? host.Forms : host.Commerce;
                if (string.IsNullOrWhiteSpace(addOnHostVersion)) throw new InvalidDataException($"{r}: host {r.Major} has no {(r.Product == Products.Forms ? "forms" : "commerce")} version.");
            }
            if (r.AlignFrom is not null)
            {
                if (r.IsAddOn) throw new InvalidDataException($"{r}: alignFrom is for Engage releases only; add-ons have only the fresh variant.");
                if (!System.Version.TryParse(r.AlignFrom, out var from) || from >= r.Parsed)
                    throw new InvalidDataException($"{r}: alignFrom '{r.AlignFrom}' must be an older Engage version.");
                if (!releases.Any(e => e.Product == Products.Engage && e.Version == r.AlignFrom))
                    throw new InvalidDataException($"{r}: alignFrom '{r.AlignFrom}' is not an Engage release in the catalog.");
            }
        }

        return new ReleaseCatalog(hosts, releases.OrderBy(r => r.Product, StringComparer.Ordinal).ThenBy(r => r.Parsed).ToList());
    }

    /// <summary>Releases in version order, per product, optionally narrowed to one product and/or major.</summary>
    public IReadOnlyList<Release> Select(string? product = null, string? major = null)
        => Releases
            .Where(r => product is null || string.Equals(r.Product, product, StringComparison.OrdinalIgnoreCase))
            .Where(r => major is null || string.Equals(r.Major, major, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public Release? Find(string product, string version)
        => Releases.FirstOrDefault(r => string.Equals(r.Product, product, StringComparison.OrdinalIgnoreCase) && r.Version == version.Trim());

    /// <summary>
    /// Two installs onto one database: the catalog release <paramref name="fromVersion"/> on its own host and CMS, then
    /// <paramref name="release"/> on its own. The hosts can be of different majors; the CMS upgrades unattended.
    /// </summary>
    public IReadOnlyList<HostSpec> StagesFrom(string fromVersion, Release release, string root)
    {
        var from = Find(release.Product, fromVersion) ?? throw new InvalidOperationException($"{release.Product} {fromVersion} is not in the catalog.");
        if (from.Parsed >= release.Parsed) throw new InvalidOperationException($"{from} is not older than {release}.");
        return [SpecFor(from, root), SpecFor(release, root)];
    }

    /// <summary>The installs for the aligned variant: the release's <c>alignFrom</c>, then the release.</summary>
    public IReadOnlyList<HostSpec> AlignedStagesFor(Release release, string root)
        => release.AlignFrom is null
            ? throw new InvalidOperationException($"{release} has no alignFrom in the catalog, so it has no aligned variant.")
            : StagesFrom(release.AlignFrom, release, root);

    /// <summary>The installs to run onto one database, in order: the optional first stage from <c>installFrom</c>, then the release.</summary>
    public IReadOnlyList<HostSpec> StagesFor(Release release, string root)
    {
        var target = SpecFor(release, root);
        if (release.InstallFrom is null) return [target];
        var first = release.IsAddOn
            ? new HostSpec(target.HostProject, target.EngageVersion, target.UmbracoVersion)
            : target with { EngageVersion = release.InstallFrom };
        return [first, target];
    }

    /// <summary>Everything the generator needs to host <paramref name="release"/>. Project paths resolve against <paramref name="root"/>.</summary>
    public HostSpec SpecFor(Release release, string root)
    {
        var host = Hosts[release.Major];
        var project = Path.GetFullPath(Path.Combine(root, host.Project));
        var umbraco = release.Umbraco ?? host.Umbraco;
        return release.Product switch
        {
            Products.Engage => new HostSpec(project, release.Version, umbraco),
            Products.Forms => new HostSpec(project, release.Engage!, umbraco, release.Version, host.Forms),
            _ => new HostSpec(project, release.Engage!, umbraco, CommerceVersion: release.Version, UmbracoCommerceVersion: host.Commerce),
        };
    }
}
