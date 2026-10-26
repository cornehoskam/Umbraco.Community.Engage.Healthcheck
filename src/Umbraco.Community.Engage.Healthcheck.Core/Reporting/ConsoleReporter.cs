using System.Text.RegularExpressions;
using Umbraco.Community.Engage.Healthcheck.Core.AddOns;
using Umbraco.Community.Engage.Healthcheck.Core.Comparison;
using Umbraco.Community.Engage.Healthcheck.Core.Detection;
using Umbraco.Community.Engage.Healthcheck.Core.Manifests;

namespace Umbraco.Community.Engage.Healthcheck.Core.Reporting;

public static partial class ConsoleReporter
{
    public static void Write(Report report, TextWriter output, ConsoleContext context, IConsoleStyle? style = null)
    {
        style ??= PlainConsoleStyle.Instance;
        if (report.Detection is not null) WriteDetection(output, style, "Engage", report.Detection, report.State.LastStep);
        if (report.TargetSource == Report.FromOption) output.WriteLine($"Checked against: Engage {report.TargetVersion} ({report.TargetVariant}, set by --expect)");
        // The step is a GUID: only with --verbose. The schema status stays, it says whether alignment is Complete.
        if (context.Verbose) output.WriteLine($"Recorded migration step: {report.State.LastStep ?? "(none)"}");
        output.WriteLine($"Recorded schema status:  {report.State.SchemaStatus ?? "(none)"}");
        if (report.AddOns.Count == 0) output.WriteLine("Add-ons: none detected");
        foreach (var a in report.AddOns)
        {
            var option = AddOnCatalog.All.FirstOrDefault(x => x.Product == a.Product)?.CliOption;
            if (a.Source == Report.FromOption && option is not null) output.WriteLine($"Add-on: {a.DisplayName} {a.Version} (set by {option})");
            else if (a.Detection is null) output.WriteLine($"Add-on: {a.DisplayName} {a.Version ?? "(version unknown)"} ({a.Source})");
            if (a.Detection is not null) WriteDetection(output, style, a.DisplayName, a.Detection, a.LastStep);
        }
        output.WriteLine($"Findings: {report.Summary.Critical} critical, {report.Summary.Warning} warning, {report.Summary.Info} info");
        // The blank line before "Summary" is written below. This one only separates the findings from this line.
        if (report.Findings.Count > 0) output.WriteLine();

        foreach (var group in report.Findings.OrderByDescending(f => f.Severity).GroupBy(f => (f.Product, Table: f.Table ?? "(database)")))
        {
            var addOn = report.AddOns.FirstOrDefault(a => a.Product == group.Key.Product);
            output.WriteLine((addOn is null ? "" : $"[{addOn.DisplayName}] ") + group.Key.Table);
            foreach (var f in group)
            {
                style.WriteLine(output, $"  [{f.Severity}] {f.Kind} {f.Object}".TrimEnd(), SeverityColor(f.Severity));
                if (f.Expected is not null) output.WriteLine($"      expected: {f.Expected}");
                if (f.Actual is not null) output.WriteLine($"      actual:   {f.Actual}");
            }
        }

        var verdict = report.ExitCode switch { ExitCodes.Healthy => "HEALTHY", ExitCodes.Warnings => "WARNINGS", _ => "CRITICAL" };
        output.WriteLine();
        output.WriteLine("Summary");
        output.WriteLine($"  Checked against:  Engage {report.TargetVersion} ({report.TargetVariant})");
        output.WriteLine($"  Engage database:  {context.EngageTarget}");
        output.WriteLine($"  Umbraco database: {context.UmbracoTarget ?? "same as Engage database"}");
        output.WriteLine($"  Add-ons:          {(report.AddOns.Count == 0 ? "none" : string.Join(", ", report.AddOns.Select(SummaryOf)))}");
        output.WriteLine($"  Findings:         {report.Summary.Critical} critical, {report.Summary.Warning} warning, {report.Summary.Info} info");
        output.Write("  Result:           ");
        style.Write(output, verdict, VerdictColor(report.ExitCode));
        output.WriteLine($" (exit code {report.ExitCode})");
        if (context.ReportPath is not null) output.WriteLine($"  Report:           {context.ReportPath}");
    }

    private static void WriteDetection(TextWriter output, IConsoleStyle style, string name, DetectionResult d, string? recordedStep)
    {
        var headline = Describe(name, d, recordedStep);
        style.WriteLine(output, headline, LineColor(headline));
        // From 18.1.0 Engage checks the status at startup and switches itself off while it is "Aligned". When the match
        // could be either major (identical databases, no CMS evidence) the line is conditional.
        if (d.Exact && Variants.IsAligned(d.Variant) && MajorOf(d.FirstVersion) is { } major)
        {
            var identical = d.Alternatives.Where(a => a.Identical).Select(a => MajorOf(a.Version)).OfType<int>().ToList();
            if (major >= 18 && identical.All(m => m >= 18)) output.WriteLine("  Engage is disabled until CompleteAlignSchema.sql is run");
            else if (major >= 18 || identical.Any(m => m >= 18)) output.WriteLine("  On Engage 18.1.0 or later: Engage is disabled until CompleteAlignSchema.sql is run");
        }
        if (d.Notice is not null) style.WriteLine(output, $"  {d.Notice}", ConsolePalette.Blocked);
        // An exact match already names the record in its own line; a closest match must not hide a disagreeing record (https://github.com/umbraco/Umbraco.Engage.Issues/issues/99).
        if (!d.Exact && d.Versions is not null && d.Basis == DetectionBasis.RecordAndSchema && !d.RecordMatchesSchema)
            style.WriteLine(output, $"Upgrade record says {name} {d.RecordedVersions}", ConsolePalette.Attention);
        // Identical alternatives are already named in the headline.
        var others = d.Alternatives.Where(a => !a.Identical).ToList();
        if (others.Count > 0) output.WriteLine($"  {(d.Exact ? "Also matches" : "Also close")}: {string.Join(", ", others.Select(a => $"{name} {Label(a)}"))}");
        WriteTableAnalysis(output, name, d);
    }

