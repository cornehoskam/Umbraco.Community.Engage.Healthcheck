using System.CommandLine;
using System.Reflection;
using System.Text;
using Microsoft.Data.SqlClient;
using Umbraco.Engage.HealthCheck.Core;
using Umbraco.Engage.HealthCheck.Core.Interactive;
using Umbraco.Engage.HealthCheck.Core.Manifests;
using Umbraco.Engage.HealthCheck.Core.Reporting;

var engageDb = new Option<string?>("--engage-db") { Description = "Connection string to the database holding the Engage tables. Or set ENGAGE_HEALTHCHECK_ENGAGE_DB." };
var umbracoDb = new Option<string?>("--umbraco-db") { Description = "Connection string to the Umbraco database, only when Engage uses a separate database. Or set ENGAGE_HEALTHCHECK_UMBRACO_DB." };
var server = new Option<string?>("--server") { Description = "SQL Server to connect to, instead of --engage-db. Needs --database. Or set ENGAGE_HEALTHCHECK_SERVER." };
var database = new Option<string?>("--database") { Description = "Engage database name, with --server. Or set ENGAGE_HEALTHCHECK_DATABASE." };
var user = new Option<string?>("--user") { Description = "SQL login, with --server. Without it, Windows authentication is used. Or set ENGAGE_HEALTHCHECK_USER." };
var password = new Option<string?>("--password") { Description = "Password for --user. Prefer ENGAGE_HEALTHCHECK_PASSWORD or the masked prompt: a value on the command line ends up in your shell history. Without a password, interactive sessions ask for it." };
var trustServerCertificate = new Option<bool>("--trust-server-certificate") { Description = "Trust the server's TLS certificate without checking it, with --server. Off by default. Or set ENGAGE_HEALTHCHECK_TRUST_SERVER_CERTIFICATE=true." };
var umbracoDatabase = new Option<string?>("--umbraco-database") { Description = "Umbraco database name on the same server with the same login, with --server, when Engage uses a separate database. Or set ENGAGE_HEALTHCHECK_UMBRACO_DATABASE." };
var expect = new Option<string?>("--expect") { Description = "Engage version to check against, e.g. 17.5.0. Default: detected from the database (asked for when the session is interactive)." };
var variant = new Option<string?>("--variant") { Description = "Manifest variant: fresh, aligned or complete. Default: fresh; with --expect, the detected variant when that release has it." };
var forms = new Option<string?>("--forms") { Description = "Engage Forms version to check against. Default: detected from the database." };
var commerce = new Option<string?>("--commerce") { Description = "Engage Commerce version to check against. Default: detected from the database." };
var manifests = new Option<string?>("--manifests") { Description = "Folder with manifest files that override the built-in ones." };
var reportPath = new Option<string?>("--report") { Description = "Where to write the JSON report. Default: engage-healthcheck-<dd-MM-yyyy-HHmm>.json in the current folder." };
var noColor = new Option<bool>("--no-color") { Description = "Turn colours off. Colours are also off when output is redirected, when NO_COLOR is set or when TERM=dumb." };
var verboseOption = new Option<bool>("--verbose") { Description = "Also show the recorded migration step (a GUID). The JSON report always has it. Or set ENGAGE_HEALTHCHECK_VERBOSE=true." };
var noBanner = new Option<bool>("--no-banner") { Description = "Do not print the start banner. It is also off when stdout is redirected or CI is set." };

var check = new Command("check", "Read-only check of an Engage database against the expected schema.")
{
    engageDb, umbracoDb, server, database, user, password, trustServerCertificate, umbracoDatabase, expect, variant, forms, commerce, manifests, reportPath, noColor, noBanner, verboseOption,
};

