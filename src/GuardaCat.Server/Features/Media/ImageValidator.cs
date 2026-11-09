using GuardaCat.Contracts.Media;
using SkiaSharp;

namespace GuardaCat.Server.Features.Media;

public sealed record ValidatedImage(byte[] Bytes, string ContentType, int Width, int Height);

public static class ImageValidator
{
    private const int MaxStoredDimension = 2560;
    private const int LossyQuality = 85;

    public static async Task<ValidatedImage?> ReadAsync(Stream input, string? contentType, CancellationToken ct, bool avatar = false)
    {
        if (contentType is not ("image/jpeg" or "image/png")) return null;
        var maxBytes = avatar ? MediaLimits.MaxAvatarBytes : MediaLimits.MaxBytes;
        var maxDimension = avatar ? MediaLimits.MaxAvatarDimension : MediaLimits.MaxDimension;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes) throw new ImageTooLargeException();
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (buffer.Length == 0) return null;
        using var data = SKData.CreateCopy(buffer.ToArray());
        using var codec = SKCodec.Create(data);
        var expected = contentType == "image/png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        if (codec is null || codec.EncodedFormat != expected || codec.FrameCount > 1) return null;
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > maxDimension
            || info.Height > maxDimension || (long)info.Width * info.Height > MediaLimits.MaxPixels) return null;
        // Decode all pixels and require success, rather than trusting MIME or a header signature.
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) return null;
        ct.ThrowIfCancellationRequested();

        var storedDimension = avatar ? MediaLimits.MaxAvatarDimension : MaxStoredDimension;
        var resizeRequired = Math.Max(bitmap.Width, bitmap.Height) > storedDimension;
        using var resized = resizeRequired ? Resize(bitmap, storedDimension) : null;
        if (resizeRequired && resized is null) return null;
        var optimized = resized ?? bitmap;
        using var image = SKImage.FromBitmap(optimized);
        using var originalFormat = image.Encode(expected, expected == SKEncodedImageFormat.Png ? 100 : LossyQuality);
        using var pixels = optimized.PeekPixels();
        var webpOptions = expected == SKEncodedImageFormat.Png
            ? new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 100)
            : new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, LossyQuality);
        using var webp = pixels.Encode(webpOptions);
        if (originalFormat is null || webp is null) return null;

        var useWebp = webp.Size < originalFormat.Size;
        var output = useWebp ? webp : originalFormat;
        if (output.Size > maxBytes) throw new ImageTooLargeException();
        // Re-encoding drops EXIF and any trailing payload from the supplied file.
        return new(output.ToArray(), useWebp ? "image/webp" : contentType, optimized.Width, optimized.Height);
    }

    private static SKBitmap? Resize(SKBitmap source, int maxDimension)
    {
        var longestSide = Math.Max(source.Width, source.Height);
        var scale = (double)maxDimension / longestSide;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        return source.Resize(new SKImageInfo(width, height, source.ColorType, source.AlphaType),
            new SKSamplingOptions(SKCubicResampler.Mitchell));
    }
}

public sealed class ImageTooLargeException : Exception;
