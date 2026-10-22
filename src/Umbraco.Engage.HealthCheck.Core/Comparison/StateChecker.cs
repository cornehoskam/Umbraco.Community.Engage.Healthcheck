using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.State;

namespace Umbraco.Engage.HealthCheck.Core.Comparison;

public static class StateChecker
{
    private const string Table = "umbracoKeyValue";

    public static IReadOnlyList<Finding> Check(Manifest expected, MigrationState actual)
    {
        if (!actual.KeyValueTableFound)
            return [Finding.Create(FindingKind.StateUnavailable, Table, null, "table present", "table not found: is the Umbraco database connection correct?", expected.Product)];

        var findings = new List<Finding>();
        if (!string.Equals(expected.LastMigrationStep, actual.LastStep, StringComparison.OrdinalIgnoreCase))
            findings.Add(Finding.Create(FindingKind.MigrationStateMismatch, Table, expected.StateKey, expected.LastMigrationStep, actual.LastStep ?? "(missing)", expected.Product));
        if (expected.SchemaStatus is not null && !string.Equals(expected.SchemaStatus, actual.SchemaStatus, StringComparison.OrdinalIgnoreCase))
            findings.Add(Finding.Create(FindingKind.SchemaStatusMismatch, Table, MigrationState.SchemaStatusKey, expected.SchemaStatus, actual.SchemaStatus ?? "(missing)", expected.Product));
        return findings;
    }
}
