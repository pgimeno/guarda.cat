namespace GuardaCat.Contracts.Profiles;

public sealed record ProfileResponse(Guid Id, string DisplayName, string? Bio, GuardaCat.Contracts.Media.MediaResponse? Avatar = null);
