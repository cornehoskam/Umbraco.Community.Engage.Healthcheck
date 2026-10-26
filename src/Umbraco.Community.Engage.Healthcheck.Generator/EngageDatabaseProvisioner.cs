using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Umbraco.Community.Engage.Healthcheck.Core.AddOns;
using Umbraco.Community.Engage.Healthcheck.Core.State;

namespace Umbraco.Community.Engage.Healthcheck.Generator;

/// <summary>Owns a throwaway SQL Server container and turns empty databases into installed Umbraco + migrated Engage databases.</summary>
public sealed class EngageDatabaseProvisioner : IAsyncDisposable
{
    private const string UmbracoStateKey = "Umbraco.Core.Upgrader.State+Umbraco.Core";
    private static readonly TimeSpan BootTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan QuietFor = TimeSpan.FromSeconds(10);

    private readonly MsSqlContainer _container;

    private EngageDatabaseProvisioner(MsSqlContainer container) => _container = container;

    public static async Task<EngageDatabaseProvisioner> StartAsync(CancellationToken ct = default)
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await container.StartAsync(ct);
        return new EngageDatabaseProvisioner(container);
    }

    public string ConnectionStringFor(string database)
        => new SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = database, TrustServerCertificate = true }.ConnectionString;

    public Task<string> ProvisionAsync(HostSpec spec, string databaseName, CancellationToken ct = default)
        => ProvisionAsync([spec], databaseName, ct);

    /// <summary>
    /// Provisions the stages one after the other onto the same database, like a customer who installs a version and
    /// upgrades later. Each stage rebuilds the host and boots it twice; stage one also installs Umbraco itself.
    /// </summary>
    public Task<string> ProvisionAsync(IReadOnlyList<HostSpec> stages, string databaseName, CancellationToken ct = default)
        => ProvisionAsync(stages, databaseName, null, ct);

    /// <param name="expectedErrors">Engage ERR/FTL texts this run expects (see <see cref="EngageBootLog.FindFailure"/>); null for none.</param>
    public async Task<string> ProvisionAsync(IReadOnlyList<HostSpec> stages, string databaseName, IReadOnlyCollection<string>? expectedErrors, CancellationToken ct = default)
    {
        if (stages.Count == 0) throw new ArgumentException("At least one stage is needed.", nameof(stages));
        await ExecuteAsync(ConnectionStringFor("master"), $"CREATE DATABASE [{databaseName}]", ct);
        var cs = ConnectionStringFor(databaseName);
        var password = "Hc!" + Guid.NewGuid().ToString("N");
        var logFile = Path.Combine(Path.GetTempPath(), $"engage-healthcheck-{databaseName}.log");
        Console.WriteLine($"Host log: {logFile}");
        if (File.Exists(logFile)) File.Delete(logFile);

        var boot = 0;
        HostSpec? previous = null;
        foreach (var spec in stages)
        {
            var hostDll = await HostRunner.BuildAsync(spec, ct);
            var plans = ExpectedPlans(spec);
            var planNames = plans.Select(p => p.Plan).ToArray();

            // Boot 1 of the first stage: Umbraco installs itself. On V17 the unattended upgrade background service then
            // also runs the Engage plan in this same boot, so every boot gets the same log-gated wait.
            var boot1 = await BootAsync(++boot, hostDll, cs, password, logFile, planNames, expectedErrors, ct);
            if (previous is null && await ReadKeyAsync(cs, UmbracoStateKey, ct) is null)
                throw new InvalidOperationException($"Umbraco did not install: '{UmbracoStateKey}' is missing after boot 1. See {logFile}.");

            // Second boot: runtime level is Run from the start, so the Engage and add-on migration components run
            // (or find nothing to do).
            var boot2 = await BootAsync(++boot, hostDll, cs, password, logFile, planNames, expectedErrors, ct);

            // Every expected plan must have left its state key behind. A plan this stage changes must also have run to "Done".
            foreach (var (plan, stateKey) in plans)
            {
                if (await ReadKeyAsync(cs, stateKey, ct) is null)
                    throw new InvalidOperationException($"'{plan}' did not migrate: '{stateKey}' is missing after boot {boot}. See {logFile}.");
                if (!Changes(previous, spec, plan)) continue;
                if (!boot1.DonePlans.Contains(plan) && !boot2.DonePlans.Contains(plan))
                    throw new InvalidOperationException($"The '{plan}' migration plan did not run to Done in either boot. See {logFile}.");
            }
            previous = spec;
        }

        return cs;
    }

    /// <summary>Whether this stage newly installs or upgrades the product behind <paramref name="plan"/>.</summary>
    private static bool Changes(HostSpec? previous, HostSpec spec, string plan)
    {
        if (previous is null) return true;
        return plan switch
        {
            "Umbraco.Engage.Forms" => previous.FormsVersion != spec.FormsVersion,
            "Umbraco.Engage.UmbracoCommerce" => previous.CommerceVersion != spec.CommerceVersion,
            _ => previous.EngageVersion != spec.EngageVersion,
        };
    }

    private const string StateKeyPrefix = "Umbraco.Core.Upgrader.State+";

    /// <summary>The migration plans a boot of <paramref name="spec"/> must finish, with their state keys.</summary>
    /// <remarks>Each plan name is its state key without the CMS upgrader prefix.</remarks>
    private static IReadOnlyList<(string Plan, string StateKey)> ExpectedPlans(HostSpec spec)
    {
        var keys = new List<string> { MigrationState.StateKey };
        if (spec.FormsVersion is not null) keys.Add(AddOnCatalog.Forms.StateKey);
        if (spec.CommerceVersion is not null) keys.Add(AddOnCatalog.Commerce.StateKey);
        return keys.Select(k => (k[StateKeyPrefix.Length..], k)).ToList();
    }

    private sealed record BootOutcome(IReadOnlySet<string> DonePlans);

    private static async Task<BootOutcome> BootAsync(int boot, string hostDll, string cs, string password, string logFile, IReadOnlyCollection<string> plans, IReadOnlyCollection<string>? expectedErrors, CancellationToken ct)
    {
        var marker = $"=== boot {boot} ===";
        File.AppendAllText(logFile, marker + Environment.NewLine);
        var host = HostRunner.Start(hostDll, cs, password, logFile);
        try { return await WaitForBootAsync(marker, host, logFile, plans, expectedErrors, ct); }
        finally { await HostRunner.StopAsync(host); }
    }

    /// <summary>
    /// Waits until this boot's log section shows "Application started.", has been quiet for <see cref="QuietFor"/>,
    /// and every expected plan that started shows the CMS "Done" line after its last start (see
    /// <see cref="EngageBootLog.PlanRunOf"/>). Throws on any Engage or expected-plan failure line.
    /// A plan that started but never logs "Done" (a failure or a hang) ends in the failure check or the timeout.
    /// </summary>
    private static async Task<BootOutcome> WaitForBootAsync(string marker, Process host, string logFile, IReadOnlyCollection<string> plans, IReadOnlyCollection<string>? expectedErrors, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + BootTimeout;
        var lastCount = -1;
        var lastChange = DateTime.UtcNow;
        List<string> lines = [];
        while (DateTime.UtcNow < deadline)
        {
            lines = ReadSection(logFile, marker);

            var failure = EngageBootLog.FindFailure(lines, plans, expectedErrors);
            if (failure is not null)
                throw new InvalidOperationException($"Migration failed ({EngageBootLog.FailureSource(lines, failure, plans)}). See {logFile}. Matched line: {failure}");

            if (host.HasExited) throw new InvalidOperationException($"Host exited early with code {host.ExitCode}. See {logFile}.");

            // Timer-driven warnings (EngageBootLog.PeriodicNoise) are not activity: they would keep the quiet window open forever.
            var activity = lines.Count(l => !EngageBootLog.IsPeriodicNoise(l));
            if (activity != lastCount) { lastCount = activity; lastChange = DateTime.UtcNow; }

            var started = lines.Any(l => l.Contains(EngageBootLog.ApplicationStarted, StringComparison.Ordinal));
            var runs = plans.ToDictionary(p => p, p => EngageBootLog.PlanRunOf(lines, p));

            if (started && DateTime.UtcNow - lastChange >= QuietFor && runs.Values.All(r => r != PlanRun.Running))
                return new BootOutcome(runs.Where(r => r.Value == PlanRun.Done).Select(r => r.Key).ToHashSet());

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        var running = EngageBootLog.RunningPlans(lines, plans);
        var stillRunning = running.Count == 0 ? "none" : string.Join(", ", running);
        throw new TimeoutException($"Boot did not finish within {BootTimeout} ({marker}). Expected plans still running: {stillRunning}. See {logFile}.");
    }

    /// <summary>The log lines written after the last occurrence of <paramref name="marker"/>.</summary>
    private static List<string> ReadSection(string logFile, string marker)
    {
        string text;
        try
        {
            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            text = reader.ReadToEnd();
        }
        catch (IOException) { return []; }

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var start = FindLastIndex(lines, l => l == marker);
        return start < 0 ? [] : lines.Skip(start + 1).ToList();
    }

    private static int FindLastIndex(List<string> lines, Func<string, bool> match)
    {
        for (var i = lines.Count - 1; i >= 0; i--) if (match(lines[i])) return i;
        return -1;
    }

    private static async Task<string?> ReadKeyAsync(string cs, string key, CancellationToken ct)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand("IF OBJECT_ID(N'dbo.umbracoKeyValue', N'U') IS NOT NULL SELECT [value] FROM umbracoKeyValue WHERE [key] = @key", conn);
        cmd.Parameters.AddWithValue("@key", key);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    private static async Task ExecuteAsync(string cs, string sql, CancellationToken ct)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
