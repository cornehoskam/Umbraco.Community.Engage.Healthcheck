using Umbraco.Community.Engage.Healthcheck.Generator;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

public class EngageBootLogTests
{
    private const string Engage = "Umbraco.Engage";
    private const string Forms = "Umbraco.Engage.Forms";
    private const string Commerce = "Umbraco.Engage.UmbracoCommerce";
    private const string Executor = "Umbraco.Cms.Infrastructure.Migrations.MigrationPlanExecutor";

    private static string Starting(string plan) => $"[10:00:00 INF] {Executor}: Starting '{plan}'...";
    private static string At(string state) => $"[10:00:00 INF] {Executor}: At {state}";
    private static readonly string Done = $"[10:00:00 INF] {Executor}: Done";

    [Fact]
    public void PlanStarting_and_PlanFailed_use_the_CMS_wording()
    {
        Assert.Equal("Starting 'Umbraco.Engage.Forms'...", EngageBootLog.PlanStarting(Forms));
        Assert.Equal("Plan Umbraco.Engage.Forms failed at step", EngageBootLog.PlanFailed(Forms));
        Assert.Equal(EngageBootLog.EngagePlanStarting, EngageBootLog.PlanStarting(Engage));
    }

    [Fact]
    public void A_plan_that_never_starts_is_not_started()
    {
        string[] lines = [Starting("Umbraco.Core"), At("origin"), Done, "[10:00:01 INF] Microsoft.Hosting.Lifetime: Application started."];
        Assert.Equal(PlanRun.NotStarted, EngageBootLog.PlanRunOf(lines, Engage));
    }

    [Fact]
    public void Two_plans_run_one_after_the_other_are_both_done()
    {
        string[] lines = [Starting(Engage), At("origin"), At("{A}"), Done, Starting(Forms), At("origin"), At("FixGoalSettingsMigration"), Done];
        Assert.Equal(PlanRun.Done, EngageBootLog.PlanRunOf(lines, Engage));
        Assert.Equal(PlanRun.Done, EngageBootLog.PlanRunOf(lines, Forms));
    }

    [Fact]
    public void Interleaved_plans_count_a_Done_only_for_the_plan_that_started_last()
    {
        // Engage starts, then Forms starts before Engage logs Done: the one Done belongs to Forms.
        string[] lines = [Starting(Engage), At("origin"), Starting(Forms), At("origin"), Done];
        Assert.Equal(PlanRun.Running, EngageBootLog.PlanRunOf(lines, Engage));
        Assert.Equal(PlanRun.Done, EngageBootLog.PlanRunOf(lines, Forms));
    }

    [Fact]
    public void Only_the_last_Starting_line_of_a_plan_counts()
    {
        // Engage ran to Done once, then started again and has not finished yet.
        string[] lines = [Starting(Engage), Done, Starting(Commerce), Done, Starting(Engage), At("origin")];
        Assert.Equal(PlanRun.Running, EngageBootLog.PlanRunOf(lines, Engage));
        Assert.Equal(PlanRun.Done, EngageBootLog.PlanRunOf(lines, Commerce));
    }

    [Fact]
    public void A_plan_without_Done_is_running()
    {
        string[] lines = [Starting(Forms), At("origin"), $"[10:00:00 INF] {Executor}: Execute CreateAnalyticsUmbracoFormsSubmissionRecordTable"];
        Assert.Equal(PlanRun.Running, EngageBootLog.PlanRunOf(lines, Forms));
    }

    [Fact]
    public void Plan_names_must_match_exactly()
    {
        // "Umbraco.Engage" must not match the Forms plan's Starting line, and the reverse.
        string[] lines = [Starting(Forms), Done];
        Assert.Equal(PlanRun.NotStarted, EngageBootLog.PlanRunOf(lines, Engage));
        Assert.Equal(PlanRun.Done, EngageBootLog.PlanRunOf(lines, Forms));
    }

    [Fact]
    public void A_failed_add_on_plan_is_a_failure_when_that_plan_is_expected()
    {
        var failed = $"[10:00:00 ERR] {Executor}: Plan Umbraco.Engage.UmbracoCommerce failed at step CreateAnalyticsUmbracoCommerceVisitorOrderTable";
        string[] lines = [Starting(Commerce), failed];
        Assert.Equal(failed, EngageBootLog.FindFailure(lines, [Engage, Commerce]));
    }

    [Fact]
    public void A_failed_plan_that_is_not_expected_is_not_a_failure()
    {
        string[] lines = [$"[10:00:00 ERR] {Executor}: Plan Umbraco.Forms failed at step X"];
        Assert.Null(EngageBootLog.FindFailure(lines, [Engage]));
    }

    [Fact]
    public void An_ERR_line_from_an_Engage_add_on_logger_is_a_failure()
    {
        var err = "[10:00:00 ERR] Umbraco.Engage.Forms.Migrations.FixGoalSettingsMigration: Boom";
        Assert.Equal(err, EngageBootLog.FindFailure([err], [Engage, Forms]));
    }

    [Fact]
    public void An_Engage_ERR_line_that_says_skipped_is_still_a_failure()
    {
        // Engage logs some ERR lines that only mean "skipped" (for example the 17.5.0 DisableInactiveVariants
        // migration). The generator fails closed on them: a manifest must come from a boot with no Engage errors.
        var skipped = "[10:00:00 ERR] Umbraco.Engage.Web.Migrations._17_5_0.DisableInactiveVariants: Could not disable A/B test variants belonging to stopped or completed tests; skipping.";
        Assert.Equal(skipped, EngageBootLog.FindFailure([Starting(Engage), skipped, Done], [Engage]));
    }

