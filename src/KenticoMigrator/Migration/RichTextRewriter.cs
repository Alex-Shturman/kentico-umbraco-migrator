using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace KenticoMigrator.Migration;

/// <summary>
/// Rewrites links in a rich text fragment to Umbraco local links (FR-018, FR-019). The markup is the verified form
/// from research R6:
/// <c>&lt;a href="/{localLink:KEY}" type="document"&gt;</c>, <c>&lt;a href="/{localLink:KEY}" type="media"&gt;</c> and
/// <c>&lt;img src="MEDIA URL" data-udi="umb://media/KEYNODASHES"&gt;</c>. Everything else is left as it is, and rewriting
/// rewritten markup changes nothing.
/// </summary>
public sealed class RichTextRewriter
{
    private const string LocalLinkMarker = "{localLink:";
    private const string MediaUdiPrefix = "umb://media/";
    private const string DocumentType = "document";
    private const string MediaTypeName = "media";

    private readonly UrlIndex _index;
    private readonly Func<Guid, string?>? _mediaUrlOf;

    /// <param name="index">Resolves hrefs to documents and media.</param>
    /// <param name="mediaUrlOf">
    /// Optional: the Umbraco URL of an uploaded media item (known once media is synced). When it returns a URL for an
    /// image, <c>src</c> is set to it; otherwise <c>src</c> is kept and only <c>data-udi</c> is added.
    /// </param>
    public RichTextRewriter(UrlIndex index, Func<Guid, string?>? mediaUrlOf = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        _index = index;
        _mediaUrlOf = mediaUrlOf;
    }

    public RewriteResult Rewrite(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return new RewriteResult(string.Empty, 0, 0, 0, []);
        }

        using var document = new HtmlParser().ParseDocument(string.Empty);
        var body = document.Body!;
        body.InnerHtml = html;

        var rewritten = 0;
        var external = 0;
        var application = 0;
        var unresolved = new List<string>();
        var seenUnresolved = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in body.QuerySelectorAll("a[href], img[src]").ToList())
        {
            var isImage = element.LocalName == "img";
            var reference = element.GetAttribute(isImage ? "src" : "href")?.Trim();
            if (string.IsNullOrEmpty(reference) || IsAlreadyRewritten(element, reference, isImage)
                || reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (kind, key) = _index.Resolve(reference);
            switch (kind)
            {
                case LinkKind.Document when !isImage && key is { } documentKey:
                    SetLink(element, documentKey, DocumentType);
                    rewritten++;
                    break;
                case LinkKind.Media when key is { } mediaKey:
                    if (isImage)
                    {
                        SetImage(element, mediaKey);
                    }
                    else
                    {
                        SetLink(element, mediaKey, MediaTypeName);
                    }

                    rewritten++;
                    break;
                case LinkKind.External:
                    external++;
                    break;
                case LinkKind.Application:
                    application++;
                    break;
                case LinkKind.Mail:
                case LinkKind.Anchor:
                    break; // left as they are and not counted: neither is a page or file of the old site
                default:
                    if (seenUnresolved.Add(reference))
                    {
                        unresolved.Add(reference);
                    }

                    break;
            }
        }

        return new RewriteResult(body.InnerHtml, rewritten, external, application, unresolved);
    }

    private static bool IsAlreadyRewritten(IElement element, string reference, bool isImage) =>
        isImage
            ? element.GetAttribute("data-udi")?.StartsWith(MediaUdiPrefix, StringComparison.Ordinal) == true
            : reference.Contains(LocalLinkMarker, StringComparison.Ordinal);

    // href first, then type, then the remaining attributes in their original order (the verified form).
    private static void SetLink(IElement anchor, Guid key, string type)
    {
        var others = anchor.Attributes
            .Where(a => a.Name is not ("href" or "type"))
            .Select(a => (a.Name, a.Value))
            .ToList();
        foreach (var name in anchor.Attributes.Select(a => a.Name).ToList())
        {
            anchor.RemoveAttribute(name);
        }

        anchor.SetAttribute("href", $"/{LocalLinkMarker}{key:D}}}");
        anchor.SetAttribute("type", type);
        foreach (var (name, value) in others)
        {
            anchor.SetAttribute(name, value);
        }
    }

    private void SetImage(IElement image, Guid key)
    {
        if (_mediaUrlOf?.Invoke(key) is { Length: > 0 } url)
        {
            image.SetAttribute("src", url);
        }

        image.SetAttribute("data-udi", MediaUdiPrefix + key.ToString("N"));
    }
}
