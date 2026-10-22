using Umbraco.Engage.HealthCheck.Core.Schema;

namespace Umbraco.Engage.HealthCheck.Core.Comparison;

public static class SchemaComparer
{
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<Finding> Compare(DatabaseSchema expected, DatabaseSchema actual)
    {
        var findings = new List<Finding>();
        var actualTables = actual.Tables.ToDictionary(t => t.Name, Names);
        var expectedNames = expected.Tables.Select(t => t.Name).ToHashSet(Names);

        foreach (var e in expected.Tables)
        {
            var exists = actualTables.TryGetValue(e.Name, out var a);
            if (EngageTables.IsReporting(e.Name))
            {
                if (!exists) findings.Add(Finding.Create(FindingKind.ReportingTableMissing, e.Name, null));
                continue;
            }
            if (!exists) { findings.Add(Finding.Create(FindingKind.TableMissing, e.Name, null)); continue; }
            CompareTable(e, a!, findings);
        }

        foreach (var a in actual.Tables)
            if (!expectedNames.Contains(a.Name) && !EngageTables.IsReporting(a.Name))
                findings.Add(Finding.Create(FindingKind.TableExtra, a.Name, null));

        return findings;
    }

    /// <summary>True when the table has no differences from the expected one. This is the single definition of "same" for tables.</summary>
    public static bool TableMatches(TableSchema expected, TableSchema actual)
    {
        var findings = new List<Finding>();
        CompareTable(expected, actual, findings);
        return findings.Count == 0;
    }

    private static void CompareTable(TableSchema e, TableSchema a, List<Finding> f)
    {
        CompareColumns(e, a, f);
        ComparePrimaryKey(e, a, f);
        CompareUniqueConstraints(e, a, f);
        CompareForeignKeys(e, a, f);
        CompareIndexes(e, a, f);
        CompareChecks(e, a, f);
    }

    private static void CompareColumns(TableSchema e, TableSchema a, List<Finding> f)
    {
        var actualCols = a.Columns.ToDictionary(c => c.Name, Names);
        foreach (var ec in e.Columns)
        {
            if (!actualCols.TryGetValue(ec.Name, out var ac)) { f.Add(Finding.Create(FindingKind.ColumnMissing, e.Name, ec.Name, ec.DataType)); continue; }
            if (!string.Equals(ec.DataType, ac.DataType, StringComparison.OrdinalIgnoreCase))
                f.Add(Finding.Create(FindingKind.ColumnTypeDifferent, e.Name, ec.Name, ec.DataType, ac.DataType));
            if (ec.IsNullable != ac.IsNullable)
                f.Add(Finding.Create(FindingKind.ColumnNullabilityDifferent, e.Name, ec.Name, Nullable(ec), Nullable(ac)));
            if (ec.IsIdentity != ac.IsIdentity)
                f.Add(Finding.Create(FindingKind.ColumnIdentityDifferent, e.Name, ec.Name, ec.IsIdentity ? "IDENTITY" : "no identity", ac.IsIdentity ? "IDENTITY" : "no identity"));
            if (ec.Collation is not null && ac.Collation is not null && !string.Equals(ec.Collation, ac.Collation, StringComparison.OrdinalIgnoreCase))
                f.Add(Finding.Create(FindingKind.ColumnCollationDifferent, e.Name, ec.Name, ec.Collation, ac.Collation));
            CompareDefault(e.Name, ec, ac, f);
        }
        foreach (var ac in a.Columns)
            if (!e.Columns.Any(c => Names.Equals(c.Name, ac.Name)))
                f.Add(Finding.Create(FindingKind.ColumnExtra, e.Name, ac.Name, null, ac.DataType));
    }

    private static string Nullable(ColumnSchema c) => c.IsNullable ? "NULL" : "NOT NULL";

    private static void CompareDefault(string table, ColumnSchema ec, ColumnSchema ac, List<Finding> f)
    {
        if (ec.Default is null && ac.Default is null) return;
        if (ec.Default is null || ac.Default is null || !SameSql(ec.Default.Definition, ac.Default.Definition))
        {
            f.Add(Finding.Create(FindingKind.ColumnDefaultDifferent, table, ec.Name, ec.Default?.Definition, ac.Default?.Definition));
            return;
        }
        CompareName(table, ec.Default.Name, ac.Default.Name, f);
    }

