using System.Diagnostics;
using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Xunit.Abstractions;

namespace Umbraco.Engage.HealthCheck.Tests;

/// <summary>
/// The one test that reads the embedded manifests on purpose. A regenerated manifest that splits or merges a group fails here.
/// </summary>
public class RealManifestGroupsTests(ITestOutputHelper output)
{
    [Fact]
    public void Embedded_v17_groups_are_the_known_ones_and_build_fast()
    {
        var store = new ManifestStore(null);
        var watch = Stopwatch.StartNew();
        var engage = store.Groups(Products.Engage, "fresh");
        watch.Stop();
        output.WriteLine($"Index build for {engage.Sum(g => g.Versions.Count)} Engage manifests: {watch.ElapsedMilliseconds} ms");
        foreach (var product in new[] { Products.Engage, Products.Forms, Products.Commerce })
            foreach (var g in store.Groups(product, "fresh"))
                output.WriteLine($"{product} V{g.Major}: {g.RangeText} step {g.LastMigrationStep}");

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Index build took {watch.ElapsedMilliseconds} ms");
        // 17.0.4 has its own step. 17.2.3 shares 17.2.x's step but made contentTypeId NOT NULL, so the schema splits that step.
        Assert.Equal(
            [
                "between 17.0.0 and 17.0.3", "17.0.4", "between 17.1.0 and 17.1.1", "between 17.2.0 and 17.2.2",
                "between 17.2.3 and 17.2.4", "between 17.3.0 and 17.3.1", "17.4.0", "17.5.0",
            ],
            engage.Where(g => g.Major == 17).Select(g => g.RangeText));
        Assert.Equal(["17.0.0", "between 17.1.0 and 17.1.2"], store.Groups(Products.Forms, "fresh").Where(g => g.Major == 17).Select(g => g.RangeText));
        Assert.Equal(["17.0.0"], store.Groups(Products.Commerce, "fresh").Where(g => g.Major == 17).Select(g => g.RangeText));
    }
}

/// <summary>Every real aligned and complete manifest is detected as itself among all fresh, complete and aligned groups.</summary>