    private static ConsoleColor SeverityColor(Severity s) => s switch { Severity.Critical => ConsolePalette.Critical, Severity.Warning => ConsolePalette.Warning, _ => ConsolePalette.Info };

    private static ConsoleColor VerdictColor(int exitCode) => exitCode switch { ExitCodes.Healthy => ConsolePalette.Healthy, ExitCodes.Warnings => ConsolePalette.Warning, _ => ConsolePalette.Critical };

    private static ConsoleColor? LineColor(string line)
        => line.StartsWith("Detected:", StringComparison.Ordinal) ? ConsolePalette.Detected
         : line.StartsWith("Schema matches no known", StringComparison.Ordinal) || line.StartsWith("Upgrade record says", StringComparison.Ordinal) ? ConsolePalette.Attention
         : null;

    private static string Label(DetectionAlternative a) => Variants.IsFresh(a.Variant) ? a.Version : $"{a.Version} ({a.Variant})";

    private static int? MajorOf(string? version)
        => version is not null && VersionText().Match(version) is { Success: true } m ? Version.Parse(m.Value).Major : null;

    private const int MaxTableLines = 10;

    private static void WriteTableAnalysis(TextWriter output, string name, DetectionResult d)
    {
        if (d.Exact || d.Versions is null || d.TableAnalysis is not { Count: > 0 } tables) return;
        output.WriteLine($"Tables not on {name} {d.Versions}:");
        foreach (var t in tables.Take(MaxTableLines))
            output.WriteLine(t.MatchesVersions is null ? $"  {t.Table}: matches no known {name} release ({t.Status})" : $"  {t.Table}: matches {name} {t.MatchesVersions} ({t.Status})");
        if (tables.Count > MaxTableLines) output.WriteLine($"  … and {tables.Count - MaxTableLines} more");
    }


    public static string Describe(string name, DetectionResult d, string? recordedStep)
    {
        if (d.Versions is null) return $"Detected: {name} (version unknown: no known releases to compare with)";
        var note = Variants.IsAligned(d.Variant) ? AlignedNote : Variants.IsComplete(d.Variant) ? CompleteNote : null;
        var found = $"{name} {d.Versions}";
        if (d.Basis == DetectionBasis.RecordOnly) return $"Detected: {found} (upgrade record only: no tables to compare)";
        if (!d.Exact)
            return $"Schema matches no known {name} release; closest: {found}{(d.Variant is null || Variants.IsFresh(d.Variant) ? "" : $" ({d.Variant.ToLowerInvariant()})")} ({d.Critical} critical, {d.Warning} warning differences)";
        // Without CMS evidence an identical database in another major is just as likely: name both.
        var identical = d.Alternatives.Where(a => a.Identical).Select(a => a.Version).ToList();
        if (identical.Count > 0) found = $"{found} or {string.Join(" or ", identical)} (identical databases)";
        if (note is not null) found += $" ({note})";
        if (d.Basis == DetectionBasis.SchemaOnly)
            return $"Detected: {found} (schema only: {(recordedStep is null ? "no upgrade record" : "upgrade record not recognised")})";
        if (note is not null && d.RecordMatchesSchema) return $"Detected: {found}{AllowsMore(d)}";
        return d.RecordMatchesSchema
            ? $"Detected: {found} (upgrade record and schema agree){AllowsMore(d)}"
            : $"Upgrade record says {name} {d.RecordedVersions} but the schema matches {found}";
    }

    private const string AlignedNote = "aligned: CompleteAlignSchema.sql has not been run yet";
    private const string CompleteNote = "alignment completed";

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex VersionText();

    // The record's step can cover newer releases of the same major than the schema shows, e.g. a 17.2.4 upgrade on a
    // 17.2.0-17.2.2 schema. RecordedUpTo never crosses majors.
    private static string AllowsMore(DetectionResult d)
        => d.RecordedUpTo is not null && d.LastVersion is not null
           && Version.TryParse(d.RecordedUpTo, out var upTo) && Version.TryParse(d.LastVersion, out var last) && upTo > last
            ? $" (upgrade record allows up to {d.RecordedUpTo})"
            : "";

    private static string SummaryOf(AddOnResult a)
    {
        var option = AddOnCatalog.All.FirstOrDefault(x => x.Product == a.Product)?.CliOption;
        if (a.Source == Report.FromOption && option is not null) return $"{a.DisplayName} {a.Version} (set by {option})";
        if (a.Version is null) return $"{a.DisplayName} (version unknown)";
        return a.Detection?.Versions is { } versions && versions != a.Version
            ? $"{a.DisplayName} {versions} (checked against {a.Version})"
            : $"{a.DisplayName} {a.Version}";
    }
}
