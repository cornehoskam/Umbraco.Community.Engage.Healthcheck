using Umbraco.Community.Engage.Healthcheck.Core;
using Umbraco.Community.Engage.Healthcheck.Core.AddOns;
using Umbraco.Community.Engage.Healthcheck.Core.Comparison;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;
using Umbraco.Community.Engage.Healthcheck.Core.Reporting;
using Umbraco.Community.Engage.Healthcheck.Core.Schema;
using Umbraco.Community.Engage.Healthcheck.Core.State;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

[Collection("sql")]
public class HealthCheckRunnerTests(SqlServerFixture sql) : IDisposable
{
    private const string EngageStep = "{engage-step}";
    private const string FormsStep = "{forms-step}";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "runner_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static DatabaseSchema FormsSchema() => SchemaBuilder.Db(SchemaBuilder.Table(AddOnCatalog.Forms.Tables[0],
        [SchemaBuilder.Col("id", identity: true), SchemaBuilder.Col("formId")],
        pk: new("PK_forms", true, ["id"]),
        indexes: [new("IX_forms_formId", false, false, [new("formId", false)], [], null, false)]));

    private void Write(Manifest manifest)
    {
        var path = Path.Combine(_folder, Products.Folder(manifest.Product), ManifestStore.FileName(manifest.Version, manifest.Variant));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ManifestSerializer.Serialize(manifest));
    }

    private async Task<string> SetUpAsync()
    {
        Write(new Manifest(1, Products.Engage, "17.5.0", "fresh", "17.5.3", MigrationState.StateKey, EngageStep, "Aligned", SchemaBuilder.Baseline()));
        Write(new Manifest(1, Products.Forms, "17.1.2", "fresh", "17.5.3", AddOnCatalog.Forms.StateKey, FormsStep, null, FormsSchema()));

        var cs = await sql.CreateDatabaseAsync("runner_" + Guid.NewGuid().ToString("N"));
        var statements = SchemaBuilder.ToCreateSql(SchemaBuilder.Baseline()).ToList();
        statements.AddRange(SchemaBuilder.ToCreateSql(FormsSchema()));
        statements.Add("CREATE TABLE umbracoKeyValue ([key] nvarchar(256) NOT NULL PRIMARY KEY, [value] nvarchar(255) NULL, [updated] datetime NULL)");
        statements.Add($"INSERT INTO umbracoKeyValue ([key], [value]) VALUES ('{MigrationState.StateKey}', '{EngageStep}'), ('{MigrationState.SchemaStatusKey}', 'Aligned'), ('{AddOnCatalog.Forms.StateKey}', '{FormsStep}')");
        foreach (var s in statements) await SqlServerFixture.ExecuteAsync(cs, s);
        return cs;
    }

    private Task<Core.Reporting.Report> RunAsync(string cs, Dictionary<string, string>? addOnVersions = null, string? expect = "17.5.0")
        => HealthCheckRunner.RunAsync(new HealthCheckOptions(cs, null, expect, "fresh", _folder, addOnVersions, IncludeEmbeddedManifests: false));

    // 17.4.0 lacks the parent's "name" column that 17.5.0 has.
    private static DatabaseSchema Older() => SchemaBuilder.Db(SchemaBuilder.Baseline().Tables
        .Select(t => t.Name == "umbracoEngageParent" ? t with { Columns = t.Columns.Where(c => c.Name != "name").ToList() } : t).ToArray());

    [Fact]
    public async Task Without_expect_engage_is_detected_and_checked_against_the_detected_release()
    {
        var cs = await SetUpAsync();
        Write(new Manifest(1, Products.Engage, "17.4.0", "fresh", "17.5.3", MigrationState.StateKey, "{older}", "Aligned", Older()));

        var report = await RunAsync(cs, expect: null);

        Assert.Equal("17.5.0", report.TargetVersion);
        Assert.Equal("detected", report.TargetSource);
        Assert.Equal("17.5.0", report.Detection!.Versions);
        Assert.Equal("record+schema", report.Detection.Basis);
        Assert.True(report.Detection.Exact);
        Assert.True(report.Detection.RecordMatchesSchema);
        Assert.Equal(ExitCodes.Healthy, report.ExitCode);
    }

    [Fact]
    public async Task Record_behind_the_schema_is_reported_and_checked_against_the_schema_match()
    {
        // https://github.com/umbraco/Umbraco.Engage.Issues/issues/99: the schema is 17.5.0, but the upgrade record still says 17.4.0.
        var cs = await SetUpAsync();
        Write(new Manifest(1, Products.Engage, "17.4.0", "fresh", "17.5.3", MigrationState.StateKey, "{older}", "Aligned", Older()));
        await SqlServerFixture.ExecuteAsync(cs, $"UPDATE umbracoKeyValue SET [value] = '{{older}}' WHERE [key] = '{MigrationState.StateKey}'");

        var report = await RunAsync(cs, expect: null);

        Assert.Equal("17.5.0", report.TargetVersion);
        Assert.False(report.Detection!.RecordMatchesSchema);
        Assert.Equal("17.4.0", report.Detection.RecordedVersions);
        Assert.Contains(report.Findings, f => f.Kind == FindingKind.MigrationStateMismatch && f.Actual == "{older}");
    }

    [Fact]
    public async Task With_expect_detection_is_still_reported()
    {
        var cs = await SetUpAsync();
        Write(new Manifest(1, Products.Engage, "17.4.0", "fresh", "17.5.3", MigrationState.StateKey, "{older}", "Aligned", Older()));

        var report = await RunAsync(cs, expect: "17.4.0");

        Assert.Equal("17.4.0", report.TargetVersion);
        Assert.Equal("option", report.TargetSource);
        Assert.Equal("17.5.0", report.Detection!.Versions);
        Assert.Contains(report.Findings, f => f.Kind == FindingKind.ColumnExtra);
    }

    [Fact]
    public async Task Add_on_detection_is_scoped_to_the_major_of_the_detected_engage()
    {
        var cs = await SetUpAsync();
        // The same Forms step and table in V18 must not make a V17 database look like Forms 18.
        Write(new Manifest(1, Products.Forms, "18.0.0", "fresh", "18.2.1", AddOnCatalog.Forms.StateKey, FormsStep, null, FormsSchema()));

        var report = await RunAsync(cs, expect: null);

        var forms = Assert.Single(report.AddOns);
        Assert.Equal("17.1.2", forms.Version);
        Assert.Equal("17.1.2", forms.Detection!.Versions);
        Assert.Empty(forms.Detection.Alternatives);
    }

    [Fact]
    public async Task Add_on_detection_is_scoped_to_the_major_of_expect()
    {
        var cs = await SetUpAsync();
        Write(new Manifest(1, Products.Engage, "18.0.0", "fresh", "18.2.1", MigrationState.StateKey, "{v18}", "Aligned", SchemaBuilder.Baseline()));
        Write(new Manifest(1, Products.Forms, "18.0.0", "fresh", "18.2.1", AddOnCatalog.Forms.StateKey, FormsStep, null, FormsSchema()));

        var report = await RunAsync(cs, expect: "18.0.0");

        Assert.Equal("18.0.0", Assert.Single(report.AddOns).Version);
    }

    [Theory]
    [InlineData(true, "aligned")]   // the expected release has an aligned manifest: the detected variant is kept
    [InlineData(false, "fresh")]    // it has none: fresh
    public async Task Expect_without_variant_takes_the_detected_variant_when_that_release_has_it(bool expectHasAligned, string checkedVariant)
    {
        var cs = await SetUpAsync();
        // The database is an aligned 17.4.0. The fresh 17.5.0 is replaced by a newer shape, so only the aligned group matches.
        Write(new Manifest(1, Products.Engage, "17.4.0", "aligned", "17.5.3", MigrationState.StateKey, EngageStep, "Aligned", SchemaBuilder.Baseline()));
        Write(new Manifest(1, Products.Engage, "17.5.0", "fresh", "17.5.3", MigrationState.StateKey, "{newer}", "Complete", Newer()));
        if (expectHasAligned)
            Write(new Manifest(1, Products.Engage, "17.5.0", "aligned", "17.5.3", MigrationState.StateKey, "{newer}", "Aligned", Newer()));

        var report = await HealthCheckRunner.RunAsync(new HealthCheckOptions(cs, null, "17.5.0", null, _folder, null, IncludeEmbeddedManifests: false));

        Assert.Equal("aligned", report.Detection!.Variant);
        Assert.Equal("17.5.0", report.TargetVersion);
        Assert.Equal(checkedVariant, report.TargetVariant);
        var text = new StringWriter();
        ConsoleReporter.Write(report, text, new ConsoleContext("db", null, null));
        Assert.Contains($"Checked against: Engage 17.5.0 ({checkedVariant}, set by --expect)", text.ToString());
    }

    [Fact]
    public async Task Expect_with_an_explicit_variant_keeps_it()
    {
        var cs = await SetUpAsync();
        Write(new Manifest(1, Products.Engage, "17.4.0", "aligned", "17.5.3", MigrationState.StateKey, EngageStep, "Aligned", SchemaBuilder.Baseline()));
        Write(new Manifest(1, Products.Engage, "17.5.0", "fresh", "17.5.3", MigrationState.StateKey, "{newer}", "Complete", Newer()));
        Write(new Manifest(1, Products.Engage, "17.5.0", "aligned", "17.5.3", MigrationState.StateKey, "{newer}", "Aligned", Newer()));

        var report = await HealthCheckRunner.RunAsync(new HealthCheckOptions(cs, null, "17.5.0", "fresh", _folder, null, IncludeEmbeddedManifests: false));

        Assert.Equal("aligned", report.Detection!.Variant);
        Assert.Equal("fresh", report.TargetVariant);
    }

    // The baseline with one more column on the parent table.
    private static DatabaseSchema Newer() => SchemaBuilder.Db(SchemaBuilder.Baseline().Tables
        .Select(t => t.Name == "umbracoEngageParent" ? t with { Columns = [.. t.Columns, SchemaBuilder.Col("extra", nullable: true)] } : t).ToArray());

    [Fact]
    public async Task Expect_in_an_older_major_checks_against_that_major_and_its_add_ons()
    {
        var cs = await SetUpAsync();
        // V16 manifests with the same Engage and Forms shape as the V17 ones from SetUpAsync.
        Write(new Manifest(1, Products.Engage, "16.3.3", "fresh", "16.5.1", MigrationState.StateKey, EngageStep, "Aligned", SchemaBuilder.Baseline()));
        Write(new Manifest(1, Products.Forms, "16.2.2", "fresh", "16.5.1", AddOnCatalog.Forms.StateKey, FormsStep, null, FormsSchema()));

        var report = await RunAsync(cs, expect: "16.3.3");

        Assert.Equal("16.3.3", report.TargetVersion);
        Assert.Equal("fresh", report.TargetVariant);
        Assert.Equal(Report.FromOption, report.TargetSource);
        Assert.Equal("16.2.2", Assert.Single(report.AddOns).Version);
        Assert.Equal(ExitCodes.Healthy, report.ExitCode);
    }

    [Fact]
    public async Task Add_on_tables_without_engage_tables_are_no_engage_database()
    {
        Write(new Manifest(1, Products.Engage, "17.5.0", "fresh", "17.5.3", MigrationState.StateKey, EngageStep, "Aligned", SchemaBuilder.Baseline()));
        var cs = await sql.CreateDatabaseAsync("addononly_" + Guid.NewGuid().ToString("N"));
        foreach (var s in SchemaBuilder.ToCreateSql(FormsSchema())) await SqlServerFixture.ExecuteAsync(cs, s);

        await Assert.ThrowsAsync<NoEngageTablesException>(() => RunAsync(cs, expect: null));
        await Assert.ThrowsAsync<NoEngageTablesException>(() => RunAsync(cs));
    }

    [Fact]
    public async Task Without_expect_and_without_manifests_detection_is_refused()
    {
        var cs = await sql.CreateDatabaseAsync("nomani_" + Guid.NewGuid().ToString("N"));
        await SqlServerFixture.ExecuteAsync(cs, "CREATE TABLE umbracoEngageParent (id int)");

        var ex = await Assert.ThrowsAsync<ManifestNotFoundException>(() => RunAsync(cs, expect: null));
        Assert.StartsWith("No Engage manifests (fresh) to detect against", ex.Message);
    }

    [Fact]
    public async Task Database_without_engage_tables_is_not_compared()
    {
        Assert.NotNull(new ManifestStore(null).Find(Products.Engage, "17.5.0", "fresh"));
        var cs = await sql.CreateDatabaseAsync("empty_" + Guid.NewGuid().ToString("N"));

        var ex = await Assert.ThrowsAsync<NoEngageTablesException>(() => HealthCheckRunner.RunAsync(new HealthCheckOptions(cs, null, "17.5.0", "fresh", null)));

        Assert.Equal("No Engage tables found in this database. Check the --engage-db connection string and that the login has VIEW DEFINITION.", ex.Message);
    }

    [Fact]
    public async Task Detected_forms_add_on_is_checked_alongside_engage()
    {
        var cs = await SetUpAsync();

        var report = await RunAsync(cs);

        Assert.DoesNotContain(report.Findings, f => f.Kind == FindingKind.TableExtra);
        Assert.DoesNotContain(report.Findings, f => f.Severity != Severity.Info);
        var forms = Assert.Single(report.AddOns);
        Assert.Equal(Products.Forms, forms.Product);
        Assert.Equal("detected", forms.Source);
        Assert.Equal("17.1.2", forms.Version);
        Assert.Equal(FormsStep, forms.LastStep);
        Assert.Equal(ExitCodes.Healthy, report.ExitCode);
    }

    [Fact]
    public async Task Add_on_findings_carry_the_add_on_product()
    {
        var cs = await SetUpAsync();
        await SqlServerFixture.ExecuteAsync(cs, "DROP INDEX [IX_forms_formId] ON [" + AddOnCatalog.Forms.Tables[0] + "]");

        var report = await RunAsync(cs);

        var finding = Assert.Single(report.Findings, f => f.Kind == FindingKind.IndexMissing);
        Assert.Equal(Products.Forms, finding.Product);
        Assert.Equal(ExitCodes.Warnings, report.ExitCode);
    }

    [Fact]
    public async Task Unknown_add_on_step_is_a_version_unknown_warning_for_the_add_on()
    {
        var cs = await SetUpAsync();
        await SqlServerFixture.ExecuteAsync(cs, $"UPDATE umbracoKeyValue SET [value] = '{{other}}' WHERE [key] = '{AddOnCatalog.Forms.StateKey}'");

        var report = await RunAsync(cs);

        var f = Assert.Single(report.Findings, f => f.Kind == FindingKind.AddOnVersionUnknown);
        Assert.Equal(Products.Forms, f.Product);
        // The schema still names the release, so the version is known from the schema alone.
        Assert.Equal("17.1.2", report.AddOns[0].Version);
        Assert.Equal("schema-only", report.AddOns[0].Detection!.Basis);
    }

    [Fact]
    public async Task Requested_add_on_without_a_manifest_is_refused()
    {
        var cs = await SetUpAsync();

        await Assert.ThrowsAsync<ManifestNotFoundException>(() =>
            RunAsync(cs, new() { [Products.Commerce] = "99.0.0" }));
    }

    [Fact]
    public async Task Missing_key_value_table_is_reported_once_for_engage()
    {
        var cs = await SetUpAsync();
        await SqlServerFixture.ExecuteAsync(cs, "DROP TABLE umbracoKeyValue");

        var report = await RunAsync(cs);

        var f = Assert.Single(report.Findings);
        Assert.Equal(FindingKind.StateUnavailable, f.Kind);
        Assert.Equal(Products.Engage, f.Product);
        var forms = Assert.Single(report.AddOns);
        Assert.Equal(Products.Forms, forms.Product);
    }

    [Fact]
    public async Task Requested_add_on_version_is_validated_before_any_database_read()
    {
        Write(new Manifest(1, Products.Engage, "17.5.0", "fresh", "17.5.3", MigrationState.StateKey, EngageStep, "Aligned", SchemaBuilder.Baseline()));
        const string unreachable = "Server=tcp:127.0.0.1,1;Database=nope;User Id=x;Password=x;Connect Timeout=1;TrustServerCertificate=True";

        var ex = await Assert.ThrowsAsync<ManifestNotFoundException>(() =>
            HealthCheckRunner.RunAsync(new HealthCheckOptions(unreachable, null, "17.5.0", "fresh", _folder, new Dictionary<string, string> { [Products.Forms] = "9.9.9" }, IncludeEmbeddedManifests: false)));

        Assert.StartsWith("No manifest for ", ex.Message);
    }

    [Fact]
    public async Task A_check_over_server_details_never_shows_the_password_in_the_console_or_the_report()
    {
        var cs = await SetUpAsync();
        var source = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs);
        var password = source.Password;
        Assert.False(string.IsNullOrEmpty(password));
        var built = Core.Interactive.ConnectionResolver.Build(new(source.DataSource, source.InitialCatalog, source.UserID, password, TrustServerCertificate: true));
        Assert.True(built.Ok);

        var report = await RunAsync(built.Engage!);

        var console = new StringWriter();
        console.WriteLine($"Checking Engage database {ConnectionDescriber.DescribeWithServerWord(built.Engage!)}");
        ConsoleReporter.Write(report, console, new ConsoleContext(ConnectionDescriber.Describe(built.Engage!), null, "r.json"));
        var path = Path.Combine(_folder, "report.json");
        Directory.CreateDirectory(_folder);
        await JsonReportWriter.WriteAsync(report, path);

        Assert.Equal(ExitCodes.Healthy, report.ExitCode);
        Assert.DoesNotContain(password, console.ToString());
        Assert.DoesNotContain(password, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Failed_connections_never_show_the_password()
    {
        var cs = await SetUpAsync();
        var source = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs);
        const string wrong = "Wr0ng-S3cret!pw";

        // The two message paths in the CLI both print ex.Message: a rejected login, and a server that cannot be reached.
        var rejected = Core.Interactive.ConnectionResolver.Build(new(source.DataSource, source.InitialCatalog, source.UserID, wrong, TrustServerCertificate: true)).Engage!;
        var unreachable = Core.Interactive.ConnectionResolver.Build(new("127.0.0.1,1", "nope", "sa", wrong, TrustServerCertificate: true)).Engage! + ";Connect Timeout=1";

        foreach (var connection in new[] { rejected, unreachable })
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => RunAsync(connection));
            Assert.IsAssignableFrom<Microsoft.Data.SqlClient.SqlException>(ex);
            var screen = $"Could not read the database: {ex.Message}\nHealth check failed: {ex.GetType().Name}: {ex.Message}";
            Assert.DoesNotContain(wrong, screen);
            Assert.DoesNotContain(source.Password, screen);
        }
    }
}
