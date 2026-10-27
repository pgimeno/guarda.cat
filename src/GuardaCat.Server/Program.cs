using GuardaCat.Server.Data;
using GuardaCat.Server.Features.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "Falta ConnectionStrings:Postgres. Configúrala mediante User Secrets o una variable de entorno.");

builder.Services.AddProblemDetails();
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
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
app.UseAuthorization();

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
