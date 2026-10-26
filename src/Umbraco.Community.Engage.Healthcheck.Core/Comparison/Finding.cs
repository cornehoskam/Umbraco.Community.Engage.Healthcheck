namespace Umbraco.Community.Engage.Healthcheck.Core.Comparison;

public enum Severity { Info, Warning, Critical }

public enum FindingKind
{
    TableMissing, TableExtra, ReportingTableMissing,
    ColumnMissing, ColumnExtra, ColumnTypeDifferent, ColumnNullabilityDifferent, ColumnIdentityDifferent,
    ColumnDefaultDifferent, ColumnCollationDifferent,
    PrimaryKeyMissing, PrimaryKeyDifferent,
    UniqueConstraintMissing,
    ForeignKeyMissing, ForeignKeyDifferent,
    IndexMissing, IndexDifferent,
    CheckConstraintMissing,
    ConstraintNameDifferent, ExtraObject,
    StateUnavailable, MigrationStateMismatch, SchemaStatusMismatch, AddOnVersionUnknown,
}

public sealed record Finding(FindingKind Kind, Severity Severity, string? Table, string? Object, string? Expected, string? Actual, string Product = Products.Engage)
{
    public static Finding Create(FindingKind kind, string? table, string? obj, string? expected = null, string? actual = null, string product = Products.Engage)
        => new(kind, SeverityRules.For(kind), table, obj, expected, actual, product);
}
