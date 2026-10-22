var builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddDeliveryApi()
    .AddComposers()
    .Build();

var app = builder.Build();
await app.BootUmbracoAsync();

// Umbraco 13 still serves the installer through its own endpoints; it must be mapped before the backoffice.
app.UseUmbraco()
    .WithMiddleware(u => { u.UseBackOffice(); u.UseWebsite(); })
    .WithEndpoints(u => { u.UseInstallerEndpoints(); u.UseBackOfficeEndpoints(); u.UseWebsiteEndpoints(); });

await app.RunAsync();
