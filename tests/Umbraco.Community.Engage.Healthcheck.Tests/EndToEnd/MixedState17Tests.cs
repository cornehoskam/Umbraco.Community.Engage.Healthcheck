using Microsoft.Data.SqlClient;
using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Reporting;
using Umbraco.Community.Engage.Healthcheck.Generator;
using Xunit.Abstractions;

namespace Umbraco.Community.Engage.Healthcheck.Tests.EndToEnd;

[Trait("Category", "E2E")]
[Collection("E2E")]
public class MixedState17Tests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Version = "17.4.0";
    private const string Table = "umbracoEngagePersonalizationAppliedPersonalization";
    private EngageDatabaseProvisioner _provisioner = null!;
    private string _cs = null!;

    public async Task InitializeAsync()
    {
        var manifest = new ManifestStore(null).Find(Products.Engage, Version, "fresh")!;
        _provisioner = await EngageDatabaseProvisioner.StartAsync();
        var host = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../hosts/Host.V17"));
        _cs = await _provisioner.ProvisionAsync(new HostSpec(host, Version, manifest.UmbracoVersion), "e2e");
    }

    public async Task DisposeAsync()
    {
        if (_provisioner is not null) await _provisioner.DisposeAsync();
    }

    private async Task ExecAsync(string sql)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task One_table_in_its_newer_shape_is_reported_as_leading()
    {
        // 17.5.0 replaced segmentId with segmentKey (and an index on it) on this table; the table is empty on a fresh install.
        await ExecAsync($"ALTER TABLE [{Table}] DROP COLUMN [segmentId]");
        await ExecAsync($"ALTER TABLE [{Table}] ADD [segmentKey] uniqueidentifier NULL");
        await ExecAsync($"CREATE NONCLUSTERED INDEX [IX_{Table}_segmentKey] ON [{Table}] ([segmentKey])");

        var report = await HealthCheckRunner.RunAsync(new HealthCheckOptions(_cs, null, null, "fresh", null));
        var writer = new StringWriter();
        ConsoleReporter.Write(report, writer, new("'E' on 'e2e'", null, null));
        output.WriteLine(writer.ToString());

        Assert.False(report.Detection!.Exact);
        Assert.Equal("17.4.0", report.Detection.Versions);
        var entry = Assert.Single(report.Detection.TableAnalysis!);
        Assert.Equal(Table, entry.Table);
        Assert.Equal(TableStatus.Leading, entry.Status);
        Assert.Equal("17.5.0", entry.MatchesVersions);
    }
}
