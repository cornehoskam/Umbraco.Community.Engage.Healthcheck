using Microsoft.Data.SqlClient;

namespace Umbraco.Engage.HealthCheck.Core.Interactive;

public sealed class Prompter(TextReader input, TextWriter output, Func<string>? readMasked = null)
{
    public const int MaxTries = 3;

    private readonly Func<string> _readMasked = readMasked ?? (() => input.ReadLine() ?? "");

    /// <summary>Asks for a masked value. Returns the trimmed text, or null when empty (optional) or after too many tries (required).</summary>
    public string? AskMasked(string question, bool required = true, Func<string, bool>? isValid = null, string? invalidMessage = null, bool trim = true)
    {
        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            output.Write(question);
            var value = trim ? _readMasked().Trim() : _readMasked();
            if (value.Length == 0)
            {
                if (!required) return null;
                continue;
            }
            if (isValid is null || isValid(value)) return value;
            if (invalidMessage is not null) output.WriteLine(invalidMessage);
        }
        return null;
    }

    public string? AskConnectionString(string question, bool required)
        => AskMasked(question, required, IsValidConnectionString, "That is not a valid connection string.");

    public const string NoPassword = "No password given.";

    /// <summary>Asks for a password. Masked, and never trimmed: spaces can be part of a password. Null when empty after too many tries.</summary>
    public string? AskPassword(string question) => AskMasked(question, required: true, trim: false);

    public const string ConnectionStringOrServerDetailsQuestion = "Connection string, or press Enter to enter server details: ";

    /// <summary>
    /// Asks for the Engage connection as a masked connection string. Pressing Enter switches to separate server details.
    /// Returns null when the user gave up on connection strings. Returns an answer with an Error when server details were incomplete.
    /// </summary>
    public ConnectionAnswer? AskEngageConnection()
    {
        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            output.Write(ConnectionStringOrServerDetailsQuestion);
            var value = _readMasked().Trim();
            if (value.Length == 0) return AskServerDetails();
            if (IsValidConnectionString(value)) return new ConnectionAnswer(value, null);
            output.WriteLine(ConnectionResolver.InvalidConnectionString);
        }
        return null;
    }

    /// <summary>Asks for server, database, login, password (only with a login), trust and the optional Umbraco database.</summary>
    public ConnectionAnswer AskServerDetails()
    {
        var server = AskRequired("Server: ");
        if (server is null) return new ConnectionAnswer(null, null, "No server given.");
        var database = AskRequired("Database: ");
        if (database is null) return new ConnectionAnswer(null, null, "No database given.");

        var user = Ask("Login (press Enter for Windows authentication): ", null);
        string? password = null;
        if (user is not null)
        {
            password = AskPassword("Password: ");
            if (password is null) return new ConnectionAnswer(null, null, NoPassword);
        }

        var trust = Ask("Trust server certificate (y/N): ", null);
        var umbracoDatabase = Ask("Umbraco database (press Enter if it is the same database): ", null);
        return new ConnectionAnswer(null, new ServerDetails(server, database, user, password, ConnectionResolver.IsTrue(trust), umbracoDatabase));
    }

    /// <summary>Asks a visible question until it gets an answer, up to <see cref="MaxTries"/> times.</summary>
    private string? AskRequired(string question)
    {
        for (var attempt = 0; attempt < MaxTries; attempt++)
        {
            var value = Ask(question, null);
            if (value is not null) return value;
        }
        return null;
    }

    /// <summary>Asks a visible question. Empty input gives the default.</summary>
    public string? Ask(string question, string? defaultValue)
    {
        output.Write(question);
        var value = input.ReadLine()?.Trim();
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }

    public const string DetectAutomatically = "detect automatically";

    /// <summary>Asks for a version with "detect automatically" as the default. Returns null to detect, otherwise the typed version.</summary>
    public string? AskVersionOrDetect(string question)
    {
        var value = Ask($"{question} [{DetectAutomatically}]: ", null);
        return value is null || value.Equals("detect", StringComparison.OrdinalIgnoreCase) || value.Equals(DetectAutomatically, StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    /// <summary>Versions of a variant from manifest names like "17.5.0.fresh", highest first.</summary>
    public static IReadOnlyList<string> VersionsFor(IEnumerable<string> available, string variant)
    {
        var suffix = "." + variant;
        return available
            .Where(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(name => name[..^suffix.Length])
            .Select(text => (Text: text, Parsed: Version.TryParse(text, out var v) ? v : null))
            .Where(x => x.Parsed is not null)
            .DistinctBy(x => x.Text)
            .OrderByDescending(x => x.Parsed)
            .Select(x => x.Text)
            .ToList();
    }

    /// <summary>One line for the version prompt: count and range, from the list that <see cref="VersionsFor"/> returns (highest first).</summary>
    public static string VersionsSummary(IReadOnlyList<string> versions)
        => $"Known Engage versions: {versions.Count} ({versions[^1]} to {versions[0]}). Press Enter to detect automatically, or type a version.";

    public static bool IsValidConnectionString(string value)
    {
        try
        {
            _ = new SqlConnectionStringBuilder(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