    private static void ComparePrimaryKey(TableSchema e, TableSchema a, List<Finding> f)
    {
        if (e.PrimaryKey is null)
        {
            if (a.PrimaryKey is not null) f.Add(Finding.Create(FindingKind.ExtraObject, e.Name, a.PrimaryKey.Name ?? "PRIMARY KEY", null, Describe(a.PrimaryKey)));
            return;
        }
        if (a.PrimaryKey is null) { f.Add(Finding.Create(FindingKind.PrimaryKeyMissing, e.Name, e.PrimaryKey.Name, Describe(e.PrimaryKey))); return; }
        if (!SameKey(e.PrimaryKey, a.PrimaryKey))
            f.Add(Finding.Create(FindingKind.PrimaryKeyDifferent, e.Name, e.PrimaryKey.Name, Describe(e.PrimaryKey), Describe(a.PrimaryKey)));
        CompareName(e.Name, e.PrimaryKey.Name, a.PrimaryKey.Name, f);
    }

    private static void CompareUniqueConstraints(TableSchema e, TableSchema a, List<Finding> f)
    {
        var unmatched = a.UniqueConstraints.ToList();
        foreach (var eu in e.UniqueConstraints)
        {
            var match = unmatched.FirstOrDefault(au => SameColumns(eu.Columns, au.Columns));
            if (match is null) { f.Add(Finding.Create(FindingKind.UniqueConstraintMissing, e.Name, eu.Name, Describe(eu))); continue; }
            unmatched.Remove(match);
            CompareName(e.Name, eu.Name, match.Name, f);
        }
        foreach (var au in unmatched) f.Add(Finding.Create(FindingKind.ExtraObject, e.Name, au.Name ?? "UNIQUE", null, Describe(au)));
    }

    private static void CompareForeignKeys(TableSchema e, TableSchema a, List<Finding> f)
    {
        var unmatched = a.ForeignKeys.ToList();
        foreach (var efk in e.ForeignKeys)
        {
            var match = unmatched.FirstOrDefault(afk => SameColumns(efk.Columns, afk.Columns)
                && Names.Equals(efk.ReferencedTable, afk.ReferencedTable)
                && SameColumns(efk.ReferencedColumns, afk.ReferencedColumns));
            if (match is null) { f.Add(Finding.Create(FindingKind.ForeignKeyMissing, e.Name, efk.Name, Describe(efk))); continue; }
            unmatched.Remove(match);
            if (efk.DeleteAction != match.DeleteAction || efk.UpdateAction != match.UpdateAction
                || (!efk.IsDisabled && match.IsDisabled) || (!efk.IsNotTrusted && match.IsNotTrusted))
                f.Add(Finding.Create(FindingKind.ForeignKeyDifferent, e.Name, efk.Name, Describe(efk), Describe(match)));
            CompareName(e.Name, efk.Name, match.Name, f);
        }
        foreach (var afk in unmatched) f.Add(Finding.Create(FindingKind.ExtraObject, e.Name, afk.Name ?? "FOREIGN KEY", null, Describe(afk)));
    }

    private static void CompareIndexes(TableSchema e, TableSchema a, List<Finding> f)
    {
        var unmatched = a.Indexes.ToList();
        foreach (var ei in e.Indexes)
        {
            var byName = unmatched.FirstOrDefault(ai => Names.Equals(ai.Name, ei.Name));
            if (byName is not null)
            {
                unmatched.Remove(byName);
                if (!SameIndex(ei, byName) || NewlyDisabled(ei, byName)) f.Add(Finding.Create(FindingKind.IndexDifferent, e.Name, ei.Name, Describe(ei), Describe(byName)));
                continue;
            }
            var byDefinition = unmatched.FirstOrDefault(ai => SameIndex(ei, ai));
            if (byDefinition is not null)
            {
                unmatched.Remove(byDefinition);
                f.Add(Finding.Create(FindingKind.ConstraintNameDifferent, e.Name, ei.Name, ei.Name, byDefinition.Name));
                if (NewlyDisabled(ei, byDefinition)) f.Add(Finding.Create(FindingKind.IndexDifferent, e.Name, ei.Name, Describe(ei), Describe(byDefinition)));
                continue;
            }
            f.Add(Finding.Create(FindingKind.IndexMissing, e.Name, ei.Name, Describe(ei)));
        }
        foreach (var ai in unmatched) f.Add(Finding.Create(FindingKind.ExtraObject, e.Name, ai.Name, null, Describe(ai)));
    }

