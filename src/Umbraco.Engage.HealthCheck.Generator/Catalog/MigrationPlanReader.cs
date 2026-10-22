using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Umbraco.Engage.HealthCheck.Core;

namespace Umbraco.Engage.HealthCheck.Generator.Catalog;

/// <summary>Reads a migration plan's last target state, from sample text or from a release tag in a local repo (read-only).</summary>
public static partial class MigrationPlanReader
{
    [GeneratedRegex(@"\bTo\s*(?:<[^>()]*>)?\s*\(")]
    private static partial Regex ToCallStart();

    [GeneratedRegex("""^\s*(?:@"(?<verbatim>(?:[^"]|"")*)"|"(?<lit>[^"\\]*)"|nameof\s*\(\s*(?<name>[\w.]+)\s*\))\s*\)""")]
    private static partial Regex ToArgument();

    [GeneratedRegex(@"/\*.*?\*/|//[^\r\n]*", RegexOptions.Singleline)]
    private static partial Regex Comments();

    /// <summary>The target state of the last <c>To(...)</c> call, or null when there is none or it is not readable.</summary>
    public static string? LastStep(string planText) => TryLastStep(planText, out var step, out _) ? step : null;

    /// <summary>
    /// Fails closed: every <c>To(...)</c> call is counted, and the last one must have a plain string literal
    /// (normal or verbatim) or <c>nameof(...)</c> as its only argument. Anything else gives a reason instead of a guess.
    /// </summary>
    public static bool TryLastStep(string planText, [NotNullWhen(true)] out string? step, out string? problem)
    {
        step = null;
        var code = Comments().Replace(planText, " ");
        var last = ToCallStart().Matches(code).LastOrDefault();
        if (last is null) { problem = "no To(...) step found in the plan"; return false; }
        var arg = ToArgument().Match(code[(last.Index + last.Length)..]);
        if (!arg.Success)
        {
            var end = Math.Min(code.Length, last.Index + 80);
            problem = $"the last To(...) call has an argument that is not a string literal or nameof: {code[last.Index..end].ReplaceLineEndings(" ")}";
            return false;
        }
        step = arg.Groups["verbatim"].Success ? arg.Groups["verbatim"].Value.Replace("\"\"", "\"")
             : arg.Groups["lit"].Success ? arg.Groups["lit"].Value
             : arg.Groups["name"].Value.Split('.')[^1];
        problem = null;
        return true;
    }
    /// <summary>The plan file inside a product repo, relative to the repo folder.</summary>
    public static string PlanPath(string product, string major)
        => product switch
        {
            Products.Engage => major is "V13" or "V16"
                ? "src/Umbraco.Engage.Web/Migrations/UmsMigrationPlan.cs"
                : "src/Umbraco.Engage.Web/Migrations/EngageMigrationPlan.cs",
            Products.Forms => "Umbraco.Engage.Forms/Migrations/UmbracoFormsMigrationPlan.cs",
            Products.Commerce => "Umbraco.Engage.Commerce/Migrations/UmbracoCommerceMigrationPlan.cs",
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, "Unknown product."),
        };

    /// <summary>The repo folder for a product and major under <paramref name="reposRoot"/>, e.g. Umbraco.Engage/Umbraco.Engage.V17.</summary>
    public static string RepoFolder(string reposRoot, string product, string major)
    {
        var name = product switch
        {
            Products.Engage => "Umbraco.Engage",
            Products.Forms => "Umbraco.Engage.Forms",
            Products.Commerce => "Umbraco.Engage.Commerce",
            _ => throw new ArgumentOutOfRangeException(nameof(product), product, "Unknown product."),
        };
        return Path.Combine(reposRoot, name, $"{name}.{major}");
    }

    public static string TagFor(string version) => $"release-{version}";

    /// <summary>The plan text at the release tag, or null with a reason when the repo, tag or file is not there.</summary>
    /// <summary>
    /// The plan file paths to try at a tag, most likely first. Some Engage tags (V17 up to 17.1.1) still use the older
    /// plan file name, and Engage tags 13.0.0-13.4.0 have no <c>src/</c> folder, so Engage gets all four.
    /// </summary>
    public static IReadOnlyList<string> CandidatePaths(string product, string major)
    {
        List<string> paths = [PlanPath(product, major)];
        if (product != Products.Engage) return paths;
        paths.Add(PlanPath(product, paths[0].EndsWith("UmsMigrationPlan.cs", StringComparison.Ordinal) ? "V17" : "V13"));
        const string src = "src/";
        return [.. paths, .. paths.Where(p => p.StartsWith(src, StringComparison.Ordinal)).Select(p => p[src.Length..])];
    }

    public static (string? Text, string? Problem) ReadAtTag(string reposRoot, Release release)
    {
        var repo = RepoFolder(reposRoot, release.Product, release.Major);
        if (!Directory.Exists(repo)) return (null, $"repo not found: {repo}");
        var tag = TagFor(release.Version);
        foreach (var path in CandidatePaths(release.Product, release.Major))
        {
            var psi = new ProcessStartInfo("git", ["-C", repo, "show", $"{tag}:{path}"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            if (p.ExitCode == 0) return (stdout.Result, null);
        }
        return (null, $"tag or plan file missing ({tag})");
    }
}
