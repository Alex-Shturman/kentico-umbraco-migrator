using System.Text.RegularExpressions;
using KenticoMigrator.Kentico;

namespace KenticoMigrator.Migration;

/// <summary>
/// Resolves hrefs found in rich text to planned documents and media (FR-018 to FR-021). Built from the plan,
/// so every document's raw old URLs (alias path, <c>.aspx</c> form, aliases, brand route) resolve to its key.
/// </summary>
public sealed partial class UrlIndex
{
    private static readonly Regex SchemeAuthority = SchemeAuthorityRegex();
    private static readonly Regex AnyScheme = AnySchemeRegex();
    private static readonly Regex MediaPath = MediaPathRegex();
    private static readonly Regex WildcardPlaceholder = WildcardPlaceholderRegex();

    private readonly IReadOnlyList<string> _ownHosts;
    private readonly Dictionary<string, Guid> _documents = [];
    private readonly HashSet<Guid> _media;
    private readonly List<string> _applicationPrefixes;
    private readonly List<Regex> _wildcards;

    private UrlIndex(MigrationPlan plan, IReadOnlyList<SourceAlias> aliases, SiteProfile profile)
    {
        _ownHosts = profile.OwnHosts;
        _media = [.. plan.Media.Where(m => m.MediaType != MediaType.Folder).Select(m => m.Key)];

        // First document in plan order wins when two pages claim the same old URL.
        foreach (var doc in plan.Documents)
        {
            foreach (var url in doc.OldUrls.Prepend(doc.OldPath))
            {
                _documents.TryAdd(Normalize(url, _ownHosts), doc.Key);
            }
        }

        // The site root is the home page, which is the one planned document without a parent.
        var root = plan.Documents.FirstOrDefault(d => d.ParentKey is null);
        if (root is not null)
        {
            _documents.TryAdd("/", root.Key);
        }

        _applicationPrefixes =
        [
            .. profile.ApplicationRoutePrefixes.Concat(profile.ExcludedPathPrefixes).Select(p => Normalize(p, _ownHosts)),
        ];
        _wildcards = [.. aliases.Where(a => a.IsWildcard).Select(a => WildcardToRegex(Normalize(a.UrlPath, _ownHosts)))];
    }

    public static UrlIndex Create(MigrationPlan plan, IReadOnlyList<SourceAlias> aliases, SiteProfile profile)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(profile);
        return new UrlIndex(plan, aliases, profile);
    }

    /// <summary>Classifies an href and, for documents and media, returns the key of the target.</summary>
    public (LinkKind Kind, Guid? Key) Resolve(string href)
    {
        var text = href?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return (LinkKind.Unknown, null);
        }

        if (text.StartsWith('#'))
        {
            return (LinkKind.Anchor, null);
        }

        if (text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || text.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return (LinkKind.Mail, null);
        }

        if (SchemeAuthority.IsMatch(text) || text.StartsWith("//", StringComparison.Ordinal))
        {
            if (!IsOwnHost(text))
            {
                return (LinkKind.External, null);
            }
        }
        else if (AnyScheme.IsMatch(text))
        {
            return (LinkKind.Unknown, null); // javascript:, data: and similar are never rewritten
        }

        var path = Normalize(text, _ownHosts);

        var media = MediaPath.Match(path);
        if (media.Success)
        {
            var key = Guid.Parse(media.Groups[2].Value);
            return _media.Contains(key) ? (LinkKind.Media, key) : (LinkKind.Unknown, null);
        }

        if (_documents.TryGetValue(path, out var document))
        {
            return (LinkKind.Document, document);
        }

        if (_applicationPrefixes.Any(p => path == p || path.StartsWith(p + "/", StringComparison.Ordinal))
            || _wildcards.Any(w => w.IsMatch(path)))
        {
            return (LinkKind.Application, null);
        }

        return (LinkKind.Unknown, null);
    }

    /// <summary>
    /// Canonical form for comparing URLs: query and fragment removed, own host and <c>~</c> removed, percent-decoded,
    /// lowercase, <c>.aspx</c> and trailing slash removed. Foreign hosts are kept.
    /// </summary>
    public static string Normalize(string url, IEnumerable<string>? ownHosts = null) =>
        NormalizeRoute(url, ownHosts, ignoreCase: true, stripAspx: true);

    /// <summary>Same as <see cref="Normalize"/> with the case and <c>.aspx</c> steps optional (redirect planning compares them).</summary>
    internal static string NormalizeRoute(string url, IEnumerable<string>? ownHosts, bool ignoreCase, bool stripAspx)
    {
        var text = (url ?? string.Empty).Trim();
        var cut = text.IndexOfAny(['#', '?']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        if (text.StartsWith("//", StringComparison.Ordinal))
        {
            text = "http:" + text;
        }

        var scheme = SchemeAuthority.Match(text);
        if (scheme.Success)
        {
            var rest = text[scheme.Length..];
            var slash = rest.IndexOf('/');
            var host = slash >= 0 ? rest[..slash] : rest;
            if (IsHostIn(host, ownHosts))
            {
                text = slash >= 0 ? rest[slash..] : "/";
            }
        }
        else
        {
            text = text.TrimStart('~');
            if (!text.StartsWith('/'))
            {
                text = "/" + text;
            }
        }

        text = Uri.UnescapeDataString(text);
        if (ignoreCase)
        {
            text = text.ToLowerInvariant();
        }

        if (stripAspx && text.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^5];
        }

        text = text.Length > 1 ? text.TrimEnd('/') : text;
        return text.Length == 0 ? "/" : text;
    }

    private bool IsOwnHost(string absolute)
    {
        var text = absolute.StartsWith("//", StringComparison.Ordinal) ? "http:" + absolute : absolute;
        var scheme = SchemeAuthority.Match(text);
        var rest = text[scheme.Length..];
        var end = rest.IndexOfAny(['/', '?', '#']);
        return IsHostIn(end >= 0 ? rest[..end] : rest, _ownHosts);
    }

    private static bool IsHostIn(string authority, IEnumerable<string>? hosts)
    {
        if (hosts is null)
        {
            return false;
        }

        var host = authority[(authority.LastIndexOf('@') + 1)..];
        var colon = host.LastIndexOf(':');
        if (colon >= 0)
        {
            host = host[..colon];
        }

        return hosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A Kentico wildcard rule such as <c>/{title}-i{ID}</c> becomes a pattern with one path segment per placeholder.</summary>
    private static Regex WildcardToRegex(string normalizedPattern)
    {
        var literals = WildcardPlaceholder.Split(normalizedPattern).Select(Regex.Escape);
        return new Regex("^" + string.Join("[^/]+", literals) + "$", RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"^[a-z][a-z0-9+.\-]*://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SchemeAuthorityRegex();

    [GeneratedRegex(@"^[a-z][a-z0-9+.\-]*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnySchemeRegex();

    [GeneratedRegex(@"^/get(attachment|media)/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(/|$)", RegexOptions.CultureInvariant)]
    private static partial Regex MediaPathRegex();

    [GeneratedRegex(@"\{\w+\}", RegexOptions.CultureInvariant)]
    private static partial Regex WildcardPlaceholderRegex();
}
