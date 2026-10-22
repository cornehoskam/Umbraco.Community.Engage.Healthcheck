using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Core.Detection;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Reporting;
using Umbraco.Engage.HealthCheck.Core.Schema;
using Umbraco.Engage.HealthCheck.Core.State;

namespace Umbraco.Engage.HealthCheck.Tests;

public class TableAnalysisTests
{
    private static TableSchema WithColumn(TableSchema t, string column)
        => t with { Columns = [.. t.Columns, SchemaBuilder.Col(column, nullable: true)] };

    private static DatabaseSchema Change(DatabaseSchema s, string table, string column)
        => SchemaBuilder.Db(s.Tables.Select(t => t.Name == table ? WithColumn(t, column) : t).ToArray());

    // A changed column (not an added one) so an older release scores worse against a database that has it.
    private static DatabaseSchema ChildAsInC(DatabaseSchema s) => SchemaBuilder.Db(s.Tables.Select(t => t.Name == Child ? t with { Columns = t.Columns.Select(c => c.Name == "parentId" ? c with { DataType = "bigint" } : c).ToList() } : t).ToArray());

    private const string Parent = "umbracoEngageParent";
    private const string Child = "umbracoEngageChild";

    private static readonly DatabaseSchema A = SchemaBuilder.Baseline();
    private static readonly DatabaseSchema B = Change(A, Parent, "extraParent");
    private static readonly DatabaseSchema C = SchemaBuilder.Db([.. ChildAsInC(B).Tables, SchemaBuilder.Table("umbracoEngageNew", [SchemaBuilder.Col("id")])]);

    private static Manifest M(string version, string step, DatabaseSchema schema)
        => new(1, Products.Engage, version, "fresh", "17.5.3", MigrationState.StateKey, step, null, schema);

    private static readonly IReadOnlyList<StateGroup> Groups = VersionIndex.Build([M("17.0.0", "{s1}", A), M("17.1.0", "{s2}", B), M("17.2.0", "{s3}", C)]);

    private static IReadOnlyList<TableAnalysisEntry> Analyze(DatabaseSchema actual, string step, out DetectionOutcome outcome)
    {
        outcome = Detector.Detect(Products.Engage, actual, step, Groups);
        return TableAnalyzer.Analyze(actual, outcome, Groups);
    }

    [Fact]
    public void Exact_database_has_no_analysis()
    {
        var analysis = Analyze(B, "{s2}", out var outcome);
        Assert.True(outcome.Exact);
        Assert.Empty(analysis);
    }

    [Fact]
    public void Table_that_matches_only_an_older_release_is_lagging()
    {
        // The newest release with the parent table still in its first shape.
        var actual = SchemaBuilder.Db(C.Tables.Select(t => t.Name == Parent ? A.Tables.Single(x => x.Name == Parent) : t).ToArray());
        var analysis = Analyze(actual, "{s3}", out var outcome);

        Assert.Equal("17.2.0", outcome.Group!.RangeText);
        var entry = Assert.Single(analysis);
        Assert.Equal(new TableAnalysisEntry(Parent, TableStatus.Lagging, "17.0.0"), entry);
    }

    [Fact]
    public void Table_that_matches_only_a_newer_release_is_leading()
    {
        var actual = ChildAsInC(A);
        var analysis = Analyze(actual, "{s1}", out var outcome);

        Assert.Equal("17.0.0", outcome.Group!.RangeText);
        Assert.False(outcome.Exact);
        Assert.Equal(new TableAnalysisEntry(Child, TableStatus.Leading, "17.2.0"), Assert.Single(analysis));
    }

    [Fact]
    public void Table_that_matches_no_release_is_unknown()
    {
        var actual = Change(A, Child, "neverShipped");
        var analysis = Analyze(actual, "{s1}", out _);

        Assert.Equal(new TableAnalysisEntry(Child, TableStatus.Unknown, null), Assert.Single(analysis));
    }

    [Fact]
    public void Tables_that_follow_the_best_release_are_not_listed()
    {
        var actual = Change(A, Child, "neverShipped");
        var analysis = Analyze(actual, "{s1}", out _);
        Assert.DoesNotContain(analysis, e => e.Table == Parent);
    }

    [Fact]
    public void Only_releases_of_the_best_major_count()
    {
        // 16.0.0 has the child in the shape the database has, but it is another major.
        var groups = VersionIndex.Build([M("16.0.0", "{o}", C), M("17.0.0", "{s1}", A), M("17.2.0", "{s3}", C)]);
        var actual = ChildAsInC(A);
        var outcome = Detector.Detect(Products.Engage, actual, "{s1}", groups);

        var entry = Assert.Single(TableAnalyzer.Analyze(actual, outcome, groups));
        Assert.Equal("17.2.0", entry.MatchesVersions);
    }

    [Fact]
    public void Reporting_tables_are_not_analysed()
    {
        var actual = SchemaBuilder.Db([.. Change(A, Child, "neverShipped").Tables, SchemaBuilder.Table("umbracoEngageReportingOther", [SchemaBuilder.Col("x")])]);
        Assert.DoesNotContain(Analyze(actual, "{s1}", out _), e => e.Table.StartsWith("umbracoEngageReporting"));
    }

