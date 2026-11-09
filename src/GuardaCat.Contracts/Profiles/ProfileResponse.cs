namespace GuardaCat.Contracts.Profiles;

public sealed record ProfileResponse(Guid Id, string DisplayName, string? Bio, GuardaCat.Contracts.Media.MediaResponse? Avatar = null);

public sealed record ActiveUserResponse(Guid Id, string DisplayName, GuardaCat.Contracts.Media.MediaResponse? Avatar = null);
