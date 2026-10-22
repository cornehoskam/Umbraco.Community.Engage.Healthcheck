namespace Umbraco.Engage.HealthCheck.Core.Schema;

public sealed record DatabaseSchema(IReadOnlyList<TableSchema> Tables);

public sealed record TableSchema(
    string Name,
    IReadOnlyList<ColumnSchema> Columns,
    KeyConstraintSchema? PrimaryKey,
    IReadOnlyList<KeyConstraintSchema> UniqueConstraints,
    IReadOnlyList<ForeignKeySchema> ForeignKeys,
    IReadOnlyList<IndexSchema> Indexes,
    IReadOnlyList<CheckConstraintSchema> CheckConstraints);

public sealed record ColumnSchema(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsIdentity,
    string? Collation,
    DefaultConstraintSchema? Default);

public sealed record DefaultConstraintSchema(string? Name, string Definition);

public sealed record KeyConstraintSchema(string? Name, bool IsClustered, IReadOnlyList<string> Columns);

public sealed record ForeignKeySchema(
    string? Name,
    IReadOnlyList<string> Columns,
    string ReferencedTable,
    IReadOnlyList<string> ReferencedColumns,
    string DeleteAction,
    string UpdateAction,
    bool IsDisabled,
    bool IsNotTrusted);

public sealed record IndexSchema(
    string Name,
    bool IsUnique,
    bool IsClustered,
    IReadOnlyList<IndexColumn> KeyColumns,
    IReadOnlyList<string> IncludedColumns,
    string? Filter,
    bool IsDisabled);

public sealed record IndexColumn(string Name, bool Descending);

public sealed record CheckConstraintSchema(string? Name, string Definition);
