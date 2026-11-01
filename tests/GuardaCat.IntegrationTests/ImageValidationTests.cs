using System.Text;
using GuardaCat.Contracts.Media;
using GuardaCat.Server.Features.Media;
using SkiaSharp;
using Xunit;

namespace GuardaCat.IntegrationTests;

public sealed class ImageValidationTests
{
    [Theory]
    [InlineData("image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("image/png", SKEncodedImageFormat.Png)]
    public async Task Real_images_are_decoded_and_trailing_payload_is_removed(string mime, SKEncodedImageFormat format)
    {
        var input = CreateImage(format).Concat(Encoding.UTF8.GetBytes("<script>untrusted-payload</script>")).ToArray();
        using var stream = new MemoryStream(input);
        var result = await ImageValidator.ReadAsync(stream, mime, default);
        Assert.NotNull(result);
        Assert.Equal(8, result.Width);
        Assert.Equal(6, result.Height);
        Assert.DoesNotContain("untrusted-payload", Encoding.UTF8.GetString(result.Bytes));
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("image/gif")]
    [InlineData("application/octet-stream")]
    public async Task Other_content_types_are_rejected(string mime)
    {
        using var stream = new MemoryStream(CreateImage());
        Assert.Null(await ImageValidator.ReadAsync(stream, mime, default));
    }

    [Fact]
    public async Task Forged_type_and_header_only_file_are_rejected()
    {
        using var mismatch = new MemoryStream(CreateImage());
        Assert.Null(await ImageValidator.ReadAsync(mismatch, "image/jpeg", default));
        using var truncated = new MemoryStream(CreateImage()[..20]);
        Assert.Null(await ImageValidator.ReadAsync(truncated, "image/png", default));
        using var html = new MemoryStream(Encoding.UTF8.GetBytes("<html>not an image</html>"));
        Assert.Null(await ImageValidator.ReadAsync(html, "image/png", default));
    }

    [Fact]
    public async Task Body_limit_is_enforced_without_a_content_length_header()
    {
        using var stream = new MemoryStream(new byte[MediaLimits.MaxBytes + 1]);
        await Assert.ThrowsAsync<ImageTooLargeException>(() => ImageValidator.ReadAsync(stream, "image/png", default));
    }

    [Theory]
    [InlineData(4097, 1)]
    [InlineData(4000, 3100)]
    public async Task Excessive_dimensions_or_pixel_count_are_rejected(int width, int height)
    {
        using var stream = new MemoryStream(CreateImage(width: width, height: height));
        Assert.Null(await ImageValidator.ReadAsync(stream, "image/png", default));
    }

    internal static byte[] CreateImage(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 8, int height = 6)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.Coral);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }

    [Fact]
    public async Task Avatar_has_smaller_byte_and_dimension_limits()
    {
        using var oversized = new MemoryStream(new byte[MediaLimits.MaxAvatarBytes + 1]);
        await Assert.ThrowsAsync<ImageTooLargeException>(() => ImageValidator.ReadAsync(oversized, "image/png", default, avatar: true));
        using var tooWide = new MemoryStream(CreateImage(width: 513));
        Assert.Null(await ImageValidator.ReadAsync(tooWide, "image/png", default, avatar: true));
        using var valid = new MemoryStream(CreateImage(width: 512, height: 512));
        Assert.NotNull(await ImageValidator.ReadAsync(valid, "image/png", default, avatar: true));
    }
}
