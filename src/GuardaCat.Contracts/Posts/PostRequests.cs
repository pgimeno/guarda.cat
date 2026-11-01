using System.ComponentModel.DataAnnotations;

namespace GuardaCat.Contracts.Posts;

public sealed class SavePostRequest
{
    [Required(ErrorMessage = "Indica el títol.")]
    [StringLength(150, MinimumLength = 5, ErrorMessage = "El títol ha de tenir entre 5 i 150 caràcters.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Escriu el contingut.")]
    [StringLength(50_000, ErrorMessage = "El contingut no pot superar els 50.000 caràcters.")]
    public string BodyMarkdown { get; set; } = string.Empty;

    [MaxLength(5, ErrorMessage = "Pots indicar un màxim de 5 etiquetes.")]
    public List<string> Tags { get; set; } = [];
}

public sealed class PreviewRequest
{
    public Guid? PostId { get; set; }
    [Required(ErrorMessage = "Escriu el contingut.")]
    [StringLength(50_000, ErrorMessage = "El contingut no pot superar els 50.000 caràcters.")]
    public string BodyMarkdown { get; set; } = string.Empty;
}

public sealed class CreateCommentRequest
{
    [Required(ErrorMessage = "Escriu el comentari.")]
    [StringLength(2_000, ErrorMessage = "El comentari no pot superar els 2.000 caràcters.")]
    public string Body { get; set; } = string.Empty;
}
