using Umbraco.Engage.HealthCheck.Core.Schema;

namespace Umbraco.Engage.HealthCheck.Core.Manifests;

public sealed record Manifest(
    int FormatVersion,
    string Product,
    string Version,
    string Variant,
    string UmbracoVersion,
    string StateKey,
    string? LastMigrationStep,
    string? SchemaStatus,
    DatabaseSchema Schema,
    string? HostEngageVersion = null,
    string? InstalledFrom = null)
{
    public const int CurrentFormatVersion = 1;
}
