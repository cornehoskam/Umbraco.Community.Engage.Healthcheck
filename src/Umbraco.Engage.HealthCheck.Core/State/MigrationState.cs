namespace Umbraco.Engage.HealthCheck.Core.State;

public sealed record MigrationState(bool KeyValueTableFound, string? LastStep, string? SchemaStatus)
{
    public const string StateKey = "Umbraco.Core.Upgrader.State+Umbraco.Engage";
    public const string SchemaStatusKey = "Umbraco.Engage+DatabaseSchemaStatus";
}
