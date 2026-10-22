using Microsoft.Data.SqlClient;

namespace Umbraco.Engage.HealthCheck.Core.Schema;

/// <summary>Reads the Engage-owned part of a SQL Server schema. Issues SELECT statements only.</summary>
public static class SqlServerSchemaReader
{
    private static readonly StringComparer Ord = StringComparer.OrdinalIgnoreCase;
    private static string Join(IEnumerable<string> s) => string.Join(",", s);

    private const string EngageTablesCte = "WITH et AS (SELECT t.object_id, t.name FROM sys.tables t WHERE t.is_ms_shipped = 0 AND t.schema_id = SCHEMA_ID(N'dbo') AND t.name LIKE @prefix + '%') ";

    public static async Task<DatabaseSchema> ReadAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var tables = await QueryAsync(conn, EngageTablesCte + "SELECT object_id, name FROM et", r => (Id: r.GetInt32(0), Name: r.GetString(1)), ct);

        var columns = await QueryAsync(conn, EngageTablesCte + """
            SELECT c.object_id, c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity, c.collation_name,
                   dc.name, dc.definition, dc.is_system_named
            FROM sys.columns c
            JOIN et ON et.object_id = c.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
            ORDER BY c.object_id, c.column_id
            """, r => (Table: r.GetInt32(0), Column: new ColumnSchema(
                r.GetString(1),
                ColumnTypeFormatter.Format(r.GetString(2), r.GetInt16(3), r.GetByte(4), r.GetByte(5)),
                r.GetBoolean(6),
                r.GetBoolean(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : new DefaultConstraintSchema(r.GetBoolean(11) ? null : r.GetString(9), r.GetString(10)))), ct);

        var keyColumns = await QueryAsync(conn, EngageTablesCte + """
            SELECT kc.parent_object_id, kc.name, kc.type, kc.is_system_named, i.type_desc, col.name
            FROM sys.key_constraints kc
            JOIN et ON et.object_id = kc.parent_object_id
            JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
            ORDER BY kc.parent_object_id, kc.name, ic.key_ordinal
            """, r => (Table: r.GetInt32(0), Name: r.GetString(1), Type: r.GetString(2).Trim(), SystemNamed: r.GetBoolean(3), Clustered: r.GetString(4) == "CLUSTERED", Column: r.GetString(5)), ct);

        var fkColumns = await QueryAsync(conn, EngageTablesCte + """
            SELECT fk.parent_object_id, fk.name, fk.is_system_named, fk.delete_referential_action_desc, fk.update_referential_action_desc,
                   CASE WHEN rt.schema_id = SCHEMA_ID(N'dbo') THEN rt.name ELSE SCHEMA_NAME(rt.schema_id) + N'.' + rt.name END, pc.name, rc.name,
                   fk.is_disabled, fk.is_not_trusted
            FROM sys.foreign_keys fk
            JOIN et ON et.object_id = fk.parent_object_id
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
            JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
            JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
            ORDER BY fk.parent_object_id, fk.name, fkc.constraint_column_id
            """, r => (Table: r.GetInt32(0), Name: r.GetString(1), SystemNamed: r.GetBoolean(2), Delete: r.GetString(3), Update: r.GetString(4),
                       RefTable: r.GetString(5), Column: r.GetString(6), RefColumn: r.GetString(7), Disabled: r.GetBoolean(8), NotTrusted: r.GetBoolean(9)), ct);

        var indexColumns = await QueryAsync(conn, EngageTablesCte + """
            SELECT i.object_id, i.name, i.is_unique, i.type_desc, i.filter_definition, ic.is_descending_key, ic.is_included_column, c.name, i.is_disabled
            FROM sys.indexes i
            JOIN et ON et.object_id = i.object_id
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.is_primary_key = 0 AND i.is_unique_constraint = 0 AND i.type IN (1, 2) AND i.is_hypothetical = 0
              AND (ic.key_ordinal > 0 OR ic.is_included_column = 1)
            ORDER BY i.object_id, i.name, ic.is_included_column, ic.key_ordinal, c.name
            """, r => (Table: r.GetInt32(0), Name: r.GetString(1), Unique: r.GetBoolean(2), Clustered: r.GetString(3) == "CLUSTERED",
                       Filter: r.IsDBNull(4) ? null : r.GetString(4), Descending: r.GetBoolean(5), Included: r.GetBoolean(6), Column: r.GetString(7), Disabled: r.GetBoolean(8)), ct);

        var checks = await QueryAsync(conn, EngageTablesCte + """
            SELECT cc.parent_object_id, cc.name, cc.definition, cc.is_system_named
            FROM sys.check_constraints cc
            JOIN et ON et.object_id = cc.parent_object_id
            ORDER BY cc.parent_object_id, cc.name
            """, r => (Table: r.GetInt32(0), Check: new CheckConstraintSchema(r.GetBoolean(3) ? null : r.GetString(1), r.GetString(2))), ct);

        var result = tables
            .OrderBy(t => t.Name, Ord)
            .Select(t =>
            {
                var keys = keyColumns.Where(k => k.Table == t.Id)
                    .GroupBy(k => k.Name)
                    .Select(g => (g.First().Type, Key: new KeyConstraintSchema(g.First().SystemNamed ? null : g.Key, g.First().Clustered, g.Select(k => k.Column).ToList())))
                    .ToList();
                var fks = fkColumns.Where(f => f.Table == t.Id)
                    .GroupBy(f => f.Name)
                    .Select(g => new ForeignKeySchema(g.First().SystemNamed ? null : g.Key, g.Select(f => f.Column).ToList(), g.First().RefTable,
                        g.Select(f => f.RefColumn).ToList(), g.First().Delete, g.First().Update, g.First().Disabled, g.First().NotTrusted))
                    .ToList();
                var indexes = indexColumns.Where(i => i.Table == t.Id)
                    .GroupBy(i => i.Name)
                    .Select(g => new IndexSchema(g.Key, g.First().Unique, g.First().Clustered,
                        g.Where(i => !i.Included).Select(i => new IndexColumn(i.Column, i.Descending)).ToList(),
                        g.Where(i => i.Included).Select(i => i.Column).Order(Ord).ToList(),
                        g.First().Filter, g.First().Disabled))
                    .ToList();
                return new TableSchema(
                    t.Name,
                    columns.Where(c => c.Table == t.Id).Select(c => c.Column).ToList(),
                    keys.Where(k => k.Type == "PK").Select(k => k.Key).SingleOrDefault(),
                    keys.Where(k => k.Type == "UQ").Select(k => k.Key).OrderBy(k => Join(k.Columns), Ord).ToList(),
                    fks.OrderBy(f => Join(f.Columns), Ord).ThenBy(f => f.ReferencedTable, Ord).ThenBy(f => Join(f.ReferencedColumns), Ord).ToList(),
                    indexes.OrderBy(i => i.Name, Ord).ToList(),
                    checks.Where(c => c.Table == t.Id).Select(c => c.Check).OrderBy(c => c.Definition, Ord).ToList());
            })
            .ToList();

        return new DatabaseSchema(result);
    }

    private static async Task<List<T>> QueryAsync<T>(SqlConnection conn, string sql, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@prefix", EngageTables.Prefix);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct)) rows.Add(map(reader));
        return rows;
    }
}
