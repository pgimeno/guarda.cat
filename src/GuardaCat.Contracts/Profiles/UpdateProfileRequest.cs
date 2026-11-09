using System.ComponentModel.DataAnnotations;

namespace GuardaCat.Contracts.Profiles;

public sealed class UpdateProfileRequest
{
    [Required(ErrorMessage = "Indica el nom públic.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "El nom ha de tenir entre 2 i 60 caràcters.")]
    public string DisplayName { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "La biografia no pot superar els 500 caràcters.")]
    public string? Bio { get; set; }
}
