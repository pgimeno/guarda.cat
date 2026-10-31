using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GuardaCat.Contracts.Auth;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GuardaCat.Server.Features.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth");

        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new CsrfResponse(antiforgery.GetAndStoreTokens(context).RequestToken!)))
            .AllowAnonymous();

        auth.MapPost("/register", RegisterAsync).AllowAnonymous().RequireRateLimiting("register");
        auth.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting("login");
        auth.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        endpoints.MapGet("/api/me", async (ClaimsPrincipal principal, UserManager<ApplicationUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            return user is null
                ? Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Cal iniciar sessió.")
                : Results.Ok(new CurrentUserResponse(user.Id, user.DisplayName, user.Email!));
        }).RequireAuthorization();
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, UserManager<ApplicationUser> users)
    {
        request.Email = request.Email?.Trim() ?? string.Empty;
        request.DisplayName = request.DisplayName?.Trim() ?? string.Empty;
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName
        };

        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, request.Password);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "EmailIndex" or "UserNameIndex" })
        {
            // The database also protects against simultaneous registrations for the same email.
            return RegistrationConflict();
        }

        if (result.Succeeded)
        {
            // Registration deliberately does not issue a session cookie.
            return Results.StatusCode(StatusCodes.Status201Created);
        }

        if (result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName"))
        {
            return RegistrationConflict();
        }

        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [nameof(RegisterRequest.Password)] =
                ["No s'ha pogut crear el compte. Revisa el correu i utilitza una contrasenya de 10 a 128 caràcters amb majúscula, minúscula, número i símbol."]
        });
    }

    private static async Task<IResult> LoginAsync(LoginRequest request,
        UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signInManager)
    {
        request.Email = request.Email?.Trim() ?? string.Empty;
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var user = await users.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var result = await signInManager.PasswordSignInAsync(
                user, request.Password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                return Results.NoContent();
            }
        }

        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
            title: "No s'ha pogut iniciar sessió. Revisa les credencials o torna-ho a provar més tard.");
    }

    private static IResult RegistrationConflict() => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "No s'ha pogut crear el compte amb aquestes dades.");

    private static Dictionary<string, string[]> Validate(object request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results.SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => (Member: member, Error: result.ErrorMessage ?? "Dades no vàlides.")))
            .GroupBy(result => result.Member)
            .ToDictionary(group => group.Key, group => group.Select(result => result.Error).ToArray());
    }
}
