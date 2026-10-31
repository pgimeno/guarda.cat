using Ganss.Xss;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace GuardaCat.Server.Features.Posts;

public sealed class MarkdownRenderer
{
    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();
    private readonly HtmlSanitizer sanitizer = new();

    public MarkdownRenderer()
    {
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.UnionWith(["p", "h1", "h2", "h3", "h4", "h5", "h6", "a",
            "pre", "code", "blockquote", "ul", "ol", "li", "strong", "em", "hr", "br"]);
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.UnionWith(["href", "title"]);
        sanitizer.UriAttributes.Clear();
        sanitizer.UriAttributes.Add("href");
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["https", "http"]);
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowDataAttributes = false;
    }

    public bool TryRender(string markdown, out string html)
    {
        var document = Markdown.Parse(markdown, pipeline);
        if (document.Descendants<LinkInline>().Any(link => link.IsImage))
        {
            // Media ownership/storage belongs to phase 3; no image source is accepted yet.
            html = string.Empty;
            return false;
        }
        html = sanitizer.Sanitize(document.ToHtml(pipeline));
        return true;
    }
}
