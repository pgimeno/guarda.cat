using System.Text.Json.Serialization;

namespace GuardaCat.Contracts.Profiles;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UpdateProfileRequest))]
[JsonSerializable(typeof(ProfileResponse))]
[JsonSerializable(typeof(List<ActiveUserResponse>))]
public partial class ProfilesJsonContext : JsonSerializerContext;
