using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Routing;

namespace UmbracoSite.Migration;

/// <summary>Puts <see cref="TemplateLessRedirectFinder"/> in front of the URL finder.</summary>
public sealed class TemplateLessRedirectComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.ContentFinders().InsertBefore<ContentFinderByUrlNew, TemplateLessRedirectFinder>();
}
