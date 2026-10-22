using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Core.Comparison;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.State;

namespace Umbraco.Engage.HealthCheck.Tests;

public class StateCheckerTests
{
    private static readonly Manifest M = new(1, Products.Engage, "17.5.0", "fresh", "17.5.3", MigrationState.StateKey, "{last}", "Aligned", SchemaBuilder.Baseline());

    [Fact]
    public void Matching_state_has_no_findings()
        => Assert.Empty(StateChecker.Check(M, new(true, "{last}", "Aligned")));

    [Fact]
    public void Missing_key_value_table_is_state_unavailable()
        => Assert.Equal(FindingKind.StateUnavailable, Assert.Single(StateChecker.Check(M, new(false, null, null))).Kind);

    [Fact]
    public void State_unavailable_carries_the_manifest_product()
        => Assert.Equal(Products.Forms, Assert.Single(StateChecker.Check(M with { Product = Products.Forms }, new(false, null, null))).Product);

    [Fact]
    public void Different_last_step_is_critical_with_both_values()
    {
        var f = Assert.Single(StateChecker.Check(M, new(true, "{older}", "Aligned")));
        Assert.Equal(FindingKind.MigrationStateMismatch, f.Kind);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("{last}", f.Expected);
        Assert.Equal("{older}", f.Actual);
    }

    [Fact]
    public void Missing_last_step_is_a_mismatch()
        => Assert.Equal(FindingKind.MigrationStateMismatch, Assert.Single(StateChecker.Check(M, new(true, null, "Aligned"))).Kind);

    [Fact]
    public void Different_schema_status_is_a_warning()
        => Assert.Equal(Severity.Warning, Assert.Single(StateChecker.Check(M, new(true, "{last}", "Pending"))).Severity);

    [Fact]
    public void Manifest_without_status_does_not_check_it()
        => Assert.Empty(StateChecker.Check(M with { SchemaStatus = null }, new(true, "{last}", "Pending")));

    [Fact]
    public void Mismatch_names_the_manifests_state_key()
    {
        var forms = M with { Product = Products.Forms, StateKey = "Umbraco.Core.Upgrader.State+Umbraco.Engage.Forms", SchemaStatus = null };
        var f = Assert.Single(StateChecker.Check(forms, new(true, "{older}", null)));
        Assert.Equal(forms.StateKey, f.Object);
        Assert.Equal(Products.Forms, f.Product);
    }
}
