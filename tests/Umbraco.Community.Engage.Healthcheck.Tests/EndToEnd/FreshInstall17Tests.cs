using Microsoft.Data.SqlClient;
using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.Comparison;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Generator;

namespace Umbraco.Community.Engage.Healthcheck.Tests.EndToEnd;

[Trait("Category", "E2E")]
[Collection("E2E")]
public class FreshInstall17Tests : IAsyncLifetime
{
    private const string Version = "17.5.0";
    private EngageDatabaseProvisioner _provisioner = null!;
    private string _cs = null!;
    private readonly Manifest _manifest = new ManifestStore(null).Find(Products.Engage, Version, "fresh")!;

    public async Task InitializeAsync()
    {
        _provisioner = await EngageDatabaseProvisioner.StartAsync();
        var host = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../hosts/Host.V17"));
        _cs = await _provisioner.ProvisionAsync(new HostSpec(host, Version, _manifest.UmbracoVersion), "e2e");
    }

    public async Task DisposeAsync()
    {
        if (_provisioner is not null) await _provisioner.DisposeAsync();
    }

    private Task<Core.Reporting.Report> CheckAsync(string? umbracoCs = null)
        => HealthCheckRunner.RunAsync(new HealthCheckOptions(_cs, umbracoCs, Version, "fresh", null));

    private async Task ExecAsync(string sql)
    {
        await using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Clean_install_is_healthy_then_every_planted_fault_is_caught()
    {
        // 1. A clean install matches its own manifest.
        var clean = await CheckAsync();
        Assert.DoesNotContain(clean.Findings, f => f.Severity != Severity.Info);
        Assert.Equal(ExitCodes.Healthy, clean.ExitCode);

        var tables = _manifest.Schema.Tables.Where(t => !EngageTables.IsReporting(t.Name)).ToList();

        // 2. A dropped index is a warning.
        var indexTable = tables.First(t => t.Indexes.Count > 0);
        var index = indexTable.Indexes[0];
        await ExecAsync($"DROP INDEX [{index.Name}] ON [{indexTable.Name}]");
        var afterIndex = await CheckAsync();
        Assert.Contains(afterIndex.Findings, f => f.Kind == FindingKind.IndexMissing && f.Object == index.Name && f.Severity == Severity.Warning);
        Assert.Equal(ExitCodes.Warnings, afterIndex.ExitCode);

        // 3. A dropped foreign key is critical.
        var fkTable = tables.First(t => t.ForeignKeys.Any(fk => fk.Name is not null));
        var fk = fkTable.ForeignKeys.First(fk => fk.Name is not null);
        await ExecAsync($"ALTER TABLE [{fkTable.Name}] DROP CONSTRAINT [{fk.Name}]");
        Assert.Contains((await CheckAsync()).Findings, f => f.Kind == FindingKind.ForeignKeyMissing && f.Table == fkTable.Name && f.Object == fk.Name && f.Severity == Severity.Critical);

        // 4. Flipping contentTypeId nullability is critical (the table is empty on a fresh install).
        const string pageVariant = "umbracoEngageAnalyticsUmbracoPageVariant";
        var column = tables.Single(t => t.Name == pageVariant).Columns.Single(c => c.Name == "contentTypeId");
        await ExecAsync($"ALTER TABLE [{pageVariant}] ALTER COLUMN [contentTypeId] {column.DataType} {(column.IsNullable ? "NOT NULL" : "NULL")}");
        Assert.Contains((await CheckAsync()).Findings, f => f.Kind == FindingKind.ColumnNullabilityDifferent && f.Table == pageVariant && f.Object == "contentTypeId" && f.Severity == Severity.Critical);

        // 5. A recorded migration step that doesn't match the manifest is critical.
        const string plantedStep = "{00000000-0000-0000-0000-000000000000}";
        await ExecAsync($"UPDATE umbracoKeyValue SET [value] = '{plantedStep}' WHERE [key] = 'Umbraco.Core.Upgrader.State+Umbraco.Engage'");
        var afterState = await CheckAsync();
        Assert.Contains(afterState.Findings, f => f.Kind == FindingKind.MigrationStateMismatch
            && f.Severity == Severity.Critical
            && f.Expected == _manifest.LastMigrationStep
            && f.Actual == plantedStep);
        Assert.Equal(ExitCodes.Critical, afterState.ExitCode);

        // 6. Pointing --umbraco-db at a database without umbracoKeyValue is reported as state unavailable.
        await ExecAsync("CREATE DATABASE [not_umbraco]");
        var notUmbracoCs = _provisioner.ConnectionStringFor("not_umbraco");
        await using (var probe = new SqlConnection(notUmbracoCs))
        {
            // A bad connection string fails here, so it can't pass for a missing table.
            await probe.OpenAsync();
        }
        var wrongDb = await CheckAsync(notUmbracoCs);
        Assert.Contains(wrongDb.Findings, f => f.Kind == FindingKind.StateUnavailable && f.Severity == Severity.Critical);
        Assert.DoesNotContain(wrongDb.Findings, f => f.Kind == FindingKind.MigrationStateMismatch);
    }
}