    [Fact]
    public void Console_shows_at_most_ten_tables_and_counts_the_rest()
    {
        var tables = Enumerable.Range(1, 13).Select(i => $"umbracoEngageT{i:00}").ToList();
        var release = SchemaBuilder.Db(tables.Select(n => SchemaBuilder.Table(n, [SchemaBuilder.Col("id")])).ToArray());
        var groups = VersionIndex.Build([M("17.0.0", "{s1}", release)]);
        var actual = SchemaBuilder.Db(tables.Select(n => SchemaBuilder.Table(n, [SchemaBuilder.Col("id"), SchemaBuilder.Col("odd", nullable: true)])).ToArray());
        var outcome = Detector.Detect(Products.Engage, actual, "{s1}", groups);
        var detection = outcome.ToResult() with { TableAnalysis = TableAnalyzer.Analyze(actual, outcome, groups) };
        Assert.Equal(13, detection.TableAnalysis!.Count);

        var lines = Render(M("17.0.0", "{s1}", release), detection);
        var header = Array.IndexOf(lines, "Tables not on Engage 17.0.0:");
        Assert.True(header >= 0);
        Assert.Equal("  umbracoEngageT01: matches no known Engage release (unknown)", lines[header + 1]);
        Assert.Equal("  umbracoEngageT10: matches no known Engage release (unknown)", lines[header + 10]);
        Assert.Equal("  … and 3 more", lines[header + 11]);
    }

    [Fact]
    public void Console_shows_a_leading_table_with_its_versions()
    {
        var actual = ChildAsInC(A);
        var outcome = Detector.Detect(Products.Engage, actual, "{s1}", Groups);
        var detection = outcome.ToResult() with { TableAnalysis = TableAnalyzer.Analyze(actual, outcome, Groups) };
        var lines = Render(M("17.0.0", "{s1}", A), detection);
        var header = Array.IndexOf(lines, "Tables not on Engage 17.0.0:");
        Assert.True(header >= 0);
        Assert.Equal("  umbracoEngageChild: matches Engage 17.2.0 (leading)", lines[header + 1]);
    }

    [Fact]
    public void Best_group_is_found_by_value_not_by_reference()
    {
        var actual = ChildAsInC(A);
        var outcome = Detector.Detect(Products.Engage, actual, "{s1}", Groups);
        var rebuilt = VersionIndex.Build([M("17.0.0", "{s1}", A), M("17.1.0", "{s2}", B), M("17.2.0", "{s3}", C)]);
        Assert.DoesNotContain(rebuilt, g => ReferenceEquals(g, outcome.Group));
        Assert.Single(TableAnalyzer.Analyze(actual, outcome, rebuilt));
    }

    private static DatabaseSchema ChildType(string type)
        => SchemaBuilder.Db(A.Tables.Select(t => t.Name == Child ? t with { Columns = t.Columns.Select(c => c.Name == "parentId" ? c with { DataType = type } : c).ToList() } : t).ToArray());

    // Five releases with the child's parentId type given per release; the third is the best group.
    private static TableAnalysisEntry AnalyzeChild(string[] releaseTypes, string actualType)
    {
        var groups = VersionIndex.Build(releaseTypes.Select((t, i) => M($"17.{i}.0", $"{{r{i}}}", ChildType(t))));
        var outcome = new DetectionOutcome(DetectionBasis.SchemaOnly, groups[2], false, [], false, [], 1, 0);
        return Assert.Single(TableAnalyzer.Analyze(ChildType(actualType), outcome, groups));
    }

    [Fact]
    public void The_nearest_matching_release_decides_the_status()
    {
        var types = new[] { "decimal(9,2)", "bigint", "smallint", "decimal(9,2)", "tinyint" };
        // decimal(9,2) matches 17.0.0 (two before) and 17.3.0 (one after).
        var leading = AnalyzeChild(types, "decimal(9,2)");
        Assert.Equal(TableStatus.Leading, leading.Status);
        Assert.Equal("17.0.0, 17.3.0", leading.MatchesVersions);
    }

    [Fact]
    public void A_tie_between_older_and_newer_counts_as_lagging()
    {
        var types = new[] { "decimal(9,2)", "bigint", "smallint", "bigint", "tinyint" };
        var tie = AnalyzeChild(types, "bigint");
        Assert.Equal(TableStatus.Lagging, tie.Status);
        Assert.Equal("17.1.0, 17.3.0", tie.MatchesVersions);
    }

    [Fact]
    public void TableMatches_agrees_with_Compare_on_the_same_table_pair()
    {
        var same = Assert.Single(A.Tables, t => t.Name == Child);
        var different = Assert.Single(ChildAsInC(A).Tables, t => t.Name == Child);

        foreach (var (expected, actual) in new[] { (same, same), (same, different) })
        {
            var viaCompare = Core.Comparison.SchemaComparer.Compare(SchemaBuilder.Db(expected), SchemaBuilder.Db(actual)).Count == 0;
            Assert.Equal(viaCompare, Core.Comparison.SchemaComparer.TableMatches(expected, actual));
        }
        Assert.True(Core.Comparison.SchemaComparer.TableMatches(same, same));
        Assert.False(Core.Comparison.SchemaComparer.TableMatches(same, different));
    }

    private static string[] Render(Manifest manifest, DetectionResult detection)
    {
        var report = Report.Create(manifest, new(true, "{s1}", "Aligned"), [], [], DateTimeOffset.UtcNow, detection);
        var writer = new StringWriter();
        ConsoleReporter.Write(report, writer, new("'E' on 'a'", null, null));
        return writer.ToString().ReplaceLineEndings("\n").Split('\n');
    }
}
