using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.AddOns;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

/// <summary>
/// Every embedded manifest, read as if it were a database, is detected as its own release's group, with its own variant,
/// when the CMS evidence names the manifest's own major.
/// </summary>
public class SelfDetectionTests
{
    private static readonly ManifestStore Store = new(null);
    private static readonly IReadOnlyList<StateGroup> EngageGroups = Variants.Detectable.SelectMany(v => Store.Groups(Products.Engage, v)).ToList();

    public static TheoryData<string, string> EngageManifests()
    {
        var data = new TheoryData<string, string>();
        foreach (var m in Store.All(Products.Engage)) data.Add(m.Version, m.Variant);
        return data;
    }

    public static TheoryData<string, string> AddOnManifests()
    {
        var data = new TheoryData<string, string>();
        foreach (var product in new[] { Products.Forms, Products.Commerce })
            foreach (var m in Store.All(product)) data.Add(product, m.Version);
        return data;
    }

    [Theory]
    [MemberData(nameof(EngageManifests))]
    public void Each_engage_manifest_detects_as_its_own_group(string version, string variant)
    {
        var m = Store.Find(Products.Engage, version, variant)!;
        var major = Version.Parse(version).Major;

        var d = Detector.Detect(Products.Engage, m.Schema, m.LastMigrationStep, EngageGroups, m.SchemaStatus, major);

        Assert.True(d.Exact, $"{version} {variant}: {d.Group} ({d.Critical} critical, {d.Warning} warning)");
        Assert.Equal(variant, d.Group!.Variant, ignoreCase: true);
        Assert.Equal(major, d.Group.Major);
        Assert.Contains(version, d.Group.Versions);
        Assert.True(d.RecordMatchesSchema);
    }

    [Theory]
    [MemberData(nameof(AddOnManifests))]
    public void Each_add_on_manifest_detects_as_its_own_group(string product, string version)
    {
        var m = Store.Find(product, version, Variants.Fresh)!;
        var major = Version.Parse(version).Major;
        var addOn = AddOnCatalog.All.Single(a => a.Product == product);

        var resolution = AddOnResolver.Resolve(addOn, null, m.LastMigrationStep, m.Schema, Store, [major], major)!;

        var d = resolution.Detection!;
        Assert.True(d.Exact, $"{product} {version}: {d.Group} ({d.Critical} critical, {d.Warning} warning)");
        Assert.Equal(Variants.Fresh, d.Group!.Variant, ignoreCase: true);
        Assert.Equal(major, d.Group.Major);
        Assert.Contains(version, d.Group.Versions);
        Assert.Equal(d.Group.Representative.Version, resolution.Version);
    }
}
