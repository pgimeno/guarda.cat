using GuardaCat.Server.Data;
using GuardaCat.Server.Features.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using GuardaCat.Server.Features.Posts;
using GuardaCat.Server.Features.Comments;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Falta ConnectionStrings:Postgres. Configúrala mediante User Secrets o una variable de entorno.");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<MarkdownRenderer>();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

builder.Services
    .AddDataProtection()
    .SetApplicationName("Guarda.cat")
    .PersistKeysToDbContext<ApplicationDbContext>();

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Password.RequiredLength = 10;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.AddAuthorization();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "__Host-GuardaCat.Session";
    options.CookieManager = new SessionCookieManager();
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized, title: "Cal iniciar sessió.").ExecuteAsync(context.HttpContext);
    options.Events.OnRedirectToAccessDenied = context => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden, title: "No tens permís per fer aquesta acció.").ExecuteAsync(context.HttpContext);
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-GuardaCat.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests,
            title: "Massa intents. Espera un minut abans de tornar-ho a provar.")
            .ExecuteAsync(context.HttpContext);
    };
    foreach (var (policy, limit) in new[] { ("register", 5), ("login", 10), ("comments", 10), ("preview", 30) })
    {
        options.AddPolicy(policy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
    app.MapOpenApi();
}
else
{
    app.UseExceptionHandler();
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseMiddleware<ApiSecurityMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapAuthEndpoints();
app.MapPostEndpoints();
app.MapCommentEndpoints();

var health = app.MapGroup("/api/health").AllowAnonymous();

health.MapGet("/live", () => Results.Ok(new { status = "live" }));

health.MapGet("/ready", async (ApplicationDbContext dbContext, CancellationToken cancellationToken) =>
    await dbContext.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "PostgreSQL no está disponible."));

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
