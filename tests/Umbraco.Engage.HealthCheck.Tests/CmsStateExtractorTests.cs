using Umbraco.Engage.HealthCheck.Core.Detection;
using Umbraco.Engage.HealthCheck.Generator.Catalog;

namespace Umbraco.Engage.HealthCheck.Tests;

/// <summary>
/// A stand-in for the CMS plan: <see cref="CmsStateExtractor.LastPlanState"/> finds a type named UmbracoPlan by name, so
/// this test assembly can play Umbraco.Infrastructure.dll.
/// </summary>
internal sealed class UmbracoPlan
{
    private readonly List<string> _states = [];

    private UmbracoPlan To(string state)
    {
        _states.Add(state);
        return this;
    }

    // Not a state: only braced GUIDs count.
    private const string Name = "Umbraco.Core";

    internal IReadOnlyList<string> DefinePlan()
    {
        To("{11111111-0000-0000-0000-000000000013}");
        _ = Name + "{not-a-guid}";
        To("{22222222-0000-0000-0000-000000000017}");
        return _states;
    }
}

public sealed class CmsStateExtractorTests : IDisposable
{
    private const string First = "{11111111-0000-0000-0000-000000000013}";
    private const string Last = "{22222222-0000-0000-0000-000000000017}";
    private readonly string _nuget = Path.Combine(Path.GetTempPath(), "nuget_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_nuget)) Directory.Delete(_nuget, true);
    }

    private static string ThisAssembly => typeof(UmbracoPlan).Assembly.Location;

    [Fact]
    public void The_last_plan_state_is_the_last_braced_GUID_loaded_in_DefinePlan()
    {
        Assert.Equal([First, Last], new UmbracoPlan().DefinePlan());
        Assert.Equal(Last, CmsStateExtractor.LastPlanState(ThisAssembly));
    }

    [Fact]
    public void An_assembly_without_UmbracoPlan_has_no_last_state()
        => Assert.Null(CmsStateExtractor.LastPlanState(typeof(CmsStates).Assembly.Location));

    private IReadOnlyDictionary<string, HostEntry> HostOn(string cms)
    {
        var lib = Path.Combine(_nuget, "umbraco.cms.infrastructure", cms, "lib", "net10.0");
        Directory.CreateDirectory(lib);
        File.Copy(ThisAssembly, Path.Combine(lib, "Umbraco.Infrastructure.dll"));
        return new Dictionary<string, HostEntry> { ["V17"] = new("hosts/Host.V17", cms) };
    }

    private static CmsStateData Data(string lastStateMajor) => new("test", new Dictionary<string, CmsMajorStates>
    {
        ["16"] = new("16.5.1", [First]),
        [lastStateMajor] = new("x", [Last]),
    });

    [Fact]
    public void A_host_whose_final_state_maps_to_its_own_major_passes()
        => Assert.Empty(CmsStateExtractor.CheckHostsMapToOwnMajor(HostOn("17.5.3"), _nuget, Data("17")));

    [Fact]
    public void A_host_whose_final_state_maps_to_another_major_fails()
    {
        var problem = Assert.Single(CmsStateExtractor.CheckHostsMapToOwnMajor(HostOn("17.5.3"), _nuget, Data("18")));
        Assert.Equal($"V17: CMS 17.5.3's final state {Last} maps to CMS 18, not CMS 17", problem);
    }

    [Fact]
    public void A_host_without_its_CMS_package_fails()
        => Assert.Equal("V17: no final upgrade plan state found in CMS 17.5.3",
            Assert.Single(CmsStateExtractor.CheckHostsMapToOwnMajor(new Dictionary<string, HostEntry> { ["V17"] = new("hosts/Host.V17", "17.5.3") }, _nuget, Data("17"))));
}
