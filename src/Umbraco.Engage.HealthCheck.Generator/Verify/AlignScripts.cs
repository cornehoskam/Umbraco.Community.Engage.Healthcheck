using System.Diagnostics;
using System.Text.RegularExpressions;
using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Generator.Catalog;

namespace Umbraco.Engage.HealthCheck.Generator.Verify;

/// <summary>Reads the manual alignment scripts from the Engage source at a release tag (read-only) and gets them ready to run.</summary>
public static partial class AlignScripts
{
    public const string CompleteAlignSchema = "CompleteAlignSchema.sql";
    public const string EnsureDataConsistency = "EnsureDataConsistency.sql";

    /// <summary>The scripts live in <c>scripts/</c> of each Engage source repo.</summary>
    public static string PathOf(string file) => $"scripts/{file}";

    [GeneratedRegex(@"^\s*GO\s*(--.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();

    [GeneratedRegex(@"INSERT\s+INTO\s+##EngageAlignConfig\s+VALUES\s*\(\s*0\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex SingleDatabaseDefault();

    /// <summary>Splits a script on lines that hold only <c>GO</c> (any case, optional trailing comment), like SSMS and sqlcmd do. Empty batches are dropped.</summary>
    public static IReadOnlyList<string> SplitBatches(string sql)
    {
        var batches = new List<string>();
        var current = new List<string>();
        foreach (var raw in sql.TrimStart('﻿').Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (GoLine().IsMatch(line))
            {
                Flush();
                continue;
            }
            current.Add(line);
        }
        Flush();
        return batches;

        void Flush()
        {
            var batch = string.Join("\n", current).Trim();
            if (batch.Length > 0) batches.Add(batch);
            current.Clear();
        }
    }

    /// <summary>
    /// Null when the script runs in single-database mode as published. Newer scripts (13.8.1, 16.3.3, 17.3+, V18) have a
    /// <c>##EngageAlignConfig</c> flag where 0 means Umbraco and Engage share a database; older ones only know that mode.
    /// </summary>
    public static string? SingleDatabaseProblem(string sql)
        => !sql.Contains("##EngageAlignConfig", StringComparison.OrdinalIgnoreCase) || SingleDatabaseDefault().IsMatch(sql)
            ? null
            : "the script's ##EngageAlignConfig flag is not 0 (single-database mode)";

    /// <summary>
    /// The tag whose CompleteAlignSchema.sql belongs to <paramref name="release"/>: its own tag, or, when that tag has none
    /// (defensive: every catalog release with an alignFrom has its own script), the next release of the same major that has one, with a note.
    /// </summary>
    public static (string? Tag, string? Note) ScriptTagFor(Release release, ReleaseCatalog catalog, Func<string, bool> tagHasScript)
    {
        var own = MigrationPlanReader.TagFor(release.Version);
        if (tagHasScript(own)) return (own, null);
        var later = catalog.Select(release.Product, release.Major).Where(r => r.Parsed > release.Parsed).Select(r => MigrationPlanReader.TagFor(r.Version));
        var tag = later.FirstOrDefault(tagHasScript);
        return tag is null
            ? (null, $"no {CompleteAlignSchema} at {own} or any later {release.Major} tag")
            : (tag, $"no script published at {own}; used the first published one, from {tag}");
    }

    /// <summary>The file at the tag in the release's version repo, or null when the repo, tag or file is missing. Never writes to the repo.</summary>
    public static string? ReadAtTag(string reposRoot, string major, string tag, string file)
    {
        var repo = MigrationPlanReader.RepoFolder(reposRoot, Products.Engage, major);
        if (!Directory.Exists(repo)) return null;
        var psi = new ProcessStartInfo("git", ["-C", repo, "show", $"{tag}:{PathOf(file)}"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return p.ExitCode == 0 ? stdout.Result : null;
    }
}
