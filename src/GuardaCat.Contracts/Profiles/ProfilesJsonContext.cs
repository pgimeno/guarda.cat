using System.Text.Json.Serialization;

namespace GuardaCat.Contracts.Profiles;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(UpdateProfileRequest))]
[JsonSerializable(typeof(ProfileResponse))]
public partial class ProfilesJsonContext : JsonSerializerContext;
