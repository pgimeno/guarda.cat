namespace GuardaCat.Contracts.Auth;

public sealed record CurrentUserResponse(Guid Id, string DisplayName, string Email);

public sealed record CsrfResponse(string Token);
