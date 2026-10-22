using System.Text.Json;
using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Core.Comparison;
using Umbraco.Engage.HealthCheck.Core.Detection;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Reporting;
using Umbraco.Engage.HealthCheck.Core.State;

namespace Umbraco.Engage.HealthCheck.Tests;

public class ReportTests
{
    private static readonly Manifest M = new(1, Products.Engage, "17.5.0", "fresh", "17.5.3", MigrationState.StateKey, "{last}", "Aligned", SchemaBuilder.Baseline());
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 14, 5, 0, TimeSpan.Zero);

    [Fact]
    public void Exit_code_is_healthy_without_findings()
        => Assert.Equal(ExitCodes.Healthy, Report.Create(M, new(true, "{last}", "Aligned"), [], [], Now).ExitCode);

    [Fact]
    public void Exit_code_is_warnings_for_warning_and_info()
        => Assert.Equal(ExitCodes.Warnings, Report.Create(M, new(true, "{last}", "Aligned"), [],
            [Finding.Create(FindingKind.IndexMissing, "t", "ix"), Finding.Create(FindingKind.ColumnCollationDifferent, "t", "c")], Now).ExitCode);

    [Fact]
    public void Exit_code_is_critical_when_any_critical()
        => Assert.Equal(ExitCodes.Critical, Report.Create(M, new(true, "{last}", "Aligned"), [],
            [Finding.Create(FindingKind.IndexMissing, "t", "ix"), Finding.Create(FindingKind.TableMissing, "t", null)], Now).ExitCode);

    [Fact]
    public void Info_only_is_healthy()
        => Assert.Equal(ExitCodes.Healthy, Report.Create(M, new(true, "{last}", "Aligned"), [], [Finding.Create(FindingKind.ReportingTableMissing, "t", null)], Now).ExitCode);

    [Fact]
    public void Default_file_name_uses_danish_date_order()
        => Assert.Equal("engage-healthcheck-20-10-2026-1405.json", JsonReportWriter.DefaultFileName(Now));

    [Fact]
    public async Task Json_report_has_format_version_and_string_enums()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        await JsonReportWriter.WriteAsync(Report.Create(M, new(true, "{old}", "Aligned"), [], [Finding.Create(FindingKind.MigrationStateMismatch, "umbracoKeyValue", MigrationState.StateKey, "{last}", "{old}")], Now), path);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(1, doc.RootElement.GetProperty("formatVersion").GetInt32());
        var finding = doc.RootElement.GetProperty("findings")[0];
        Assert.Equal("MigrationStateMismatch", finding.GetProperty("kind").GetString());
        Assert.Equal("Critical", finding.GetProperty("severity").GetString());
    }


    private static string Render(Report report, ConsoleContext? context = null)
    {
        var sw = new StringWriter();
        ConsoleReporter.Write(report, sw, context ?? new ConsoleContext("'Engage' on 'localhost'", null, "r.json"));
        return sw.ToString();
    }

    [Fact]
    public void Console_output_groups_by_table_and_shows_verdict()
    {
        var report = Report.Create(M, new(true, "{last}", "Aligned"), [],
            [Finding.Create(FindingKind.IndexMissing, "umbracoEngageChild", "IX_a", "NONCLUSTERED (a)"), Finding.Create(FindingKind.TableMissing, "umbracoEngageParent", null)], Now);
        var text = Render(report);
        Assert.Contains("Engage 17.5.0 (fresh)", text);
        Assert.Contains("umbracoEngageChild", text);
        Assert.Contains("[Warning] IndexMissing IX_a", text);
        Assert.Contains("Result:           CRITICAL (exit code 2)", text);
    }


    [Fact]
    public void Recorded_migration_step_is_hidden_without_verbose()
    {
        var report = Report.Create(M, new(true, "{abc-step}", "Complete"), [], [], Now);
        var text = Render(report);
        Assert.DoesNotContain("Recorded migration step", text);
        Assert.DoesNotContain("{abc-step}", text);
        Assert.Contains("Recorded schema status:  Complete", text);
    }

    [Fact]
    public void Recorded_migration_step_is_shown_with_verbose_and_json_always_has_it()
    {
        var report = Report.Create(M, new(true, "{abc-step}", "Complete"), [], [], Now);
        var text = Render(report, new ConsoleContext("'Engage' on 'localhost'", null, "r.json", Verbose: true));
        Assert.Contains("Recorded migration step: {abc-step}", text);
        Assert.Contains("Recorded schema status:  Complete", text);
        Assert.Contains("{abc-step}", JsonSerializer.Serialize(report));
    }

    [Fact]
    public void Exactly_one_blank_line_before_summary_without_findings()
    {
        var text = Render(Report.Create(M, new(true, "{last}", "Aligned"), [], [], Now)).ReplaceLineEndings("\n");
        Assert.Contains("info\n\nSummary\n", text);
        Assert.DoesNotContain("\n\n\nSummary", text);
    }

    [Fact]
    public void Exactly_one_blank_line_before_summary_with_findings()
    {
        var report = Report.Create(M, new(true, "{last}", "Aligned"), [], [Finding.Create(FindingKind.IndexMissing, "umbracoEngageChild", "IX_a", "NONCLUSTERED (a)")], Now);
        var text = Render(report).ReplaceLineEndings("\n");
        Assert.Contains("(a)\n\nSummary\n", text);
        Assert.DoesNotContain("\n\n\nSummary", text);
    }
    [Fact]
    public void Console_summary_block_ends_the_output()
    {
        var report = Report.Create(M, new(true, "{last}", "Aligned"), [], [Finding.Create(FindingKind.TableMissing, "t", null)], Now);
        var text = Render(report, new("'dev.E' on 'localhost'", null, "engage-healthcheck-20-10-2026-1436.json"));
        var summary = text[text.IndexOf("Summary", StringComparison.Ordinal)..].ReplaceLineEndings("\n");
        Assert.Equal(
            "Summary\n" +
            "  Checked against:  Engage 17.5.0 (fresh)\n" +
            "  Engage database:  'dev.E' on 'localhost'\n" +
            "  Umbraco database: same as Engage database\n" +
            "  Add-ons:          none\n" +
            "  Findings:         1 critical, 0 warning, 0 info\n" +
            "  Result:           CRITICAL (exit code 2)\n" +
            "  Report:           engage-healthcheck-20-10-2026-1436.json\n", summary);
        Assert.DoesNotContain("Report written to", text);
    }

    [Fact]
    public void Console_summary_shows_separate_umbraco_database()
    {
        var report = Report.Create(M, new(true, "{last}", "Aligned"), [], [], Now);
        var text = Render(report, new("'E' on 'a'", "'U' on 'b'", "r.json"));
        Assert.Contains("Umbraco database: 'U' on 'b'", text);
        Assert.Contains("Result:           HEALTHY (exit code 0)", text);
    }

    [Fact]
    public void Console_summary_omits_report_line_without_path()
    {
        var report = Report.Create(M, new(true, "{last}", "Aligned"), [], [], Now);
        Assert.DoesNotContain("Report:", Render(report, new("'E' on 'a'", null, null)));
    }

    private static DetectionResult D(string basis, string? versions, bool exact = true, bool agree = true, string? recorded = null, int critical = 0, int warning = 0, params string[] alternatives)
        => new(basis, versions, versions, versions, versions, exact, agree, recorded ?? (agree ? versions : null), critical, warning, alternatives.Select(a => new DetectionAlternative(a, "fresh")).ToList());

    private static readonly DetectionResult Range = new("record+schema", "between 17.0.1 and 17.0.4", "17.0.1", "17.0.4", "17.0.4", true, true, "between 17.0.1 and 17.0.4", 0, 0, [], null, []);

    private static Report WithDetection(DetectionResult detection, string source = "detected", string? lastStep = "{last}", IReadOnlyList<AddOnResult>? addOns = null)
        => Report.Create(M, new(true, lastStep, "Aligned"), addOns ?? [], [], Now, detection, source);

    [Fact]
    public async Task Json_report_has_exactly_the_expected_properties()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        await JsonReportWriter.WriteAsync(WithDetection(Range with { Alternatives = [new DetectionAlternative("18.1.0", "aligned", Identical: true)] }, addOns: [FormsResult]), path);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        var expected = new[] { "formatVersion", "toolVersion", "checkedAtUtc", "targetVersion", "targetVariant", "targetSource", "detection", "state", "addOns", "summary", "findings", "exitCode" }.Order().ToArray();
        Assert.Equal(expected, names);
        // The tool version is the plain release version, without the "+commit" build suffix.
        Assert.DoesNotContain("+", doc.RootElement.GetProperty("toolVersion").GetString());

        // Version text only: no server, database or user names can get in.
        var detection = new[] { "basis", "versions", "firstVersion", "lastVersion", "comparedWith", "exact", "recordMatchesSchema", "recordedVersions", "critical", "warning", "alternatives", "recordedUpTo", "tableAnalysis", "variant", "notice", "cmsMajor" }.Order().ToArray();
        Assert.Equal(detection, doc.RootElement.GetProperty("detection").EnumerateObject().Select(p => p.Name).Order().ToArray());
        var addOn = doc.RootElement.GetProperty("addOns")[0];
        Assert.Equal(new[] { "product", "displayName", "version", "source", "lastStep", "detection" }.Order().ToArray(), addOn.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(detection, addOn.GetProperty("detection").EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("between 17.1.0 and 17.1.2", addOn.GetProperty("detection").GetProperty("versions").GetString());
        Assert.Equal("record+schema", doc.RootElement.GetProperty("detection").GetProperty("basis").GetString());
        Assert.Contains("\"basis\": \"record+schema\"", await File.ReadAllTextAsync(path));
        // Alternatives are structured: version text and variant, never a schema.
        var alternative = doc.RootElement.GetProperty("detection").GetProperty("alternatives")[0];
        Assert.Equal(new[] { "identical", "variant", "version" }, alternative.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal("aligned", alternative.GetProperty("variant").GetString());
    }

    [Fact]
    public void Console_shows_a_disagreeing_record_next_to_a_closest_match()
    {
        var text = Render(WithDetection(D("record+schema", "17.5.0", exact: false, agree: false, recorded: "17.4.0", critical: 1))).ReplaceLineEndings("\n");
        Assert.Contains("Schema matches no known Engage release; closest: Engage 17.5.0 (1 critical, 0 warning differences)\nUpgrade record says Engage 17.4.0\n", text);
    }

    [Fact]
    public void Console_closest_match_that_agrees_with_the_record_has_no_record_line()
        => Assert.DoesNotContain("Upgrade record says", Render(WithDetection(D("record+schema", "17.5.0", exact: false, critical: 1))));

    [Fact]
    public void Console_says_when_the_record_reaches_further_than_the_match()
    {
        var d = new DetectionResult("record+schema", "between 17.2.0 and 17.2.2", "17.2.0", "17.2.2", "17.2.2", true, true, "between 17.2.0 and 17.2.4", 0, 0, [], "17.2.4");
        Assert.Contains("Detected: Engage between 17.2.0 and 17.2.2 (upgrade record and schema agree) (upgrade record allows up to 17.2.4)", Render(WithDetection(d)));
    }

    [Fact]
    public void Console_does_not_mention_a_record_that_reaches_no_further()
        => Assert.DoesNotContain("allows up to", Render(WithDetection(Range with { RecordedUpTo = "17.0.4" })));

    private static readonly AddOnResult FormsResult = new(Products.Forms, "Engage Forms", "17.1.2", "detected", "{forms}",
        new("record+schema", "between 17.1.0 and 17.1.2", "17.1.0", "17.1.2", "17.1.2", true, true, "between 17.1.0 and 17.1.2", 0, 0, []));

    [Fact]
    public void Console_shows_a_single_detected_release()
        => Assert.Contains("Detected: Engage 17.4.0 (upgrade record and schema agree)", Render(WithDetection(D("record+schema", "17.4.0"))));

    [Fact]
    public void Console_shows_a_detected_range()
        => Assert.Contains("Detected: Engage between 17.0.1 and 17.0.4 (upgrade record and schema agree)", Render(WithDetection(Range)));

    [Fact]
    public void Console_shows_schema_only_without_a_record()
        => Assert.Contains("Detected: Engage between 17.0.1 and 17.0.4 (schema only: no upgrade record)",
            Render(WithDetection(Range with { Basis = "schema-only", RecordMatchesSchema = false, RecordedVersions = null }, lastStep: null)));

    [Fact]
    public void Console_shows_schema_only_with_an_unknown_record()
        => Assert.Contains("Detected: Engage 17.4.0 (schema only: upgrade record not recognised)",
            Render(WithDetection(D("schema-only", "17.4.0", agree: false), lastStep: "{unknown}")));

    [Fact]
    public void Console_shows_the_closest_release_when_nothing_matches()
        => Assert.Contains("Schema matches no known Engage release; closest: Engage 17.4.0 (2 critical, 3 warning differences)",
            Render(WithDetection(D("record+schema", "17.4.0", exact: false, critical: 2, warning: 3))));

    [Fact]
    public void Console_shows_a_record_that_contradicts_the_schema()
        => Assert.Contains("Upgrade record says Engage 17.4.0 but the schema matches Engage 17.5.0",
            Render(WithDetection(D("record+schema", "17.5.0", agree: false, recorded: "17.4.0"))));

    [Fact]
    public void Console_shows_alternatives()
        => Assert.Contains("  Also matches: Engage 16.5.0",
            Render(WithDetection(D("record+schema", "17.0.0", alternatives: "16.5.0"))));

    [Fact]
    public void Console_labels_alternatives_of_a_non_exact_detection_as_close()
    {
        var text = Render(WithDetection(D("record+schema", "17.0.0", exact: false, critical: 1, alternatives: "16.5.0")));
        Assert.Contains("  Also close: Engage 16.5.0", text);
        Assert.DoesNotContain("Also matches", text);
    }

    [Fact]
    public void Console_shows_expect_next_to_the_detection()
    {
        var text = Render(WithDetection(D("record+schema", "17.4.0"), source: "option")).ReplaceLineEndings("\n");
        Assert.Contains("Detected: Engage 17.4.0 (upgrade record and schema agree)\nChecked against: Engage 17.5.0 (fresh, set by --expect)\n", text);
    }

    [Fact]
    public void Console_without_expect_has_no_set_by_line()
        => Assert.DoesNotContain("set by --expect", Render(WithDetection(D("record+schema", "17.5.0"))));

    [Fact]
    public void Console_shows_add_on_option_and_its_detection()
    {
        var commerce = new AddOnResult(Products.Commerce, "Engage Commerce", "17.0.0", "option", null, D("schema-only", "17.0.0", agree: false));
        var text = Render(Report.Create(M, new(true, "{last}", "Aligned"), [commerce], [], Now)).ReplaceLineEndings("\n");
        Assert.Contains("Add-on: Engage Commerce 17.0.0 (set by --commerce)\nDetected: Engage Commerce 17.0.0 (schema only: no upgrade record)\n", text);
    }

    [Fact]
    public void Console_shows_none_detected_without_add_ons()
    {
        var text = Render(Report.Create(M, new(true, "{last}", "Aligned"), [], [], Now));
        Assert.Contains("Add-ons: none detected", text);
        Assert.Contains("  Add-ons:          none", text);
    }

    [Fact]
    public void Console_shows_add_on_line_prefix_and_summary()
    {
        var commerce = new AddOnResult(Products.Commerce, "Engage Commerce", "17.0.0", "option", null);
        var report = Report.Create(M, new(true, "{last}", "Aligned"), [FormsResult, commerce],
            [Finding.Create(FindingKind.IndexMissing, "umbracoEngageForms", "IX_f", null, null, Products.Forms), Finding.Create(FindingKind.IndexMissing, "umbracoEngageChild", "IX_c")], Now);
        var text = Render(report).ReplaceLineEndings("\n");
        Assert.Contains("Detected: Engage Forms between 17.1.0 and 17.1.2 (upgrade record and schema agree)\n", text);
        Assert.Contains("Add-on: Engage Commerce 17.0.0 (set by --commerce)\n", text);
        Assert.Contains("\n[Engage Forms] umbracoEngageForms\n", text);
        Assert.Contains("\numbracoEngageChild\n", text);
        Assert.Contains("  Umbraco database: same as Engage database\n  Add-ons:          Engage Forms between 17.1.0 and 17.1.2 (checked against 17.1.2), Engage Commerce 17.0.0 (set by --commerce)\n  Findings:", text);
    }

    [Fact]
    public void Console_summary_shows_a_single_detected_add_on_release_plainly()
    {
        var commerce = new AddOnResult(Products.Commerce, "Engage Commerce", "17.0.0", "detected", "{c}", D("record+schema", "17.0.0"));
        Assert.Contains("  Add-ons:          Engage Commerce 17.0.0\n", Render(Report.Create(M, new(true, "{last}", "Aligned"), [commerce], [], Now)).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Console_shows_unknown_version_add_on()
    {
        var unknown = new AddOnResult(Products.Forms, "Engage Forms", null, "detected", "{x}");
        var text = Render(Report.Create(M, new(true, "{last}", "Aligned"), [unknown], [], Now));
        Assert.Contains("Add-on: Engage Forms (version unknown) (detected)", text);
    }

    [Fact]
    public async Task Json_report_includes_add_ons()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        await JsonReportWriter.WriteAsync(Report.Create(M, new(true, "{last}", "Aligned"), [FormsResult],
            [Finding.Create(FindingKind.IndexMissing, "t", "ix", null, null, Products.Forms)], Now), path);
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var a = doc.RootElement.GetProperty("addOns")[0];
        Assert.Equal("Engage.Forms", a.GetProperty("product").GetString());
        Assert.Equal("17.1.2", a.GetProperty("version").GetString());
        Assert.Equal("detected", a.GetProperty("source").GetString());
        Assert.Equal("Engage.Forms", doc.RootElement.GetProperty("findings")[0].GetProperty("product").GetString());
    }
}
