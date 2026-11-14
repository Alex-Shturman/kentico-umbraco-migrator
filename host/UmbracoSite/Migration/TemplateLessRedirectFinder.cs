using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace UmbracoSite.Migration;

/// <summary>
/// An old URL that is also the URL of a page without a template (a migrated folder, for example the old alias
/// <c>/products</c> next to the folder <c>/Products</c>) would answer 404: Umbraco finds the folder, has no template to
/// render it with, and never reaches its redirect finder. This finder runs before the URL finder and, only when the page
/// found at the request path has no template and a redirect is registered for that path to a page that has a template, answers with that redirect.
/// Every other request is left to the normal finders, so folders without a redirect still answer 404.
/// Limitation: it looks routes up without a domain root, like the redirects the migrator registers, so it is meant for a site
/// without configured domains (the host in this repository).
/// </summary>
public sealed class TemplateLessRedirectFinder : IContentFinder
{
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IRedirectUrlService _redirectUrlService;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly IDocumentUrlService _documentUrlService;

    public TemplateLessRedirectFinder(
        IUmbracoContextAccessor umbracoContextAccessor,
        IRedirectUrlService redirectUrlService,
        IPublishedUrlProvider urlProvider,
        IDocumentUrlService documentUrlService)
    {
        _documentUrlService = documentUrlService;
        _umbracoContextAccessor = umbracoContextAccessor;
        _redirectUrlService = redirectUrlService;
        _urlProvider = urlProvider;
    }

    public Task<bool> TryFindContent(IPublishedRequestBuilder request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = request.AbsolutePathDecoded;
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext))
        {
            return Task.FromResult(false);
        }

        // Same key form as the redirects the migrator registers: decoded, lower case, no trailing slash.
        var route = path.ToLowerInvariant().TrimEnd('/');
        if (route.Length == 0)
        {
            return Task.FromResult(false);
        }

        // The same lookup the URL finder does; a request without a culture uses the default one.
        var foundKey = _documentUrlService.GetDocumentKeyByRoute(path, request.Culture, null, false);
        var found = foundKey is { } key ? umbracoContext.Content?.GetById(key) : null;
        if (found is null || found.TemplateId is > 0)
        {
            return Task.FromResult(false);
        }

        var redirect = _redirectUrlService.GetMostRecentRedirectUrl(route);
        var target = redirect is null ? null : umbracoContext.Content?.GetById(redirect.ContentKey);
        if (target is null || target.Key == found.Key || target.TemplateId is not > 0)
        {
            return Task.FromResult(false);
        }

        var url = _urlProvider.GetUrl(target);
        if (string.IsNullOrEmpty(url) || url == "#")
        {
            return Task.FromResult(false);
        }

        request.SetRedirectPermanent(url);
        return Task.FromResult(true);
    }
}
