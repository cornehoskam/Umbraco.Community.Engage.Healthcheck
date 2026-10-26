namespace Umbraco.Community.Engage.Healthcheck.Core.Reporting;

/// <summary>Screen-only context for the summary block. Kept out of Report so it never reaches the JSON.</summary>
public sealed record ConsoleContext(string EngageTarget, string? UmbracoTarget, string? ReportPath, bool Verbose = false);
