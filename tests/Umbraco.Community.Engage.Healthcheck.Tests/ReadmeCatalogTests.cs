using System.Text.RegularExpressions;
using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Generator.Catalog;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

/// <summary>The lists in docs/supported-versions.md are written by hand; these tests keep them equal to the catalog and the manifests.</summary>
public partial class ReadmeCatalogTests
{
    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "catalog", "releases.json"))) dir = Path.GetDirectoryName(dir)!;
        return dir;
    }

    private static string SupportedSection()
    {
        var path = Path.Combine(Root(), "docs", "supported-versions.md");
        Assert.True(File.Exists(path), "docs/supported-versions.md is missing");
        return File.ReadAllText(path).ReplaceLineEndings("\n");
    }

    [GeneratedRegex(@"^- \*\*(?<name>Engage Forms|Engage Commerce|Engage)\b[^*]*?(?<count>\d+) releases\*\*$")]
    private static partial Regex ProductLine();

    [GeneratedRegex(@"^  - (?<major>V\d+): (?<versions>.+)$")]
    private static partial Regex MajorLine();

    /// <summary>Product -> (major -> versions), and product -> the stated release count.</summary>
    private static (Dictionary<string, Dictionary<string, List<string>>> Lists, Dictionary<string, int> Counts) ReadmeLists()
    {
        var lists = new Dictionary<string, Dictionary<string, List<string>>>();
        var counts = new Dictionary<string, int>();
        string? product = null;
        foreach (var line in SupportedSection().Split('\n'))
        {
            if (ProductLine().Match(line) is { Success: true } p)
            {
                product = p.Groups["name"].Value switch { "Engage Forms" => Products.Forms, "Engage Commerce" => Products.Commerce, _ => Products.Engage };
                lists[product] = [];
                counts[product] = int.Parse(p.Groups["count"].Value);
            }
            else if (product is not null && MajorLine().Match(line) is { Success: true } m)
            {
                lists[product][m.Groups["major"].Value] = m.Groups["versions"].Value.Split(", ").Select(v => v.Trim()).ToList();
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                product = null;
            }
        }
        return (lists, counts);
    }

    [Fact]
    public void Readme_release_lists_equal_the_catalog()
    {
        var catalog = ReleaseCatalog.Load(Path.Combine(Root(), "catalog", "releases.json"));
        var (lists, counts) = ReadmeLists();

        foreach (var product in new[] { Products.Engage, Products.Forms, Products.Commerce })
        {
            var releases = catalog.Select(product);
            var expected = releases.GroupBy(r => r.Major).ToDictionary(g => g.Key, g => g.Select(r => r.Version).ToList());
            Assert.True(lists.ContainsKey(product), $"docs/supported-versions.md lists no {product} releases");
            Assert.Equal(expected.Keys.Order(), lists[product].Keys.Order());
            foreach (var (major, versions) in expected) Assert.Equal(versions, lists[product][major]);
            Assert.Equal(releases.Count, counts[product]);
        }
    }

    [Fact]
    public void Readme_manifest_counts_equal_the_embedded_manifests()
    {
        var store = new ManifestStore(null);
        int Count(string product, string variant) => store.All(product).Count(m => string.Equals(m.Variant, variant, StringComparison.OrdinalIgnoreCase));
        var engage = Count(Products.Engage, Variants.Fresh);
        var forms = Count(Products.Forms, Variants.Fresh);
        var commerce = Count(Products.Commerce, Variants.Fresh);
        var aligned = Count(Products.Engage, Variants.Aligned);
        var complete = Count(Products.Engage, Variants.Complete);
        var total = engage + forms + commerce + aligned + complete;

        Assert.Contains(
            $"**{total} manifests in total:** {engage + forms + commerce} fresh (Engage {engage}, Forms {forms}, Commerce {commerce}), {aligned} aligned and {complete} completed.",
            SupportedSection());
        Assert.Contains($"for the {aligned} Engage releases that have an alignment step", SupportedSection());
    }
}
