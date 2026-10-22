namespace Umbraco.Engage.HealthCheck.Core.Interactive;

/// <summary>Separate connection values, the alternative to a full connection string.</summary>
public sealed record ServerDetails(
    string? Server = null,
    string? Database = null,
    string? User = null,
    string? Password = null,
    bool TrustServerCertificate = false,
    string? UmbracoDatabase = null)
{
    public bool HasUser => !string.IsNullOrWhiteSpace(User);

    // The password is a secret: keep it out of ToString(), which a log line or an assertion message could print.
    public override string ToString() => $"ServerDetails {{ Server = {Server}, Database = {Database}, User = {User}, Password = ***, Trust = {TrustServerCertificate} }}";
}

/// <summary>The connection strings built from <see cref="ServerDetails"/>, or why that was not possible.</summary>
public sealed record ConnectionBuildResult(string? Engage, string? Umbraco, string? Error)
{
    public bool Ok => Error is null;

    // The connection strings can hold a password.
    public override string ToString() => $"ConnectionBuildResult {{ Ok = {Ok}, Error = {Error} }}";
}

/// <summary>What the user gave for the Engage database: a full connection string, or separate server details. Error says why they gave none.</summary>
public sealed record ConnectionAnswer(string? ConnectionString, ServerDetails? Details, string? Error = null)
{
    public override string ToString() => $"ConnectionAnswer {{ ConnectionString = {(ConnectionString is null ? "none" : "***")}, Details = {Details}, Error = {Error} }}";
}

/// <summary>Where each value came from, for messages: "--server" or the environment variable's name.</summary>
public sealed record ConnectionSources(
    string EngageDb = "--engage-db",
    string UmbracoDb = "--umbraco-db",
    string Server = "--server",
    string Database = "--database",
    string User = "--user",
    string Password = "--password",
    string Trust = "--trust-server-certificate",
    string UmbracoDatabase = "--umbraco-database");
