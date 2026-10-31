using System.ComponentModel.DataAnnotations;

namespace GuardaCat.Server.Features.Posts;

internal static class PostValidation
{
    public static IResult? Validate(object request)
    {
        var errors = new List<ValidationResult>();
        if (Validator.TryValidateObject(request, new ValidationContext(request), errors, true)) return null;
        return Results.ValidationProblem(errors
            .SelectMany(error => error.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => (Member: member, Error: error.ErrorMessage ?? "Dades no vàlides.")))
            .GroupBy(error => error.Member)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Error).ToArray()));
    }

    public static bool ValidPage(int page, int pageSize) =>
        page >= 1 && pageSize is >= 1 and <= 50 && ((long)page - 1) * pageSize <= int.MaxValue;

    public static IResult InvalidPage() => Results.Problem(statusCode: 400,
        title: "La pàgina ha de ser positiva i la mida ha d'estar entre 1 i 50, sense superar el límit de paginació.");

    public static IResult ImagesNotSupported() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["BodyMarkdown"] = ["Les imatges encara no estan disponibles. Retira-les del Markdown."]
    });

    public static IResult NotFound() => Results.Problem(statusCode: 404, title: "La publicació no existeix.");
    public static IResult Forbidden() => Results.Problem(statusCode: 403, title: "Només l'autor pot modificar aquesta publicació.");
}