check.SetAction(async (parse, ct) =>
{
    var noColorGiven = parse.GetValue(noColor);
    var errorStyle = OutputPolicy.StyleFor(noColorGiven, forStdErr: true);
    var outStyle = OutputPolicy.StyleFor(noColorGiven, forStdErr: false);
    void Err(string message) => errorStyle.WriteLine(Console.Error, message, ConsolePalette.Error);

    try
    {
        // Only for a real check run: --help and parse errors never get here. Stdout only.
        if (OutputPolicy.BannerFor(parse.GetValue(noBanner)))
            Banner.Write(Console.Out, outStyle, Banner.CurrentVersion(Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()));
        var interactive = !Console.IsInputRedirected;
        // Prompts go to stderr so redirected stdout stays clean.
        var prompter = new Prompter(Console.In, Console.Error, () => ConsoleMasked.Read(ct));

        // A blank option counts as not given, so the environment variable can apply. A password is only "not given" when empty.
        (string? Value, string Source) Pick(Option<string?> option, string envName, bool isPassword = false)
        {
            string? Usable(string? v) => (isPassword ? string.IsNullOrEmpty(v) : string.IsNullOrWhiteSpace(v)) ? null : v;
            var fromOption = Usable(parse.GetValue(option));
            return fromOption is not null ? (fromOption, option.Name) : (Usable(Environment.GetEnvironmentVariable(envName)), envName);
        }

        var engagePick = Pick(engageDb, "ENGAGE_HEALTHCHECK_ENGAGE_DB");
        var umbracoPick = Pick(umbracoDb, "ENGAGE_HEALTHCHECK_UMBRACO_DB");
        var serverPick = Pick(server, "ENGAGE_HEALTHCHECK_SERVER");
        var databasePick = Pick(database, "ENGAGE_HEALTHCHECK_DATABASE");
        var userPick = Pick(user, "ENGAGE_HEALTHCHECK_USER");
        var passwordPick = Pick(password, "ENGAGE_HEALTHCHECK_PASSWORD", isPassword: true);
        var umbracoDatabasePick = Pick(umbracoDatabase, "ENGAGE_HEALTHCHECK_UMBRACO_DATABASE");
        var trustFromFlag = parse.GetValue(trustServerCertificate);
        var trustFromEnv = ConnectionResolver.IsTrue(Environment.GetEnvironmentVariable("ENGAGE_HEALTHCHECK_TRUST_SERVER_CERTIFICATE"));

        var verbose = parse.GetValue(verboseOption) || ConnectionResolver.IsTrue(Environment.GetEnvironmentVariable("ENGAGE_HEALTHCHECK_VERBOSE"));

        var engage = engagePick.Value;
        var umbraco = umbracoPick.Value;
        var details = new ServerDetails(serverPick.Value, databasePick.Value, userPick.Value, passwordPick.Value, trustFromFlag || trustFromEnv, umbracoDatabasePick.Value);
        var sources = new ConnectionSources(engagePick.Source, umbracoPick.Source, serverPick.Source, databasePick.Source, userPick.Source, passwordPick.Source,
            trustFromFlag ? trustServerCertificate.Name : "ENGAGE_HEALTHCHECK_TRUST_SERVER_CERTIFICATE", umbracoDatabasePick.Source);

        var mixed = ConnectionResolver.CheckExclusive(engage, umbraco, details, sources);
        if (mixed is not null)
        {
            Err(mixed);
            return ExitCodes.Failed;
        }

        if (string.IsNullOrWhiteSpace(engage) && !ConnectionResolver.AnySeparateOption(details))
        {
            if (interactive)
            {
                var answer = prompter.AskEngageConnection();
                if (answer?.Error is not null)
                {
                    Err(answer.Error);
                    return ExitCodes.Failed;
                }
                engage = answer?.ConnectionString;
                if (answer?.Details is not null)
                {
                    // The answer can clash with an --umbraco-db given up front. It must not be dropped without a word.
                    details = answer.Details;
                    var clash = ConnectionResolver.CheckExclusive(null, umbraco, details, sources with { UmbracoDatabase = "the Umbraco database you entered" });
                    if (clash is not null)
                    {
                        Err(clash);
                        return ExitCodes.Failed;
                    }
                }
                // Already interactive, so also offer the optional separate Umbraco database.
                // With server details, that question was already part of the flow.
                if (engage is not null && string.IsNullOrWhiteSpace(umbraco))
                {
                    umbraco = prompter.AskConnectionString("Umbraco database connection string (press Enter if it is the same database): ", required: false);
                }
            }
            if (string.IsNullOrWhiteSpace(engage) && !ConnectionResolver.AnySeparateOption(details))
            {
                Err("Give --engage-db (or set ENGAGE_HEALTHCHECK_ENGAGE_DB), or give --server and --database (or set ENGAGE_HEALTHCHECK_SERVER and ENGAGE_HEALTHCHECK_DATABASE).");
                return ExitCodes.Failed;
            }
        }

        if (string.IsNullOrWhiteSpace(engage))
        {
            // Separate options: a login needs a password. Ask for it masked when someone is there to answer.
            if (details.HasUser && string.IsNullOrEmpty(details.Password) && interactive)
            {
                var typed = prompter.AskPassword("Password: ");
                if (typed is null)
                {
                    Err(Prompter.NoPassword);
                    return ExitCodes.Failed;
                }
                details = details with { Password = typed };
            }
            var built = ConnectionResolver.Build(details);
            if (!built.Ok)
            {
                Err(built.Error!);
                return ExitCodes.Failed;
            }
            engage = built.Engage!;
            umbraco = built.Umbraco;
        }
        if (string.IsNullOrWhiteSpace(umbraco)) umbraco = null;

        // Never let the SQL client's own message near the screen: it can quote parts of the string.
        if (!Prompter.IsValidConnectionString(engage))
        {
            Err("That is not a valid connection string.");
            return ExitCodes.Failed;
        }
        if (umbraco is not null && !Prompter.IsValidConnectionString(umbraco))
        {
            Err("That is not a valid Umbraco connection string.");
            return ExitCodes.Failed;
        }

        var variantName = string.IsNullOrWhiteSpace(parse.GetValue(variant)) ? null : parse.GetValue(variant)!.Trim();
        var expectVersion = parse.GetValue(expect)?.Trim();
        if (string.IsNullOrWhiteSpace(expectVersion) && interactive)
        {
            var versions = Prompter.VersionsFor(new ManifestStore(parse.GetValue(manifests)).Available(Products.Engage), variantName ?? Variants.Fresh);
            if (versions.Count > 0)
            {
                Console.Error.WriteLine(Prompter.VersionsSummary(versions));
            }
            // Pressing Enter detects the version from the database.
            expectVersion = prompter.AskVersionOrDetect("Engage version to check against");
        }
        if (string.IsNullOrWhiteSpace(expectVersion)) expectVersion = null;

        // Server and database names go to the screen only, never into the JSON report.
        Console.WriteLine($"Checking Engage database {ConnectionDescriber.DescribeWithServerWord(engage)}");
        if (umbraco is not null)
        {
            Console.WriteLine($"Reading migration state from {ConnectionDescriber.DescribeWithServerWord(umbraco)}");
        }
        var addOnVersions = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(parse.GetValue(forms))) addOnVersions[Products.Forms] = parse.GetValue(forms)!.Trim();
        if (!string.IsNullOrWhiteSpace(parse.GetValue(commerce))) addOnVersions[Products.Commerce] = parse.GetValue(commerce)!.Trim();
        var report = await HealthCheckRunner.RunAsync(new HealthCheckOptions(engage, umbraco, expectVersion, variantName, parse.GetValue(manifests), addOnVersions.Count > 0 ? addOnVersions : null), ct);

        // A failed write must not hide the findings. The exception message is not shown: the path can hold a user name.
        string? path = parse.GetValue(reportPath) ?? JsonReportWriter.DefaultFileName(DateTimeOffset.Now);
        try
        {
            await JsonReportWriter.WriteAsync(report, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            Err($"Report could not be written: {ex.GetType().Name}");
            path = null;
        }
        ConsoleReporter.Write(report, Console.Out, new ConsoleContext(
            ConnectionDescriber.Describe(engage),
            umbraco is null ? null : ConnectionDescriber.Describe(umbraco),
            path, verbose), outStyle);
        return report.ExitCode;
    }
    catch (Exception ex) when (ex is ManifestNotFoundException or NoEngageTablesException)
    {
        Err(ex.Message);
        return ExitCodes.Failed;
    }
    catch (SqlException ex)
    {
        // SQL Server's own message is shown on screen only; it never goes into the JSON report.
        Err($"Could not read the database: {ex.Message}");
        return ExitCodes.Failed;
    }
    catch (OperationCanceledException)
    {
        Err("Cancelled.");
        return ExitCodes.Failed;
    }
    catch (Exception ex)
    {
        Err($"Health check failed: {ex.GetType().Name}: {ex.Message}");
        return ExitCodes.Failed;
    }
});

