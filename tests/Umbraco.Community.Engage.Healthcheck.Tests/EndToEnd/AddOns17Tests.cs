using Microsoft.Data.SqlClient;
using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.Comparison;
using Umbraco.Community.Engage.Healthcheck.Core.Reporting;
using Umbraco.Community.Engage.Healthcheck.Generator;

namespace Umbraco.Community.Engage.Healthcheck.Tests.EndToEnd;

[Trait("Category", "E2E")]
[Collection("E2E")]
public class AddOns17Tests : IClassFixture<AddOns17Tests.ContainerFixture>
{
    private const string EngageVersion = "17.5.0";
    private const string UmbracoVersion = "17.5.3";

    private readonly ContainerFixture _fixture;

    public AddOns17Tests(ContainerFixture fixture) => _fixture = fixture;

    /// <summary>One SQL Server container for the class; each fact provisions its own database in it.</summary>
    public sealed class ContainerFixture : IAsyncLifetime
    {
        public EngageDatabaseProvisioner Provisioner { get; private set; } = null!;

        public async Task InitializeAsync() => Provisioner = await EngageDatabaseProvisioner.StartAsync();

        public async Task DisposeAsync()
        {
            if (Provisioner is not null) await Provisioner.DisposeAsync();
        }
    }

    private static string HostPath
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../hosts/Host.V17"));

    private static Task<Core.Reporting.Report> CheckAsync(string cs)
        => HealthCheckRunner.RunAsync(new HealthCheckOptions(cs, null, EngageVersion, "fresh", null));

    private static async Task ExecAsync(string cs, string sql)
    {
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Forms_is_detected_and_a_dropped_forms_index_is_caught()
    {
        var cs = await _fixture.Provisioner.ProvisionAsync(
            new HostSpec(HostPath, EngageVersion, UmbracoVersion, FormsVersion: "17.1.2", UmbracoFormsVersion: "17.6.1"), "e2e_forms");

        var clean = await CheckAsync(cs);
        Assert.DoesNotContain(clean.Findings, f => f.Severity != Severity.Info);
        Assert.Equal(ExitCodes.Healthy, clean.ExitCode);
        var forms = Assert.Single(clean.AddOns, a => a.Product == Products.Forms);
        // Forms 17.1.0-17.1.2 share step and schema, so detection names the range and checks against its newest.
        Assert.Equal("17.1.2", forms.Version);
        Assert.Equal("detected", forms.Source);
        Assert.Equal("Detected: Engage Forms between 17.1.0 and 17.1.2 (upgrade record and schema agree)",
            ConsoleReporter.Describe(forms.DisplayName, forms.Detection!, forms.LastStep));

        // Without --expect the Engage release is detected too, and Forms stays inside V17.
        var detected = await HealthCheckRunner.RunAsync(new HealthCheckOptions(cs, null, null, "fresh", null));
        Assert.Equal("17.5.0", detected.Detection!.Versions);
        Assert.Equal(EngageVersion, detected.TargetVersion);
        Assert.Equal("between 17.1.0 and 17.1.2", Assert.Single(detected.AddOns, a => a.Product == Products.Forms).Detection!.Versions);
        Assert.Equal(ExitCodes.Healthy, detected.ExitCode);

        const string index = "IX_umbracoEngageAnalyticsUmbracoFormsSubmissionRecord_visitorExternalId";
        await ExecAsync(cs, $"DROP INDEX [{index}] ON [umbracoEngageAnalyticsUmbracoFormsSubmissionRecord]");

        var after = await CheckAsync(cs);
        Assert.Contains(after.Findings, f => f.Kind == FindingKind.IndexMissing
            && f.Product == Products.Forms
            && f.Object == index
            && f.Severity == Severity.Warning);
    }

    [Fact]
    public async Task Commerce_is_detected_and_an_unknown_commerce_state_is_caught()
    {
        var cs = await _fixture.Provisioner.ProvisionAsync(
            new HostSpec(HostPath, EngageVersion, UmbracoVersion, CommerceVersion: "17.0.0", UmbracoCommerceVersion: "17.2.8"), "e2e_commerce");

        var clean = await CheckAsync(cs);
        Assert.DoesNotContain(clean.Findings, f => f.Severity != Severity.Info);
        Assert.Equal(ExitCodes.Healthy, clean.ExitCode);
        var commerce = Assert.Single(clean.AddOns, a => a.Product == Products.Commerce);
        Assert.Equal("17.0.0", commerce.Version);
        Assert.Equal("detected", commerce.Source);

        await ExecAsync(cs, "UPDATE umbracoKeyValue SET [value] = '{00000000-0000-0000-0000-000000000000}' WHERE [key] = 'Umbraco.Core.Upgrader.State+Umbraco.Engage.UmbracoCommerce'");

        var after = await CheckAsync(cs);
        Assert.Contains(after.Findings, f => f.Kind == FindingKind.AddOnVersionUnknown && f.Product == Products.Commerce);
        Assert.DoesNotContain(after.Findings, f => f.Kind == FindingKind.TableMissing);
    }
}
