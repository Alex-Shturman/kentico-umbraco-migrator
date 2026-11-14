
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

#if DEBUG
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
#endif

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .AddAzureBlobMediaFileSystem()
    .AddAzureBlobImageSharpCache()
    .Build();

WebApplication app = builder.Build();


await app.BootUmbracoAsync();


// Old Kentico URLs end in .aspx. Umbraco never offers a path that ends in a file extension to the
// content finders, so a registered redirect for /old.aspx answers 404 (research R10). Appending a slash
// makes the redirect finder see it; the trailing slash is ignored when it looks up. Only page requests
// (GET, HEAD) outside the back office are touched.
app.Use((context, next) =>
{
    var request = context.Request;
    var path = request.Path.Value;
    if ((HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        && path is not null
        && path.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase)
        && !request.Path.StartsWithSegments("/umbraco", StringComparison.OrdinalIgnoreCase))
    {
        // The path ends in ".aspx", so it cannot already end in a slash and no double slash is created.
        request.Path = path + "/";
    }

    return next(context);
});

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
