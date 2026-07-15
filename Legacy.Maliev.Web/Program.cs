using Legacy.Maliev.Web.Infrastructure;
using Maliev.Aspire.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.AddStandardCors();
builder.AddStandardMiddleware(options => options.EnableRequestLogging = true);
builder.AddStandardOpenApi(
    title: "Legacy MALIEV Web BFF",
    description: "Server-rendered compatibility frontend and BFF for the independently deployable legacy services.");
builder.Services.AddRazorPages();
builder.Services.AddResponseCompression();
builder.Services.AddOutputCache();
builder.Services.AddLegacyServiceClients(builder.Configuration);

var app = builder.Build();
app.UseStandardMiddleware();
app.UseResponseCompression();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseOutputCache();
app.MapDefaultEndpoints("web");
app.MapRazorPages();
app.MapApiDocumentation(servicePrefix: "web");
await app.RunAsync();

public partial class Program;
