namespace Umbraco.Community.Engage.Healthcheck.Core.Comparison;

/// <summary>The single place that decides how serious each kind of finding is.</summary>
public static class SeverityRules
{
    private static readonly Dictionary<FindingKind, Severity> Rules = new()
    {
        [FindingKind.TableMissing] = Severity.Critical,
        [FindingKind.TableExtra] = Severity.Warning,
        [FindingKind.ReportingTableMissing] = Severity.Info,
        [FindingKind.ColumnMissing] = Severity.Critical,
        [FindingKind.ColumnExtra] = Severity.Warning,
        [FindingKind.ColumnTypeDifferent] = Severity.Critical,
        [FindingKind.ColumnNullabilityDifferent] = Severity.Critical,
        [FindingKind.ColumnIdentityDifferent] = Severity.Critical,
        [FindingKind.ColumnDefaultDifferent] = Severity.Warning,
        [FindingKind.ColumnCollationDifferent] = Severity.Info,
        [FindingKind.PrimaryKeyMissing] = Severity.Critical,
        [FindingKind.PrimaryKeyDifferent] = Severity.Warning,
        [FindingKind.UniqueConstraintMissing] = Severity.Critical,
        [FindingKind.ForeignKeyMissing] = Severity.Critical,
        [FindingKind.ForeignKeyDifferent] = Severity.Warning,
        [FindingKind.IndexMissing] = Severity.Warning,
        [FindingKind.IndexDifferent] = Severity.Warning,
        [FindingKind.CheckConstraintMissing] = Severity.Warning,
        [FindingKind.ConstraintNameDifferent] = Severity.Warning,
        [FindingKind.ExtraObject] = Severity.Warning,
        [FindingKind.StateUnavailable] = Severity.Critical,
        [FindingKind.MigrationStateMismatch] = Severity.Critical,
        [FindingKind.SchemaStatusMismatch] = Severity.Warning,
        [FindingKind.AddOnVersionUnknown] = Severity.Warning,
    };

    public static Severity For(FindingKind kind) => Rules[kind];
}
