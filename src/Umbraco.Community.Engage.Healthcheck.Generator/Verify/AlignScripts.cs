using System.Text.RegularExpressions;

namespace Umbraco.Community.Engage.Healthcheck.Generator.Verify;

/// <summary>
/// Finds the manual alignment scripts in a local folder, as downloaded from the Engage schema alignment guide on
/// docs.umbraco.com, and gets them ready to run.
/// </summary>
public static partial class AlignScripts
{
    public const string CompleteAlignSchema = "CompleteAlignSchema.sql";
    public const string EnsureDataConsistency = "EnsureDataConsistency.sql";
    public const string FolderVariable = "ENGAGE_ALIGN_SCRIPTS";

    [GeneratedRegex(@"^\s*GO\s*(--.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GoLine();

    [GeneratedRegex(@"INSERT\s+INTO\s+##EngageAlignConfig\s+VALUES\s*\(\s*0\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex SingleDatabaseDefault();

    /// <summary>
    /// The script for a major: <c>&lt;folder&gt;/&lt;major&gt;/&lt;file&gt;</c> (for example <c>V17/CompleteAlignSchema.sql</c>)
    /// when it exists, else <c>&lt;folder&gt;/&lt;file&gt;</c>. Null when neither exists.
    /// </summary>
    public static string? Find(string folder, string major, string file)
        => new[] { Path.Combine(folder, major, file), Path.Combine(folder, file) }.FirstOrDefault(File.Exists);

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
    /// Null when the script runs in single-database mode as published. A script with a <c>##EngageAlignConfig</c> flag must
    /// have it at 0 (Umbraco and Engage share a database); a script without the flag only knows that mode.
    /// </summary>
    public static string? SingleDatabaseProblem(string sql)
        => !sql.Contains("##EngageAlignConfig", StringComparison.OrdinalIgnoreCase) || SingleDatabaseDefault().IsMatch(sql)
            ? null
            : "the script's ##EngageAlignConfig flag is not 0 (single-database mode)";
}
