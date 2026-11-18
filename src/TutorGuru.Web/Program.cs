using MudBlazor.Services;
using TutorGuru.Web.Components;
using TutorGuru.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.WebHost.UseUrls(builder.Configuration["ASPNETCORE_URLS"] ?? "https://localhost:7243");
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddScoped<GuruApi>();
// Keep API requests and secure authentication cookies on the browser's origin.
// The standalone .NET solution does not need Replit's router or a Node server.
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
var app = builder.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapReverseProxy();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
