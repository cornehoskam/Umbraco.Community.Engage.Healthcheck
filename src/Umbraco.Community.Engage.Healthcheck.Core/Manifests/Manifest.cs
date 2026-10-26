using Umbraco.Community.Engage.Healthcheck.Core.Schema;

namespace Umbraco.Community.Engage.Healthcheck.Core.Manifests;

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
