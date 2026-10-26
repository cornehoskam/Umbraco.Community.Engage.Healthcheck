namespace Umbraco.Community.Engage.Healthcheck.Core;

public static class Products
{
    public const string Engage = "Engage";
    public const string Forms = "Engage.Forms";
    public const string Commerce = "Engage.Commerce";

    public static string Folder(string product) => product switch
    {
        Engage => "engage",
        Forms => "forms",
        Commerce => "commerce",
        _ => throw new ArgumentOutOfRangeException(nameof(product), product, "Unknown product."),
    };
}
