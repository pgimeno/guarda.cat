using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GuardaCat.Contracts.Profiles;
using GuardaCat.Server.Data;
using Microsoft.EntityFrameworkCore;
using GuardaCat.Server.Features.Media;

namespace GuardaCat.Server.Features.Profiles;

public static class ProfileEndpoints
{
    private static readonly TimeZoneInfo CommunityTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    public static void MapProfileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/public/users/active-today", GetActiveTodayAsync).AllowAnonymous()
            .Produces<List<ActiveUserResponse>>()
            .WithDescription("Fins a cinc usuaris amb més publicacions i comentaris durant el dia actual a Europe/Madrid.");

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

    private static async Task<IResult> GetActiveTodayAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, CommunityTimeZone));
        var startLocal = localToday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endLocal = localToday.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, CommunityTimeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, CommunityTimeZone);

        var activity = db.Posts.AsNoTracking()
            .Where(post => post.CreatedAtUtc >= startUtc && post.CreatedAtUtc < endUtc)
            .Select(post => new { UserId = post.AuthorId, OccurredAtUtc = post.CreatedAtUtc })
            .Concat(db.Comments.AsNoTracking()
                .Where(comment => comment.CreatedAtUtc >= startUtc && comment.CreatedAtUtc < endUtc)
                .Select(comment => new { UserId = comment.AuthorId, OccurredAtUtc = comment.CreatedAtUtc }));

        var rankedUsers = await activity
            .GroupBy(item => item.UserId)
            .Select(group => new
            {
                Id = group.Key,
                ActivityCount = group.Count(),
                LastActivityUtc = group.Max(item => item.OccurredAtUtc)
            })
            .Join(db.Users.AsNoTracking(), item => item.Id, user => user.Id,
                (item, user) => new { user.Id, user.DisplayName, item.ActivityCount, item.LastActivityUtc })
            .OrderByDescending(user => user.ActivityCount)
            .ThenByDescending(user => user.LastActivityUtc)
            .ThenBy(user => user.Id)
            .Take(5)
            .ToListAsync(ct);

        var ids = rankedUsers.Select(user => user.Id).ToList();
        var avatars = await db.MediaAssets.AsNoTracking()
            .Where(asset => ids.Contains(asset.OwnerId) && asset.PostId == null)
            .ToDictionaryAsync(asset => asset.OwnerId, ct);

        return Results.Ok(rankedUsers.Select(user => new ActiveUserResponse(
            user.Id,
            user.DisplayName,
            avatars.TryGetValue(user.Id, out var avatar) ? MediaService.Response(avatar) : null)).ToList());
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