    private static void CompareChecks(TableSchema e, TableSchema a, List<Finding> f)
    {
        var unmatched = a.CheckConstraints.ToList();
        foreach (var ec in e.CheckConstraints)
        {
            var match = unmatched.FirstOrDefault(ac => SameSql(ec.Definition, ac.Definition));
            if (match is null) { f.Add(Finding.Create(FindingKind.CheckConstraintMissing, e.Name, ec.Name, ec.Definition)); continue; }
            unmatched.Remove(match);
            CompareName(e.Name, ec.Name, match.Name, f);
        }
        foreach (var ac in unmatched) f.Add(Finding.Create(FindingKind.ExtraObject, e.Name, ac.Name ?? "CHECK", null, ac.Definition));
    }

    // A null name means SQL Server generated it, which differs per database, so it is never compared.
    private static void CompareName(string table, string? expected, string? actual, List<Finding> f)
    {
        if (expected is null || actual is null || Names.Equals(expected, actual)) return;
        f.Add(Finding.Create(FindingKind.ConstraintNameDifferent, table, expected, expected, actual));
    }

    private static bool SameColumns(IReadOnlyList<string> x, IReadOnlyList<string> y) => x.SequenceEqual(y, Names);
    private static bool SameKey(KeyConstraintSchema x, KeyConstraintSchema y) => x.IsClustered == y.IsClustered && SameColumns(x.Columns, y.Columns);
    private static bool SameSql(string x, string y) => string.Equals(x.Trim(), y.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool NewlyDisabled(IndexSchema expected, IndexSchema actual) => !expected.IsDisabled && actual.IsDisabled;

    private static bool SameIndex(IndexSchema x, IndexSchema y) =>
        x.IsUnique == y.IsUnique && x.IsClustered == y.IsClustered
        && x.KeyColumns.Count == y.KeyColumns.Count
        && x.KeyColumns.Zip(y.KeyColumns).All(p => Names.Equals(p.First.Name, p.Second.Name) && p.First.Descending == p.Second.Descending)
        && x.IncludedColumns.Order(Names).SequenceEqual(y.IncludedColumns.Order(Names), Names)
        && string.Equals(x.Filter?.Trim(), y.Filter?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Describe(KeyConstraintSchema k) => $"{(k.IsClustered ? "CLUSTERED" : "NONCLUSTERED")} ({string.Join(", ", k.Columns)})";
    private static string Describe(ForeignKeySchema k) => $"({string.Join(", ", k.Columns)}) → {k.ReferencedTable}({string.Join(", ", k.ReferencedColumns)}) ON DELETE {k.DeleteAction} ON UPDATE {k.UpdateAction}{(k.IsDisabled ? " DISABLED" : "")}{(k.IsNotTrusted ? " NOT TRUSTED" : "")}";
    private static string Describe(IndexSchema i)
    {
        var keys = string.Join(", ", i.KeyColumns.Select(c => c.Descending ? $"{c.Name} DESC" : c.Name));
        var include = i.IncludedColumns.Count > 0 ? $" INCLUDE ({string.Join(", ", i.IncludedColumns)})" : "";
        var filter = i.Filter is not null ? $" WHERE {i.Filter}" : "";
        return $"{(i.IsUnique ? "UNIQUE " : "")}{(i.IsClustered ? "CLUSTERED" : "NONCLUSTERED")} ({keys}){include}{filter}{(i.IsDisabled ? " DISABLED" : "")}";
    }
}
