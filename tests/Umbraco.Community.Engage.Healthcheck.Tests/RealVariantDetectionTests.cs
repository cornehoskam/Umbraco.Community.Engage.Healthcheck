using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Reporting;
using Umbraco.Community.Engage.Healthcheck.Generator.Catalog;
using Xunit.Abstractions;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

/// <summary>Every real aligned and complete manifest is detected as itself among all fresh, complete and aligned groups.</summary>
public class RealVariantDetectionTests(ITestOutputHelper output)
{
    private static ReleaseCatalog Catalog()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "catalog", "releases.json"))) root = Path.GetDirectoryName(root)!;
        return ReleaseCatalog.Load(Path.Combine(root, "catalog", "releases.json"));
    }

    private static readonly ManifestStore Store = new(null);
    private static readonly IReadOnlyList<StateGroup> Groups = Variants.Detectable.SelectMany(v => Store.Groups(Products.Engage, v)).ToList();

    private static DetectionResult Detect(Manifest m, int? cms) => Detector.Detect(Products.Engage, m.Schema, m.LastMigrationStep, Groups, m.SchemaStatus, cms).ToResult();

    [Fact]
    public void Each_aligned_and_complete_manifest_is_its_own_release_with_CMS_evidence_and_an_identical_twin_without()
    {
        var manifests = Store.All(Products.Engage).Where(m => !Variants.IsFresh(m.Variant)).ToList();
        // One aligned and one complete manifest per catalog release with an alignFrom.
        Assert.Equal(2 * Catalog().Select(Products.Engage).Count(r => r.AlignFrom is not null), manifests.Count);
        foreach (var m in manifests)
        {
            var major = Version.Parse(m.Version).Major;
            var withCms = Detect(m, major);
            output.WriteLine($"{m.Version} {m.Variant} (CMS {major}): {ConsoleReporter.Describe("Engage", withCms, m.LastMigrationStep)}");
            Assert.True(withCms.Exact, $"{m.Version} {m.Variant}");
            Assert.Equal(m.Variant, withCms.Variant);
            Assert.Contains(m.Version, VersionsOf(withCms.Versions!));
            Assert.DoesNotContain(withCms.Alternatives, a => Version.Parse(VersionsOf(a.Version)[0]).Major != major);

            var without = Detect(m, null);
            Assert.True(VersionsOf(without.Versions!).Contains(m.Version) || without.Alternatives.Any(a => a.Identical && VersionsOf(a.Version).Contains(m.Version)), $"{m.Version} {m.Variant}");
        }
    }

    [Theory]
    [InlineData("17.4.0", "fresh")]
    [InlineData("18.1.0", "fresh")]
    [InlineData("17.4.0", "aligned")]
    [InlineData("18.1.0", "aligned")]
    [InlineData("17.4.0", "complete")]
    [InlineData("18.1.0", "complete")]
    public void The_record_of_17_4_0_and_18_1_0_never_reaches_into_the_other_major(string version, string variant)
    {
        var m = Store.Find(Products.Engage, version, variant)!;
        foreach (var cms in new int?[] { null, 17, 18 })
        {
            var text = ConsoleReporter.Describe("Engage", Detect(m, cms), m.LastMigrationStep);
            output.WriteLine($"{version} {variant} CMS {cms?.ToString() ?? "-"}: {text}");
            Assert.DoesNotContain("allows up to", text);
        }
    }

    /// <summary>The versions a range text names: "17.4.0" or "between 17.3.0 and 17.3.1".</summary>
    private static string[] VersionsOf(string range)
    {
        var parts = range.Replace("between ", "").Split(" and ");
        if (parts.Length == 1) return parts;
        var first = Version.Parse(parts[0]);
        var last = Version.Parse(parts[1]);
        return Store.All(Products.Engage).Select(m => m.Version).Distinct()
            .Where(v => Version.Parse(v) >= first && Version.Parse(v) <= last).ToArray();
    }
}
