using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Npgsql;
using TutorGuru.Api;
using TutorGuru.Api.Data;
using TutorGuru.Api.Identity;
using TutorGuru.Api.Storage;

var db = new Database();
if (args.Length > 0 && args[0].StartsWith("--") && !args[0].StartsWith("--urls"))
{
    if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Migration tooling requires Linux.");
    Environment.ExitCode = await MigrationCli.Run(args, db);
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.WebHost.UseUrls($"http://0.0.0.0:{Environment.GetEnvironmentVariable("PORT") ?? "8080"}");
builder.Services.AddSingleton(db);
builder.Services.AddScoped<Tutoring>();
builder.Services.AddScoped<ObjectStorage>();
builder.Services.AddScoped<MailService>();
builder.Services.AddHttpClient("sign", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("stream", c => c.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddIdentityCore<IdentityUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 10;
    options.SignIn.RequireConfirmedEmail = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddUserStore<DapperUserStore>().AddSignInManager().AddDefaultTokenProviders();
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
builder.Services.AddDataProtection().SetApplicationName("TheTutorGuru.Identity");
builder.Services.Configure<KeyManagementOptions>(o => o.XmlRepository = new DatabaseKeyRepository(db));
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));
builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
    o.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
    o.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
}).AddCookie(IdentityConstants.ApplicationScheme, o =>
{
    o.Cookie.Name = "__Host-TutorGuru.Identity";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    // Development preview can be embedded cross-site; CSRF tokens still guard
    // every mutation. Published standalone traffic keeps the stricter default.
    o.Cookie.SameSite = builder.Environment.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Lax;
    o.Cookie.Path = "/";
    o.ExpireTimeSpan = TimeSpan.FromDays(14);
    o.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return c.Response.WriteAsJsonAsync(new { error = "Please sign in." }); };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return c.Response.WriteAsJsonAsync(new { error = "Access denied." }); };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "__Host-TutorGuru.Csrf";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.Path = "/";
    o.Cookie.SameSite = builder.Environment.IsDevelopment() ? SameSiteMode.None : SameSiteMode.Strict;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", c => RateLimitPartition.GetSlidingWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new SlidingWindowRateLimiterOptions
    { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0 }));
});
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    // Only the local Replit router is trusted; public clients cannot spoof protocol/IP.
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Add(System.Net.IPAddress.Loopback);
    o.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
});
var app = builder.Build();
app.UseForwardedHeaders();
app.Use(async (c, next) =>
{
    try { await next(c); }
    catch (HttpError e) when (!c.Response.HasStarted) { c.Response.StatusCode = e.Status; await c.Response.WriteAsJsonAsync(new { error = e.Message }); }
    catch (PostgresException e) when (!c.Response.HasStarted && e.SqlState == "23505")
    { c.Response.StatusCode = 409; await c.Response.WriteAsJsonAsync(new { error = "This record already exists. Manage the existing learner, account, or subscription instead." }); }
    catch (Exception e) when (!c.Response.HasStarted && e is System.Text.Json.JsonException or FormatException)
    { c.Response.StatusCode = 400; await c.Response.WriteAsJsonAsync(new { error = "The request contains an invalid value." }); }
    catch (Exception e) when (!c.Response.HasStarted)
    {
        // No request body, tokens, credentials, or signed URLs in diagnostics.
        app.Logger.LogError("Request failed: {Type} SQL state {SqlState} at {Path}", e.GetType().Name, (e as PostgresException)?.SqlState, c.Request.Path);
        c.Response.StatusCode = 500; await c.Response.WriteAsJsonAsync(new { error = "The request could not be completed. Please try again." });
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (c, next) =>
{
    if (!HttpMethods.IsGet(c.Request.Method) && !HttpMethods.IsHead(c.Request.Method))
    {
        try { await c.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(c); }
        catch (AntiforgeryValidationException) { throw new HttpError(400, "This secure request expired. Refresh the page and try again."); }
    }
    var path = c.Request.Path.Value ?? "";
    if (path.StartsWith("/api/") && !path.StartsWith("/api/auth/") && path is not "/api/healthz" &&
        !(HttpMethods.IsGet(c.Request.Method) && path is "/api/subjects" or "/api/courses"))
        await c.RequestServices.GetRequiredService<Tutoring>().Load(c, !path.Equals("/api/profile"));
    c.Response.Headers.XContentTypeOptions = "nosniff";
    if (path != "/api/subjects" && path != "/api/courses" && path != "/api/auth/csrf") c.Response.Headers.CacheControl = "private, no-store";
    await next(c);
});
app.MapGet("/api/healthz", () => Results.Json(new { status = "ok" }));
app.MapAuth();
app.MapAccounts();
app.MapClasses();
app.MapResources();
app.Run();
