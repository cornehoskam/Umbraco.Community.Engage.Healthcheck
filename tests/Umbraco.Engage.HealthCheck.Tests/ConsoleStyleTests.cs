using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Core.Comparison;
using Umbraco.Engage.HealthCheck.Core.Detection;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Reporting;
using Umbraco.Engage.HealthCheck.Core.State;

namespace Umbraco.Engage.HealthCheck.Tests;

/// <summary>Records what was written and in which colour, and still writes the plain text.</summary>
internal sealed class RecordingStyle : IConsoleStyle
{
    public List<(string Text, ConsoleColor? Color)> Calls { get; } = [];

    public void Write(TextWriter output, string text, ConsoleColor? color = null)
    {
        Calls.Add((text, color));
        output.Write(text);
    }

    public ConsoleColor? ColorOf(string startsWith) => Calls.Single(c => c.Text.StartsWith(startsWith, StringComparison.Ordinal)).Color;
}

public class ConsoleStyleTests
{
    private static readonly Manifest M = new(1, Products.Engage, "17.5.0", "fresh", "17.5.3", MigrationState.StateKey, "{last}", "Aligned", SchemaBuilder.Baseline());

    private static DetectionResult D(string versions, bool exact = true, string? notice = null, bool recordMatches = true)
        => new(DetectionBasis.RecordAndSchema, versions, versions, versions, versions, exact, recordMatches, "17.0.0", exact ? 0 : 1, 0, [], null, [], Variants.Fresh, notice);

    private static (string Plain, RecordingStyle Style) Render(Report report)
    {
        var style = new RecordingStyle();
        var sw = new StringWriter();
        ConsoleReporter.Write(report, sw, new ConsoleContext("db", null, null), style);
        return (sw.ToString(), style);
    }

    private static Report WithFindings(params Finding[] findings) => Report.Create(M, new(true, "{last}", "Aligned"), [], findings, DateTimeOffset.UnixEpoch);

    private static Report WithDetection(DetectionResult d) => Report.Create(M, new(true, "S", "Aligned"), [], [], DateTimeOffset.UnixEpoch, d, Report.FromDetection);

    // Plain text stays the same

    [Fact]
    public void Plain_style_and_default_output_are_identical()
    {
        var report = WithFindings(Finding.Create(FindingKind.TableMissing, "t", null));
        var defaultOut = new StringWriter();
        var plainOut = new StringWriter();
        var ctx = new ConsoleContext("db", null, null);
        ConsoleReporter.Write(report, defaultOut, ctx);
        ConsoleReporter.Write(report, plainOut, ctx, PlainConsoleStyle.Instance);
        Assert.Equal(defaultOut.ToString(), plainOut.ToString());
        Assert.Equal(defaultOut.ToString(), Render(report).Plain);
    }

    [Fact]
    public void Labels_stay_as_text_in_every_style()
    {
        var text = Render(WithFindings(Finding.Create(FindingKind.TableMissing, "t", null), Finding.Create(FindingKind.IndexMissing, "t", "ix"))).Plain;
        Assert.Contains("[Critical]", text);
        Assert.Contains("[Warning]", text);
        Assert.Contains("CRITICAL", text);
    }

    // Findings

    [Theory]
    [InlineData(FindingKind.TableMissing, Severity.Critical, ConsoleColor.Red)]
    [InlineData(FindingKind.IndexMissing, Severity.Warning, ConsoleColor.Yellow)]
    [InlineData(FindingKind.ReportingTableMissing, Severity.Info, ConsoleColor.DarkGray)]
    public void Findings_are_coloured_by_severity(FindingKind kind, Severity severity, ConsoleColor expected)
    {
        var finding = Finding.Create(kind, "t", "x");
        Assert.Equal(severity, finding.Severity);
        var (_, style) = Render(WithFindings(finding));
        Assert.Equal(expected, style.ColorOf($"  [{severity}]"));
    }

    [Fact]
    public void Table_group_header_has_the_default_colour()
    {
        var (plain, style) = Render(WithFindings(Finding.Create(FindingKind.TableMissing, "umbracoEngageThing", null)));
        Assert.Contains("umbracoEngageThing", plain);
        Assert.DoesNotContain(style.Calls, c => c.Text.StartsWith("umbracoEngageThing", StringComparison.Ordinal));
    }

    // Results

    [Theory]
    [InlineData(0, "HEALTHY", ConsoleColor.Green)]
    [InlineData(1, "WARNINGS", ConsoleColor.Yellow)]
    [InlineData(2, "CRITICAL", ConsoleColor.Red)]
    public void Result_word_is_coloured(int exitCode, string word, ConsoleColor expected)
    {
        var findings = exitCode switch
        {
            0 => Array.Empty<Finding>(),
            1 => [Finding.Create(FindingKind.IndexMissing, "t", "ix")],
            _ => [Finding.Create(FindingKind.TableMissing, "t", null)],
        };
        var report = WithFindings(findings);
        Assert.Equal(exitCode, report.ExitCode);
        var (plain, style) = Render(report);
        Assert.Equal(expected, style.ColorOf(word));
        Assert.Contains($"Result:           {word} (exit code {exitCode})", plain);
    }

    // Detection lines

    [Fact]
    public void Detected_line_is_cyan()
        => Assert.Equal(ConsoleColor.Cyan, Render(WithDetection(D("17.4.0"))).Style.ColorOf("Detected:"));

    [Fact]
    public void No_known_release_line_is_yellow()
        => Assert.Equal(ConsoleColor.Yellow, Render(WithDetection(D("17.4.0", exact: false))).Style.ColorOf("Schema matches no known"));

