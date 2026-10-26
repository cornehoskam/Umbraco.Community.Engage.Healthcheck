namespace Umbraco.Community.Engage.Healthcheck.Core.Reporting;

/// <summary>
/// Writes text with an optional colour. Colour is additive only: every label stays in the text, so a style that
/// ignores the colour loses nothing.
/// </summary>
public interface IConsoleStyle
{
    void Write(TextWriter output, string text, ConsoleColor? color = null);
}

public static class ConsoleStyleExtensions
{
    public static void WriteLine(this IConsoleStyle style, TextWriter output, string text = "", ConsoleColor? color = null)
    {
        style.Write(output, text, color);
        output.WriteLine();
    }
}

/// <summary>No colour. Used when output is redirected, NO_COLOR or --no-color is set, or in tests.</summary>
public sealed class PlainConsoleStyle : IConsoleStyle
{
    public static readonly PlainConsoleStyle Instance = new();

    public void Write(TextWriter output, string text, ConsoleColor? color = null) => output.Write(text);
}

/// <summary>Colours through Console.ForegroundColor and always resets afterwards. The line break is written uncoloured.</summary>
public sealed class ConsoleColorStyle : IConsoleStyle
{
    public void Write(TextWriter output, string text, ConsoleColor? color = null)
    {
        if (color is null || text.Length == 0)
        {
            output.Write(text);
            return;
        }
        Console.ForegroundColor = color.Value;
        try { output.Write(text); }
        finally { Console.ResetColor(); }
    }
}

/// <summary>The colours of the scheme, in one place.</summary>
public static class ConsolePalette
{
    public const ConsoleColor Critical = ConsoleColor.Red;
    public const ConsoleColor Warning = ConsoleColor.Yellow;
    public const ConsoleColor Info = ConsoleColor.DarkGray;
    public const ConsoleColor Detected = ConsoleColor.Cyan;
    public const ConsoleColor Attention = ConsoleColor.Yellow;
    public const ConsoleColor Blocked = ConsoleColor.Red;
    public const ConsoleColor Healthy = ConsoleColor.Green;
    public const ConsoleColor Banner = ConsoleColor.Cyan;
    public const ConsoleColor Error = ConsoleColor.Red;
}

/// <summary>The pure rules for when colour and the banner are on. They take the environment and flags as arguments.</summary>
public static class OutputPolicy
{
    /// <summary>Colour is off for a redirected stream, a non-empty NO_COLOR (no-color.org), TERM=dumb or --no-color.</summary>
    public static bool ColorEnabled(string? noColorEnv, string? termEnv, bool noColorFlag, bool redirected)
        => !redirected
           && !noColorFlag
           && string.IsNullOrEmpty(noColorEnv)
           && !string.Equals(termEnv, "dumb", StringComparison.OrdinalIgnoreCase);

    /// <summary>The banner is off for redirected stdout, --no-banner or CI. CI counts as set when it is not empty, "false" or "0".</summary>
    public static bool BannerEnabled(string? ciEnv, bool noBannerFlag, bool stdoutRedirected)
        => !stdoutRedirected && !noBannerFlag && !IsCi(ciEnv);

    public static bool IsCi(string? ciEnv)
        => !string.IsNullOrWhiteSpace(ciEnv)
           && !string.Equals(ciEnv.Trim(), "false", StringComparison.OrdinalIgnoreCase)
           && ciEnv.Trim() != "0";

    /// <summary>The style for stdout (forStdErr false) or stderr, from the real environment.</summary>
    public static IConsoleStyle StyleFor(bool noColorFlag, bool forStdErr)
        => ColorEnabled(Environment.GetEnvironmentVariable("NO_COLOR"), Environment.GetEnvironmentVariable("TERM"), noColorFlag,
            forStdErr ? Console.IsErrorRedirected : Console.IsOutputRedirected)
            ? new ConsoleColorStyle()
            : PlainConsoleStyle.Instance;

    public static bool BannerFor(bool noBannerFlag)
        => BannerEnabled(Environment.GetEnvironmentVariable("CI"), noBannerFlag, Console.IsOutputRedirected);
}
