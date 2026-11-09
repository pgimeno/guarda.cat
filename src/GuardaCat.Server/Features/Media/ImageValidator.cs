using GuardaCat.Contracts.Media;
using SkiaSharp;

namespace GuardaCat.Server.Features.Media;

public sealed record ValidatedImage(byte[] Bytes, string ContentType, int Width, int Height);

public static class ImageValidator
{
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
        using var image = SKImage.FromBitmap(bitmap);
        using var output = image.Encode(expected, 85);
        if (output is null) return null;
        if (output.Size > maxBytes) throw new ImageTooLargeException();
        // Re-encoding drops EXIF and any trailing payload from the supplied file.
        return new(output.ToArray(), contentType, info.Width, info.Height);
    }
}

public sealed class ImageTooLargeException : Exception;
