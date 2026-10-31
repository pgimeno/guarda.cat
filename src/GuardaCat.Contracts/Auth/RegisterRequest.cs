using System.ComponentModel.DataAnnotations;

namespace GuardaCat.Contracts.Auth;

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "Indica el nom públic.")]
    [StringLength(60, MinimumLength = 2, ErrorMessage = "El nom ha de tenir entre 2 i 60 caràcters.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica el correu electrònic.")]
    [EmailAddress(ErrorMessage = "El correu electrònic no és vàlid.")]
    [StringLength(256, ErrorMessage = "El correu és massa llarg.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica una contrasenya.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "La contrasenya ha de tenir entre 10 i 128 caràcters.")]
    public string Password { get; set; } = string.Empty;
}
