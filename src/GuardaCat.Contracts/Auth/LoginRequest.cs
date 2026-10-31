using System.ComponentModel.DataAnnotations;

namespace GuardaCat.Contracts.Auth;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Indica el correu electrònic.")]
    [EmailAddress(ErrorMessage = "El correu electrònic no és vàlid.")]
    [StringLength(256, ErrorMessage = "El correu és massa llarg.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica la contrasenya.")]
    [StringLength(128, ErrorMessage = "La contrasenya és massa llarga.")]
    public string Password { get; set; } = string.Empty;
}
