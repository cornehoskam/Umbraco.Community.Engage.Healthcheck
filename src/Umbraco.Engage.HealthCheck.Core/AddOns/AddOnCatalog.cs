namespace Umbraco.Engage.HealthCheck.Core.AddOns;

public sealed record AddOn(string Product, string DisplayName, string StateKey, IReadOnlyList<string> Tables, string CliOption);

public static class AddOnCatalog
{
    public static readonly AddOn Forms = new(Products.Forms, "Engage Forms", "Umbraco.Core.Upgrader.State+Umbraco.Engage.Forms",
        ["umbracoEngageAnalyticsUmbracoFormsSubmissionRecord"], "--forms");
    public static readonly AddOn Commerce = new(Products.Commerce, "Engage Commerce", "Umbraco.Core.Upgrader.State+Umbraco.Engage.UmbracoCommerce",
        ["umbracoEngageAnalyticsUmbracoCommerceVisitorOrder"], "--commerce");
    public static readonly IReadOnlyList<AddOn> All = [Forms, Commerce];

    public static bool OwnsTable(string table) => All.Any(a => a.Tables.Contains(table, StringComparer.OrdinalIgnoreCase));
}