var root = new RootCommand("Umbraco Engage database health check. Never writes to your database.") { check };
var parsed = root.Parse(args);
if (parsed.Errors.Count > 0)
{
    var parseErrorStyle = OutputPolicy.StyleFor(args.Contains("--no-color"), forStdErr: true);
    foreach (var error in parsed.Errors)
    {
        parseErrorStyle.WriteLine(Console.Error, error.Message, ConsolePalette.Error);
    }
    return ExitCodes.Failed;
}
return await parsed.InvokeAsync();

// Reads a line without echoing it. Shows '*' per character; Backspace deletes; Enter ends.
internal static class ConsoleMasked
{
    public static string Read(CancellationToken ct = default)
    {
        // Without this, Ctrl+C is a process signal and ReadKey never returns it.
        bool? saved = null;
        try
        {
            saved = Console.TreatControlCAsInput;
            Console.TreatControlCAsInput = true;
        }
        catch (IOException)
        {
            saved = null; // no console: keep today's behaviour
        }

        try
        {
            return ReadLoop(ct);
        }
        finally
        {
            if (saved is not null)
            {
                try { Console.TreatControlCAsInput = saved.Value; }
                catch (IOException) { }
            }
        }
    }

    private static string ReadLoop(CancellationToken ct)
    {
        var buffer = new StringBuilder();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
            {
                Console.Error.WriteLine();
                throw new OperationCanceledException();
            }
            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return buffer.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    Console.Error.Write("\b \b");
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
                Console.Error.Write('*');
            }
        }
    }
}
