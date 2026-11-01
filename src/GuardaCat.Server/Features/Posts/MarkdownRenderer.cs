using Ganss.Xss;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GuardaCat.Server.Features.Posts;

public sealed class MarkdownRenderer
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().DisableHtml().UsePreciseSourceLocation().Build();
    private readonly HtmlSanitizer sanitizer = new();

    public MarkdownRenderer()
    {
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "h1", "h2", "h3", "h4", "h5", "h6", "a",
            "pre", "code", "blockquote", "ul", "ol", "li", "strong", "em", "hr", "br", "img"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(["href", "title", "src", "alt"]);
        sanitizer.UriAttributes.Clear();
        sanitizer.UriAttributes.Add("href");
        sanitizer.UriAttributes.Add("src");
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["https", "http"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowDataAttributes = false;
    }

    public bool TryRender(string markdown, out string html, IReadOnlySet<string>? imageUrls = null, bool omitUnavailable = false)
    {
        var document = Markdown.Parse(markdown, pipeline);
        foreach (var link in document.Descendants<LinkInline>().Where(link => link.IsImage).ToList())
        {
            if (link.Url is not null && imageUrls?.Contains(link.Url) == true) continue;
            if (omitUnavailable) link.Remove();
            else { html = string.Empty; return false; }
        }
        html = sanitizer.Sanitize(document.ToHtml(pipeline));
        return true;
    }

    public string RemoveImage(string markdown, string url)
    {
        var document = Markdown.Parse(markdown, pipeline);
        foreach (var link in document.Descendants<LinkInline>().Where(link => link.IsImage && link.Url == url)
            .OrderByDescending(link => link.Span.Start))
            markdown = markdown.Remove(link.Span.Start, link.Span.Length);
        return markdown;
    }
}
