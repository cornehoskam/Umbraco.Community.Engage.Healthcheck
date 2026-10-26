using Umbraco.Community.Engage.Healthcheck.Core.Schema;

namespace Umbraco.Community.Engage.Healthcheck.Tests;

public static class SchemaBuilder
{
    public static ColumnSchema Col(string name, string type = "int", bool nullable = false, bool identity = false, DefaultConstraintSchema? def = null, string? collation = null)
        => new(name, type, nullable, identity, collation, def);

    public static TableSchema Table(
        string name,
        IEnumerable<ColumnSchema> columns,
        KeyConstraintSchema? pk = null,
        IEnumerable<KeyConstraintSchema>? uniques = null,
        IEnumerable<ForeignKeySchema>? fks = null,
        IEnumerable<IndexSchema>? indexes = null,
        IEnumerable<CheckConstraintSchema>? checks = null)
        => new(name, columns.ToList(), pk, (uniques ?? []).ToList(), (fks ?? []).ToList(), (indexes ?? []).ToList(), (checks ?? []).ToList());

    public static DatabaseSchema Db(params TableSchema[] tables) => new(tables);

    /// <summary>CREATE statements for a schema. Covers only what Baseline() and the add-on test tables use.</summary>
    public static IEnumerable<string> ToCreateSql(DatabaseSchema schema)
    {
        foreach (var t in schema.Tables)
        {
            var parts = t.Columns.Select(c =>
                $"[{c.Name}] {c.DataType}{(c.IsIdentity ? " IDENTITY(1,1)" : "")} {(c.IsNullable ? "NULL" : "NOT NULL")}" +
                (c.Default is null ? "" : $" DEFAULT {c.Default.Definition}")).ToList();
            if (t.PrimaryKey is { } pk)
                parts.Add($"CONSTRAINT [{pk.Name}] PRIMARY KEY {(pk.IsClustered ? "CLUSTERED" : "NONCLUSTERED")} ({Cols(pk.Columns)})");
            parts.AddRange(t.UniqueConstraints.Select(u => $"CONSTRAINT [{u.Name}] UNIQUE ({Cols(u.Columns)})"));
            parts.AddRange(t.CheckConstraints.Select(c => $"CONSTRAINT [{c.Name}] CHECK {c.Definition}"));
            yield return $"CREATE TABLE [{t.Name}] ({string.Join(", ", parts)})";
            foreach (var ix in t.Indexes)
                yield return $"CREATE {(ix.IsUnique ? "UNIQUE " : "")}{(ix.IsClustered ? "CLUSTERED" : "NONCLUSTERED")} INDEX [{ix.Name}] ON [{t.Name}] " +
                    $"({string.Join(", ", ix.KeyColumns.Select(k => $"[{k.Name}]{(k.Descending ? " DESC" : "")}"))})" +
                    (ix.IncludedColumns.Count > 0 ? $" INCLUDE ({Cols(ix.IncludedColumns)})" : "");
        }
        foreach (var t in schema.Tables)
            foreach (var fk in t.ForeignKeys)
                yield return $"ALTER TABLE [{t.Name}] ADD CONSTRAINT [{fk.Name}] FOREIGN KEY ({Cols(fk.Columns)}) " +
                    $"REFERENCES [{fk.ReferencedTable}] ({Cols(fk.ReferencedColumns)}) ON DELETE {fk.DeleteAction.Replace('_', ' ')} ON UPDATE {fk.UpdateAction.Replace('_', ' ')}";
    }

    private static string Cols(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"[{n}]"));

    /// <summary>A realistic healthy Engage table pair used as the baseline in most tests.</summary>
    public static DatabaseSchema Baseline() => Db(
        Table("umbracoEngageParent",
            [Col("id", identity: true), Col("key", "uniqueidentifier"), Col("name", "nvarchar(255)", nullable: true, def: new(null, "('')"))],
            pk: new("PK_umbracoEngageParent", true, ["id"]),
            uniques: [new("UQ_umbracoEngageParent_key", false, ["key"])]),
        Table("umbracoEngageChild",
            [Col("id", identity: true), Col("parentId"), Col("contentTypeId", nullable: false)],
            pk: new("PK_umbracoEngageChild", true, ["id"]),
            fks: [new("FK_umbracoEngageChild_umbracoEngageParent", ["parentId"], "umbracoEngageParent", ["id"], "CASCADE", "NO_ACTION", false, false)],
            indexes: [new("IX_umbracoEngageChild_parentId", false, false, [new("parentId", false)], ["contentTypeId"], null, false)],
            checks: [new("CK_umbracoEngageChild_contentTypeId", "([contentTypeId]>(0))")]),
        Table("umbracoEngageReportingDaily", [Col("day", "date")]));
}
