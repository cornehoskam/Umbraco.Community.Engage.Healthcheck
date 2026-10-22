using Microsoft.Data.SqlClient;

namespace Umbraco.Engage.HealthCheck.Core.Interactive;

/// <summary>Combines the two ways to connect. Messages never contain a password.</summary>
public static class ConnectionResolver
{
    public const string InvalidConnectionString = "That is not a valid connection string.";

    // One emptiness rule for every check. A blank value is "not given"; a password only counts as missing when it is empty,
    // because a space can be part of a password.
    private static bool Given(string? value) => !string.IsNullOrWhiteSpace(value);
    private static bool PasswordGiven(string? value) => !string.IsNullOrEmpty(value);

    public static bool AnySeparateOption(ServerDetails d)
        => Given(d.Server) || Given(d.Database) || Given(d.User) || PasswordGiven(d.Password) || Given(d.UmbracoDatabase) || d.TrustServerCertificate;

    /// <summary>Returns an error message when the two ways to connect are mixed, otherwise null. Names the environment variable when a value came from one.</summary>
    public static string? CheckExclusive(string? engageDb, string? umbracoDb, ServerDetails details, ConnectionSources? sources = null)
    {
        sources ??= new ConnectionSources();
        var given = new List<string>();
        if (Given(details.Server)) given.Add(sources.Server);
        if (Given(details.Database)) given.Add(sources.Database);
        if (Given(details.User)) given.Add(sources.User);
        if (PasswordGiven(details.Password)) given.Add(sources.Password);
        if (Given(details.UmbracoDatabase)) given.Add(sources.UmbracoDatabase);
        if (details.TrustServerCertificate) given.Add(sources.Trust);
        var alsoGiven = $" Also given: {string.Join(", ", given)}.";

        if (Given(engageDb) && given.Count > 0)
        {
            return $"Use either {sources.EngageDb} or the separate options (--server, --database, --user, --password, --umbraco-database, --trust-server-certificate), not both.{alsoGiven}";
        }
        if (Given(umbracoDb) && Given(details.UmbracoDatabase))
        {
            return $"Use either {sources.UmbracoDb} or {sources.UmbracoDatabase}, not both.";
        }
        if (Given(umbracoDb) && given.Count > 0)
        {
            // --umbraco-db is a full string: it cannot follow separate Engage values.
            return $"Use either --umbraco-db with --engage-db, or --umbraco-database with the separate options, not a mix. {sources.UmbracoDb} is set.{alsoGiven}";
        }
        if (Given(details.UmbracoDatabase) && !Given(details.Server) && !Given(details.Database))
        {
            return $"{sources.UmbracoDatabase} only works together with --server and --database.";
        }
        return null;
    }

    /// <summary>Builds the connection strings. Needs a password already when a login is given.</summary>
    public static ConnectionBuildResult Build(ServerDetails d)
    {
        if (!Given(d.Server) || !Given(d.Database))
        {
            return new(null, null, "Give both --server and --database (or ENGAGE_HEALTHCHECK_SERVER and ENGAGE_HEALTHCHECK_DATABASE).");
        }
        if (!d.HasUser && PasswordGiven(d.Password))
        {
            return new(null, null, "--password needs --user. Without --user the check uses Windows authentication.");
        }
        if (d.HasUser && !PasswordGiven(d.Password))
        {
            return new(null, null, "--user needs a password. Set ENGAGE_HEALTHCHECK_PASSWORD, or run interactively to be asked for it.");
        }

        try
        {
            var engage = ConnectionStringFor(d, d.Database!.Trim());
            var umbraco = Given(d.UmbracoDatabase) ? ConnectionStringFor(d, d.UmbracoDatabase!.Trim()) : null;
            return new(engage, umbraco, null);
        }
        catch (ArgumentException)
        {
            // The builder's own message can quote the value: say nothing about it.
            return new(null, null, InvalidConnectionString);
        }
    }

    private static string ConnectionStringFor(ServerDetails d, string database)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = d.Server!.Trim(),
            InitialCatalog = database,
            TrustServerCertificate = d.TrustServerCertificate,
        };
        if (d.HasUser)
        {
            builder.UserID = d.User!.Trim();
            builder.Password = d.Password!; // never trimmed
        }
        else
        {
            builder.IntegratedSecurity = true;
        }
        return builder.ConnectionString;
    }

    /// <summary>Reads "true", "1", "yes" or "y" (any case), the way an environment variable is usually set.</summary>
    public static bool IsTrue(string? value)
        => value is not null && value.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "y";
}
