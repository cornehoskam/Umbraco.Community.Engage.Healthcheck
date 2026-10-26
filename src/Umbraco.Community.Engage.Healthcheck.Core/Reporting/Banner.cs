using System.Reflection;

namespace Umbraco.Community.Engage.Healthcheck.Core.Reporting;

/// <summary>The start banner. Plain ASCII, at most 6 lines and 78 columns, so it renders the same everywhere.</summary>
public static class Banner
{
    public const int MaxLines = 6;
    public const int MaxWidth = 78;

    // FIGlet "Small" font. Backslashes are part of the art, hence the raw string literals.
    private static readonly string[] Engage =
    [
        """ ___                         """,
        """| __|_ _  __ _ __ _ __ _ ___ """,
        """| _|| ' \/ _` / _` / _` / -_)""",
        """|___|_||_\__, \__,_\__, \___|""",
        """         |___/     |___/     """,
    ];

    private static readonly string[] Healthcheck =
    [
        """ _  _          _ _   _       _           _   """,
        """| || |___ __ _| | |_| |_  __| |_  ___ __| |__""",
        """| __ / -_) _` | |  _| ' \/ _| ' \/ -_) _| / /""",
        """|_||_\___\__,_|_|\__|_||_\__|_||_\___\__|_\_\""",
        """                                             """,
    ];

    /// <summary>The five art rows plus the version line. Trailing spaces are trimmed.</summary>
    public static IReadOnlyList<string> Lines(string version)
    {
        var left = Engage.Max(l => l.Length);
        var right = Healthcheck.Max(l => l.Length);
        var rows = Engage.Zip(Healthcheck, (a, b) => (a.PadRight(left) + " " + b.PadRight(right)).TrimEnd()).ToList();
        rows.Add($"v{version}");
        return rows;
    }

    public static void Write(TextWriter output, IConsoleStyle style, string version)
    {
        foreach (var line in Lines(version)) style.WriteLine(output, line, ConsolePalette.Banner);
        output.WriteLine();
    }

    /// <summary>The informational version without the "+commit" suffix.</summary>
    public static string VersionOf(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "0.0.0";
        var plus = informationalVersion.IndexOf('+');
        var version = (plus < 0 ? informationalVersion : informationalVersion[..plus]).Trim();
        return version.Length == 0 ? "0.0.0" : version;
    }

    public static string CurrentVersion(Assembly assembly)
        => VersionOf(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