    [Fact]
    public void RunningPlans_lists_every_expected_plan_still_running_in_expected_order()
    {
        // Interleaved add-on plans: Forms and Engage start, then Commerce starts and logs the only Done.
        string[] lines = [Starting(Forms), Starting(Engage), Starting(Commerce), Done];
        Assert.Equal([Engage, Forms], EngageBootLog.RunningPlans(lines, [Engage, Forms, Commerce]));
        Assert.Empty(EngageBootLog.RunningPlans([Starting(Engage), Done], [Engage, Forms]));
    }

    [Fact]
    public void FailureSource_names_the_failed_plan_or_the_logger()
    {
        var planFailed = $"[10:00:00 ERR] {Executor}: Plan Umbraco.Engage.Forms failed at step FixGoalSettingsMigration";
        Assert.Equal("plan 'Umbraco.Engage.Forms'", EngageBootLog.FailureSource(planFailed, [Engage, Forms]));

        var commerceErr = "[10:00:00 ERR] Umbraco.Engage.Commerce.Migrations.UmbracoCommerceMigrationComponent: Boom";
        Assert.Equal("logger Umbraco.Engage.Commerce.Migrations.UmbracoCommerceMigrationComponent", EngageBootLog.FailureSource(commerceErr, [Engage, Commerce]));

        var readiness = "[10:00:00 WRN] Umbraco.Cms.Something: Engage schema readiness check failed";
        Assert.Equal("plan 'Umbraco.Engage'", EngageBootLog.FailureSource(readiness, [Engage]));
    }

    [Fact]
    public void ERR_lines_from_other_loggers_and_INF_Engage_lines_are_not_failures()
    {
        string[] lines =
        [
            "[10:00:00 ERR] Umbraco.Forms.Core.Something: Not ours",
            "[10:00:00 INF] Umbraco.Engage.Web.Migrations.Something: Skipping nothing",
            Starting(Engage), Done,
        ];
        Assert.Null(EngageBootLog.FindFailure(lines, [Engage]));
    }

    // CMS 13 logs "Plan failed at step {TargetState}" without the plan name (verified in Umbraco.Infrastructure
    // 13.16.2); CMS 16 and later log "Plan {PlanName} failed at step {TargetState}".
    private static string NamelessFailed(string step) => $"[10:00:00 ERR] {Executor}: Plan failed at step {step}";

    [Fact]
    public void A_CMS13_nameless_plan_failure_belongs_to_the_plan_that_started_last()
    {
        var failed = NamelessFailed("{A}");
        string[] lines = [Starting("Umbraco.Licenses"), Done, Starting(Engage), At("origin"), failed];
        Assert.Equal(failed, EngageBootLog.FindFailure(lines, [Engage]));
        Assert.Equal("plan 'Umbraco.Engage'", EngageBootLog.FailureSource(lines, failed, [Engage]));
    }

    [Fact]
    public void A_CMS13_nameless_failure_of_a_plan_that_is_not_expected_is_not_a_failure()
    {
        string[] lines = [Starting("UmbracoForms"), At("origin"), NamelessFailed("X")];
        Assert.Null(EngageBootLog.FindFailure(lines, [Engage]));
    }

    [Fact]
    public void A_CMS13_nameless_failure_with_no_plan_started_fails_closed()
    {
        var failed = NamelessFailed("X");
        Assert.Equal(failed, EngageBootLog.FindFailure([failed], [Engage]));
        Assert.Equal("an unnamed plan", EngageBootLog.FailureSource([failed], failed, [Engage]));
    }

    [Fact]
    public void The_named_failure_line_is_not_read_as_a_nameless_one()
    {
        // "Plan Umbraco.Forms failed at step" (an unexpected plan, CMS 16+) must not fall back to the last started plan.
        string[] lines = [Starting(Engage), $"[10:00:00 ERR] {Executor}: Plan Umbraco.Forms failed at step X"];
        Assert.Null(EngageBootLog.FindFailure(lines, [Engage]));
    }

    [Theory]
    [InlineData("SQL error executing migration CreateInitialTables at line 12")]
    [InlineData("Error executing migration InsertInitialDatasets")]
    public void V13_and_V16_Engage_SQL_migration_errors_logged_by_the_CMS_MigrationContext_are_failures(string message)
    {
        // Engage V13/V16 SqlMigrationBase logs through the migration context's logger, not an Umbraco.Engage* one.
        var err = $"[10:00:00 ERR] Umbraco.Cms.Infrastructure.Migrations.MigrationContext: {message}";
        Assert.Equal(err, EngageBootLog.FindFailure([Starting(Engage), err], [Engage]));
        Assert.Equal("plan 'Umbraco.Engage'", EngageBootLog.FailureSource([Starting(Engage), err], err, [Engage]));
    }

    [Fact]
    public void Periodic_add_on_warnings_do_not_count_as_boot_activity()
    {
        // Umbraco Commerce 16 repeats this every 10 seconds while no request has set the server role, so the
        // generator's quiet window would never close.
        Assert.True(EngageBootLog.IsPeriodicNoise("[10:18:40 WRN] Umbraco.Commerce.Cms.Scheduling.NodeLeaseHostedService: Server role is Unknown during NodeLeaseHostedService startup. Defaulting to reserved Node ID 0."));
        Assert.True(EngageBootLog.IsPeriodicNoise("[10:18:43 WRN] Umbraco.Cms.Infrastructure.BackgroundJobs.Jobs.ServerRegistration.TouchServerJob: No umbracoApplicationUrl for service (yet), skip."));
        Assert.False(EngageBootLog.IsPeriodicNoise(Starting(Engage)));
        Assert.False(EngageBootLog.IsPeriodicNoise("[10:18:40 ERR] Umbraco.Commerce.Cms.Scheduling.NodeLeaseHostedService: Server role is Unknown"));
    }
}
