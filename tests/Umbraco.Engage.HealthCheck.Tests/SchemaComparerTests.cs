using Umbraco.Engage.HealthCheck.Core.Comparison;
using Umbraco.Engage.HealthCheck.Core.Schema;
using static Umbraco.Engage.HealthCheck.Tests.SchemaBuilder;

namespace Umbraco.Engage.HealthCheck.Tests;

public class SchemaComparerTests
{
    private static DatabaseSchema Replace(DatabaseSchema db, string table, Func<TableSchema, TableSchema?> change)
        => new(db.Tables.Select(t => t.Name == table ? change(t) : t).Where(t => t is not null).Select(t => t!).ToList());

    private static Finding Single(IReadOnlyList<Finding> findings, FindingKind kind)
        => Assert.Single(findings, f => f.Kind == kind);

    [Fact]
    public void Identical_schemas_have_no_findings()
        => Assert.Empty(SchemaComparer.Compare(Baseline(), Baseline()));

    [Fact]
    public void Missing_table_is_critical()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", _ => null);
        var f = Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.TableMissing);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("umbracoEngageChild", f.Table);
    }

    [Fact]
    public void Missing_reporting_table_is_info_only()
    {
        var actual = Replace(Baseline(), "umbracoEngageReportingDaily", _ => null);
        var f = Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.ReportingTableMissing);
        Assert.Equal(Severity.Info, f.Severity);
    }

    [Fact]
    public void Reporting_table_structure_is_not_compared()
    {
        var actual = Replace(Baseline(), "umbracoEngageReportingDaily", t => t with { Columns = [Col("other")] });
        Assert.Empty(SchemaComparer.Compare(Baseline(), actual));
    }

    [Fact]
    public void Extra_engage_table_is_reported()
    {
        var actual = new DatabaseSchema([.. Baseline().Tables, Table("umbracoEngageCustom", [Col("id")])]);
        var f = Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.TableExtra);
        Assert.Equal(Severity.Warning, f.Severity);
    }

    [Fact]
    public void Missing_column_is_critical()
    {
        var actual = Replace(Baseline(), "umbracoEngageParent", t => t with { Columns = t.Columns.Where(c => c.Name != "name").ToList() });
        Assert.Equal(Severity.Critical, Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.ColumnMissing).Severity);
    }

    [Fact]
    public void Column_type_difference_is_critical_and_shows_both_values()
    {
        var actual = Replace(Baseline(), "umbracoEngageParent", t => t with { Columns = t.Columns.Select(c => c.Name == "name" ? c with { DataType = "nvarchar(100)" } : c).ToList() });
        var f = Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.ColumnTypeDifferent);
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("nvarchar(255)", f.Expected);
        Assert.Equal("nvarchar(100)", f.Actual);
    }

    [Fact]
    public void Nullability_difference_is_critical()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { Columns = t.Columns.Select(c => c.Name == "contentTypeId" ? c with { IsNullable = true } : c).ToList() });
        Assert.Equal(Severity.Critical, Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.ColumnNullabilityDifferent).Severity);
    }

    [Fact]
    public void Collation_difference_is_info()
    {
        var expected = Replace(Baseline(), "umbracoEngageParent", t => t with { Columns = t.Columns.Select(c => c.Name == "name" ? c with { Collation = "SQL_Latin1_General_CP1_CI_AS" } : c).ToList() });
        var actual = Replace(Baseline(), "umbracoEngageParent", t => t with { Columns = t.Columns.Select(c => c.Name == "name" ? c with { Collation = "Danish_Norwegian_CI_AS" } : c).ToList() });
        Assert.Equal(Severity.Info, Single(SchemaComparer.Compare(expected, actual), FindingKind.ColumnCollationDifferent).Severity);
    }

    [Fact]
    public void System_named_default_with_same_definition_matches()
    {
        var actual = Replace(Baseline(), "umbracoEngageParent", t => t with { Columns = t.Columns.Select(c => c.Name == "name" ? c with { Default = new("DF__umbracoEn__name__1234", "('')") } : c).ToList() });
        Assert.Empty(SchemaComparer.Compare(Baseline(), actual));
    }

    [Fact]
    public void Missing_primary_key_is_critical()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { PrimaryKey = null });
        Assert.Equal(Severity.Critical, Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.PrimaryKeyMissing).Severity);
    }

    [Fact]
    public void Nonclustered_primary_key_is_a_warning()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { PrimaryKey = t.PrimaryKey! with { IsClustered = false } });
        Assert.Equal(Severity.Warning, Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.PrimaryKeyDifferent).Severity);
    }

    [Fact]
    public void Renamed_primary_key_is_a_name_warning()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { PrimaryKey = t.PrimaryKey! with { Name = "PK_old" } });
        var f = Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.ConstraintNameDifferent);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("PK_umbracoEngageChild", f.Expected);
        Assert.Equal("PK_old", f.Actual);
    }

    [Fact]
    public void Missing_unique_constraint_is_critical()
    {
        var actual = Replace(Baseline(), "umbracoEngageParent", t => t with { UniqueConstraints = [] });
        Assert.Equal(Severity.Critical, Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.UniqueConstraintMissing).Severity);
    }

    [Fact]
    public void Missing_foreign_key_is_critical()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { ForeignKeys = [] });
        Assert.Equal(Severity.Critical, Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.ForeignKeyMissing).Severity);
    }

    [Fact]
    public void Foreign_key_without_cascade_is_a_warning()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { ForeignKeys = t.ForeignKeys.Select(fk => fk with { DeleteAction = "NO_ACTION" }).ToList() });
        var f = Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.ForeignKeyDifferent);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("CASCADE", f.Expected);
    }

    [Fact]
    public void Missing_index_is_a_warning()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { Indexes = [] });
        Assert.Equal(Severity.Warning, Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.IndexMissing).Severity);
    }

    [Fact]
    public void Index_missing_include_column_is_a_warning()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { Indexes = t.Indexes.Select(i => i with { IncludedColumns = [] }).ToList() });
        Assert.Equal(Severity.Warning, Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.IndexDifferent).Severity);
    }

    [Fact]
    public void Index_with_other_name_but_same_definition_is_a_name_warning()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { Indexes = t.Indexes.Select(i => i with { Name = "IX_legacy" }).ToList() });
        var findings = SchemaComparer.Compare(Baseline(), actual);
        Assert.Equal(FindingKind.ConstraintNameDifferent, Assert.Single(findings).Kind);
    }

    [Fact]
    public void Extra_index_is_a_warning()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { Indexes = [.. t.Indexes, new("IX_custom", false, false, [new("contentTypeId", false)], [], null, false)] });
        var f = Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.ExtraObject);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("IX_custom", f.Object);
    }

    [Fact]
    public void Missing_check_constraint_is_a_warning()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { CheckConstraints = [] });
        Assert.Equal(Severity.Warning, Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.CheckConstraintMissing).Severity);
    }

    [Fact]
    public void Disabled_index_is_an_index_difference_warning()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { Indexes = t.Indexes.Select(i => i with { IsDisabled = true }).ToList() });
        var f = Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.IndexDifferent);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Equal("IX_umbracoEngageChild_parentId", f.Object);
        Assert.DoesNotContain("DISABLED", f.Expected);
        Assert.Contains("DISABLED", f.Actual);
    }

    [Fact]
    public void Disabled_foreign_key_is_a_foreign_key_difference()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { ForeignKeys = t.ForeignKeys.Select(fk => fk with { IsDisabled = true, IsNotTrusted = true }).ToList() });
        var f = Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.ForeignKeyDifferent);
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("DISABLED", f.Actual);
        Assert.DoesNotContain("DISABLED", f.Expected);
    }

    [Fact]
    public void Untrusted_foreign_key_is_a_foreign_key_difference()
    {
        var actual = Replace(Baseline(), "umbracoEngageChild", t => t with { ForeignKeys = t.ForeignKeys.Select(fk => fk with { IsNotTrusted = true }).ToList() });
        var f = Single(SchemaComparer.Compare(Baseline(), actual), FindingKind.ForeignKeyDifferent);
        Assert.Contains("NOT TRUSTED", f.Actual);
        Assert.DoesNotContain("NOT TRUSTED", f.Expected);
    }
}
