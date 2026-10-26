using System.IO.Compression;
using System.Reflection;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;

namespace Umbraco.Community.Engage.Healthcheck.Core.Manifests;

/// <summary>
/// Finds manifests per product. Embedded ones are gzipped resources named <c>manifests/&lt;folder&gt;/&lt;file&gt;.json.gz</c>;
/// an external folder holds plain <c>&lt;folder&gt;/&lt;file&gt;.json</c> files and wins on the same name.
/// </summary>
public sealed class ManifestStore(string? externalFolder, bool includeEmbedded = true)
{
    private const string ResourcePrefix = "manifests/";
    private const string JsonExtension = ".json";
    private const string GzipExtension = ".json.gz";
    private static readonly Assembly Assembly = typeof(ManifestStore).Assembly;

    private readonly Lock _lock = new();
    private readonly Dictionary<(string Folder, string Name), Manifest?> _loaded = [];
    private readonly Dictionary<(string Product, string Variant), IReadOnlyList<StateGroup>> _groups = [];

    public static string FileName(string version, string variant) => $"{version}.{variant}.json";

    public static string RelativePath(string product, string version, string variant) => $"{Products.Folder(product)}/{FileName(version, variant)}";

    public Manifest? Find(string product, string version, string variant) => Load(Products.Folder(product), $"{version}.{variant}");

    public IReadOnlyList<Manifest> All(string product)
    {
        var folder = Products.Folder(product);
        return Available(product).Select(name => Load(folder, name)!).ToList();
    }

    public IReadOnlyList<string> Available(string product)
    {
        var folder = Products.Folder(product);
        var names = includeEmbedded ? EmbeddedNames(folder) : [];
        var dir = ExternalDirectory(folder);
        if (dir is not null && Directory.Exists(dir))
            names = names.Concat(Directory.GetFiles(dir, "*" + JsonExtension).Select(f => Path.GetFileName(f)[..^JsonExtension.Length]));
        return names.Distinct().Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>The state groups of one product and variant, built on first use and then reused.</summary>
    public IReadOnlyList<StateGroup> Groups(string product, string variant)
    {
        var key = (product, variant.ToLowerInvariant());
        lock (_lock)
        {
            if (_groups.TryGetValue(key, out var cached)) return cached;
        }
        var built = VersionIndex.Build(All(product).Where(m => string.Equals(m.Variant, variant, StringComparison.OrdinalIgnoreCase)));
        lock (_lock)
        {
            return _groups.TryAdd(key, built) ? built : _groups[key];
        }
    }

    // Each manifest is deserialized once per store.
    private Manifest? Load(string folder, string name)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue((folder, name), out var cached)) return cached;
        }
        var manifest = Read(folder, name);
        lock (_lock)
        {
            return _loaded.TryAdd((folder, name), manifest) ? manifest : _loaded[(folder, name)];
        }
    }

    private Manifest? Read(string folder, string name)
    {
        var dir = ExternalDirectory(folder);
        if (dir is not null)
        {
            var path = Path.Combine(dir, name + JsonExtension);
            if (File.Exists(path)) return ManifestSerializer.Deserialize(File.ReadAllText(path));
        }
        if (!includeEmbedded) return null;
        using var stream = Assembly.GetManifestResourceStream($"{ResourcePrefix}{folder}/{name}{GzipExtension}");
        if (stream is null) return null;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return ManifestSerializer.Deserialize(reader.ReadToEnd());
    }

    private string? ExternalDirectory(string folder) => externalFolder is null ? null : Path.Combine(externalFolder, folder);

    private static IEnumerable<string> EmbeddedNames(string folder)
    {
        var prefix = $"{ResourcePrefix}{folder}/";
        return Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(GzipExtension, StringComparison.Ordinal))
            .Select(n => n[prefix.Length..^GzipExtension.Length])
            .Where(n => !n.Contains('/'));
    }
}
