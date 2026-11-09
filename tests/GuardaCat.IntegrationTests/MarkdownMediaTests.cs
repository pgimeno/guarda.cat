using GuardaCat.Server.Features.Posts;
using Xunit;

namespace GuardaCat.IntegrationTests;

public sealed class MarkdownMediaTests
{
    private readonly MarkdownRenderer renderer = new();
    private const string Url = "/media/147edbd3-e5b5-4d5a-933a-2b3384bc4eb1";

    [Fact]
    public void Only_allowlisted_post_images_render_and_deleted_images_do_not_hide_article()
    {
        Assert.True(renderer.TryRender($"Hola ![Foto]({Url})", out var html, new HashSet<string> { Url }));
        Assert.Contains($"src=\"{Url}\"", html);
        Assert.False(renderer.TryRender($"![Foto]({Url})", out _));
        Assert.False(renderer.TryRender("![Foto](https://example.org/x.png)", out _, new HashSet<string> { Url }));
        Assert.True(renderer.TryRender("![Foto](https://example.org/x.png)", out var external,
            new HashSet<string> { "https://example.org/x.png" }));
        Assert.Contains("src=\"https://example.org/x.png\"", external);
        Assert.False(renderer.TryRender("![Foto](data:image/png;base64,AAAA)", out _, new HashSet<string> { Url }));
        Assert.True(renderer.TryRender($"Hola ![Foto]({Url})", out var deleted, omitUnavailable: true));
        Assert.Contains("Hola", deleted);
        Assert.DoesNotContain("<img", deleted);
    }

    [Fact]
    public void Feed_excerpt_omits_image_markdown_and_preserves_image_order()
    {
        const string external = "https://example.org/foto.png";
        var markdown = $"## Títol\n\nText **visible**.\n\n![Local]({Url})\n\n![Externa](<{external}>)";

        Assert.Equal([Url, external], renderer.ImageUrlsInOrder(markdown));
        var excerpt = renderer.Excerpt(markdown);
        Assert.Contains("Títol", excerpt);
        Assert.Contains("Text visible.", excerpt);
        Assert.DoesNotContain("Imatge", excerpt);
        Assert.DoesNotContain("![", excerpt);
        Assert.DoesNotContain("media/", excerpt);
    }

    [Theory]
    [InlineData("Abans ![Foto]({0}) després")]
    [InlineData("Abans ![Foto][imatge] després\n\n[imatge]: {0}")]
    [InlineData("Abans ![Foto]({0} \"Títol\") després")]
    public void Removing_image_removes_its_markdown_node_and_preserves_other_text(string template)
    {
        var updated = renderer.RemoveImage(string.Format(template, Url), Url);
        Assert.Contains("Abans", updated);
        Assert.Contains("després", updated);
        Assert.True(renderer.TryRender(updated, out var html));
        Assert.DoesNotContain("<img", html);
    }
}
