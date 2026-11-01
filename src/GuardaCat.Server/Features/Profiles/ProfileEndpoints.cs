using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GuardaCat.Contracts.Profiles;
using GuardaCat.Server.Data;
using Microsoft.EntityFrameworkCore;
using GuardaCat.Server.Features.Media;

namespace GuardaCat.Server.Features.Profiles;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/public/users/{id:guid}", GetAsync).AllowAnonymous()
            .Produces<ProfileResponse>().ProducesProblem(404)
            .WithDescription("Perfil público: identificador, nombre y bio en texto plano. Sin email ni datos de Identity.");

        var own = endpoints.MapGroup("/api/me/profile").RequireAuthorization().ProducesProblem(401);
        own.MapGet("", (ClaimsPrincipal user, ApplicationDbContext db, CancellationToken ct) =>
            GetAsync(UserId(user), db, ct)).Produces<ProfileResponse>().ProducesProblem(404)
            .WithDescription("Perfil propio actual, sin caché, para edición.");
        own.MapPut("", UpdateAsync).Produces<ProfileResponse>().ProducesProblem(400).ProducesProblem(404)
            .WithDescription("Modifica exclusivamente el nombre y la bio de la sesión. Nombre: 2–60 caracteres; bio opcional: hasta 500. Requiere X-CSRF-TOKEN.");
    }

    private static async Task<IResult> GetAsync(Guid id, ApplicationDbContext db, CancellationToken ct)
    {
        var profile = await db.Users.AsNoTracking().Where(user => user.Id == id)
            .Select(user => new ProfileResponse(user.Id, user.DisplayName, user.Bio))
            .SingleOrDefaultAsync(ct);
        if (profile is null) return NotFound();
        var avatar = await db.MediaAssets.AsNoTracking().SingleOrDefaultAsync(asset => asset.OwnerId == id && asset.PostId == null, ct);
        return Results.Ok(profile with { Avatar = avatar is null ? null : MediaService.Response(avatar) });
    }

    private static async Task<IResult> UpdateAsync(UpdateProfileRequest request, ClaimsPrincipal user,
        ApplicationDbContext db, CancellationToken ct)
    {
        request.DisplayName = request.DisplayName?.Trim() ?? string.Empty;
        request.Bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
        {
            return Results.ValidationProblem(errors
                .SelectMany(error => error.MemberNames.Select(member =>
                    (Member: member, Error: error.ErrorMessage ?? "Dades no vàlides.")))
                .GroupBy(error => error.Member)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Error).ToArray()));
        }

        var id = UserId(user);
        // Update only public fields, without overwriting concurrent Identity/security changes.
        var updated = await db.Users.Where(value => value.Id == id).ExecuteUpdateAsync(setters => setters
            .SetProperty(value => value.DisplayName, request.DisplayName)
            .SetProperty(value => value.Bio, request.Bio), ct);
        return updated == 0 ? NotFound() : await GetAsync(id, db, ct);
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static IResult NotFound() => Results.Problem(statusCode: 404, title: "El perfil no existeix.");
}
