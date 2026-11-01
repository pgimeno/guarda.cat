using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;

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
        ["BodyMarkdown"] = ["El Markdown només pot incloure imatges pujades a aquesta publicació. Retira les imatges externes, alienes o eliminades."]
    });

    public static IResult NotFound() => Results.Problem(statusCode: 404, title: "La publicació no existeix.");
    public static IResult Forbidden() => Results.Problem(statusCode: 403, title: "Només l'autor pot modificar aquesta publicació.");

    public static IResult? NormalizeTags(IEnumerable<string>? values, out List<string> tags)
    {
        tags = [];
        if (values is null) return InvalidTags("Les etiquetes no poden ser nul·les.");

        foreach (var value in values)
        {
            if (!TryNormalizeTag(value, out var tag))
                return InvalidTags("Cada etiqueta ha de tenir entre 2 i 30 caràcters i només pot contenir lletres, números o guions.");
            if (!tags.Contains(tag, StringComparer.Ordinal)) tags.Add(tag);
            if (tags.Count > 5) return InvalidTags("Pots indicar un màxim de 5 etiquetes.");
        }

        tags.Sort(StringComparer.Ordinal);
        return null;
    }

    public static IResult? NormalizeTagFilter(string? value, out string? tag)
    {
        tag = null;
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!TryNormalizeTag(value, out var normalized)) return InvalidTags(
            "El filtre ha de tenir entre 2 i 30 caràcters i només pot contenir lletres, números o guions.");
        tag = normalized;
        return null;
    }

    private static bool TryNormalizeTag(string? value, out string tag)
    {
        tag = string.Empty;
        if (value is null) return false;
        var source = value.Normalize(NormalizationForm.FormKC).Trim().TrimStart('#');
        var result = new StringBuilder(source.Length);
        var pendingSeparator = false;
        foreach (var character in source)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && result.Length > 0) result.Append('-');
                result.Append(char.ToLower(character, CultureInfo.InvariantCulture));
                pendingSeparator = false;
            }
            else if (char.IsWhiteSpace(character) || character == '-') pendingSeparator = result.Length > 0;
            else return false;
        }

        tag = result.ToString();
        return tag.Length is >= 2 and <= 30;
    }

    private static IResult InvalidTags(string message) => Results.ValidationProblem(
        new Dictionary<string, string[]> { ["Tags"] = [message] });
}
