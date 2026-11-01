using System.Text.Json.Serialization;

namespace GuardaCat.Contracts.Media;

public static class MediaLimits
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxPostImages = 5;
    public const long MaxAvatarBytes = 256 * 1024;
    public const int MaxAvatarDimension = 512;
    public const int MaxDimension = 4096;
    public const long MaxPixels = 12_000_000;
}

public sealed record MediaResponse(Guid Id, string Url, int Width, int Height);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MediaResponse))]
[JsonSerializable(typeof(List<MediaResponse>))]
public partial class MediaJsonContext : JsonSerializerContext;
