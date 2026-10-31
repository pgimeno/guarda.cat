using System.Text.Json.Serialization;

namespace GuardaCat.Contracts.Auth;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(CurrentUserResponse))]
[JsonSerializable(typeof(CsrfResponse))]
public partial class AuthJsonContext : JsonSerializerContext;
