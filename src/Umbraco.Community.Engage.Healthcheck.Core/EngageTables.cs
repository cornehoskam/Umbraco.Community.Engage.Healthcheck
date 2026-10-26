namespace Umbraco.Community.Engage.Healthcheck.Core;

public static class EngageTables
{
    public const string Prefix = "umbracoEngage";
    public const string ReportingPrefix = "umbracoEngageReporting";

    public static bool IsReporting(string tableName) => tableName.StartsWith(ReportingPrefix, StringComparison.OrdinalIgnoreCase);
}