    [Fact]
    public void Upgrade_record_says_extra_line_is_yellow()
    {
        // Closest match while the record disagrees with the schema: the extra line from issue #99.
        var (_, style) = Render(WithDetection(D("17.4.0", exact: false, recordMatches: false)));
        Assert.Equal(ConsoleColor.Yellow, style.ColorOf("Upgrade record says"));
    }

    [Fact]
    public void Upgrade_record_disagreement_headline_is_yellow()
        => Assert.Equal(ConsoleColor.Yellow, Render(WithDetection(D("17.4.0", recordMatches: false))).Style.ColorOf("Upgrade record says"));

    [Fact]
    public void Blocked_notice_is_red()
    {
        const string notice = "Engage v18 upgrade blocked: CompleteAlignSchema.sql has not been run";
        Assert.Equal(ConsoleColor.Red, Render(WithDetection(D("17.4.0", notice: notice))).Style.ColorOf("  " + notice));
    }

    [Fact]
    public void A_healthy_report_colours_only_the_detection_and_the_result()
        => Assert.Equal(["Detected:", "HEALTHY"], Render(WithDetection(D("17.4.0"))).Style.Calls.Where(c => c.Color is not null).Select(c => c.Text.Split(' ')[0]));

    // The real style

    [Fact]
    public void Console_style_writes_the_plain_text_and_no_ansi()
    {
        var sw = new StringWriter();
        new ConsoleColorStyle().Write(sw, "hello", ConsoleColor.Red);
        Assert.Equal("hello", sw.ToString());
    }

    // Colour policy

    [Theory]
    [InlineData(null, null, false, false, true)]
    [InlineData("", null, false, false, true)]
    [InlineData("1", null, false, false, false)]
    [InlineData("anything", null, false, false, false)]
    [InlineData(null, "dumb", false, false, false)]
    [InlineData(null, "DUMB", false, false, false)]
    [InlineData(null, "xterm-256color", false, false, true)]
    [InlineData(null, null, true, false, false)]
    [InlineData(null, null, false, true, false)]
    [InlineData("", "xterm", false, true, false)]
    public void Colour_policy(string? noColorEnv, string? term, bool flag, bool redirected, bool expected)
        => Assert.Equal(expected, OutputPolicy.ColorEnabled(noColorEnv, term, flag, redirected));

    // Banner policy

    [Theory]
    [InlineData(null, false, false, true)]
    [InlineData("", false, false, true)]
    [InlineData("true", false, false, false)]
    [InlineData("1", false, false, false)]
    [InlineData("false", false, false, true)]
    [InlineData("0", false, false, true)]
    [InlineData(null, true, false, false)]
    [InlineData(null, false, true, false)]
    public void Banner_policy(string? ci, bool noBanner, bool redirected, bool expected)
        => Assert.Equal(expected, OutputPolicy.BannerEnabled(ci, noBanner, redirected));

    // Banner

    [Fact]
    public void Banner_fits_six_lines_and_78_columns_in_plain_ascii()
    {
        foreach (var version in new[] { "1.0.0", "17.5.0-rc.12", "123.456.789-preview.1234" })
        {
            var lines = Banner.Lines(version);
            Assert.InRange(lines.Count, 1, Banner.MaxLines);
            foreach (var line in lines)
            {
                Assert.True(line.Length <= Banner.MaxWidth, line);
                Assert.All(line, ch => Assert.InRange(ch, ' ', '~'));
            }
        }
    }

    [Fact]
    public void Banner_lines_are_exactly_the_figlet_small_art()
        => Assert.Equal(
        [
            """ ___                           _  _          _ _   _       _           _""",
            """| __|_ _  __ _ __ _ __ _ ___  | || |___ __ _| | |_| |_  __| |_  ___ __| |__""",
            """| _|| ' \/ _` / _` / _` / -_) | __ / -_) _` | |  _| ' \/ _| ' \/ -_) _| / /""",
            """|___|_||_\__, \__,_\__, \___| |_||_\___\__,_|_|\__|_||_\__|_||_\___\__|_\_\""",
            """         |___/     |___/""",
            "v1.2.3",
        ], Banner.Lines("1.2.3"));

    [Fact]
    public void Banner_ends_with_the_version_line()
        => Assert.Equal("v17.5.0", Banner.Lines("17.5.0")[^1]);

    [Fact]
    public void Banner_is_cyan_with_a_recording_style_and_plain_text_otherwise()
    {
        var style = new RecordingStyle();
        var sw = new StringWriter();
        Banner.Write(sw, style, "1.2.3");
        Assert.All(style.Calls.Where(c => c.Text.Length > 0), c => Assert.Equal(ConsoleColor.Cyan, c.Color));

        var plain = new StringWriter();
        Banner.Write(plain, PlainConsoleStyle.Instance, "1.2.3");
        Assert.Equal(sw.ToString(), plain.ToString());
        Assert.DoesNotContain('\u001b', plain.ToString());
    }

    [Theory]
    [InlineData("17.5.0+abc123def", "17.5.0")]
    [InlineData("17.5.0-rc.1+abc", "17.5.0-rc.1")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData(null, "0.0.0")]
    [InlineData("", "0.0.0")]
    public void Version_drops_the_commit_suffix(string? input, string expected)
        => Assert.Equal(expected, Banner.VersionOf(input));

    [Fact]
    public void Json_report_has_no_colour_codes()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(WithFindings(Finding.Create(FindingKind.TableMissing, "t", null)));
        Assert.DoesNotContain('\u001b', json);
        Assert.DoesNotContain("\\u001B", json, StringComparison.OrdinalIgnoreCase);
    }
}
