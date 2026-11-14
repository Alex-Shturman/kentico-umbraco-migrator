using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Management.Controllers;
using Umbraco.Cms.Api.Management.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Authorization;

namespace UmbracoSite.Migration;

/// <summary>One redirect to register: an old URL path and the key of the document it should reach.</summary>
public sealed record RedirectRequest(string? Url, Guid ContentKey);

/// <summary>A request entry that was not registered, with the reason.</summary>
public sealed record RedirectFailure(string? Url, string Reason);

/// <summary>Outcome of one batch.</summary>
public sealed record RedirectsResponse(int Created, int Existing, IReadOnlyList<RedirectFailure> Failed);

/// <summary>
/// Registers native Umbraco redirects (visible in "URL Redirect Management") because the Management
/// API has no create endpoint for them. Contract: specs/001-kentico-umbraco-migrator/contracts/redirect-endpoint.md.
/// </summary>
[ApiVersion("1.0")]
[VersionedApiBackOfficeRoute("kentico-migration/redirects")]
[ApiExplorerSettings(GroupName = "Kentico migration")]
[Authorize(AuthorizationPolicies.SectionAccessContent)]
public sealed class RedirectsController : ManagementApiControllerBase
{
    private const int MaxBatchSize = 500;
    private const int MaxUrlLength = 2000;

    private readonly IRedirectUrlService _redirectUrlService;
    private readonly IContentService _contentService;
    private readonly ILogger<RedirectsController> _logger;

    public RedirectsController(
        IRedirectUrlService redirectUrlService,
        IContentService contentService,
        ILogger<RedirectsController> logger)
    {
        _redirectUrlService = redirectUrlService;
        _contentService = contentService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(RedirectsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult Register([FromBody] IReadOnlyList<RedirectRequest>? requests)
    {
        if (requests is null || requests.Count > MaxBatchSize)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid batch",
                Detail = $"Send a JSON array with at most {MaxBatchSize} entries.",
            });
        }

        var created = 0;
        var existing = 0;
        var failed = new List<RedirectFailure>();

        // Which document each stored route points to, loaded on first sight and kept current within the batch.
        var routeOwners = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var knownDocuments = new Dictionary<Guid, bool>();

        foreach (var request in requests)
        {
            if (request is null)
            {
                failed.Add(new RedirectFailure(null, "entry is null"));
                continue;
            }

            if (!TryNormalizeRoute(request.Url, out var route, out var invalidReason))
            {
                failed.Add(new RedirectFailure(request.Url, invalidReason));
                continue;
            }

            if (request.ContentKey == Guid.Empty)
            {
                failed.Add(new RedirectFailure(request.Url, "content not found"));
                continue;
            }

            if (!knownDocuments.TryGetValue(request.ContentKey, out var documentExists))
            {
                documentExists = _contentService.GetById(request.ContentKey) is not null;
                knownDocuments[request.ContentKey] = documentExists;
            }

            if (!documentExists)
            {
                failed.Add(new RedirectFailure(request.Url, "content not found"));
                continue;
            }

            if (!routeOwners.TryGetValue(route, out var owner))
            {
                var stored = _redirectUrlService.GetMostRecentRedirectUrl(route);
                if (stored is not null)
                {
                    owner = stored.ContentKey;
                    routeOwners[route] = owner;
                }
                else
                {
                    try
                    {
                        _redirectUrlService.Register(route, request.ContentKey, culture: null);
                        routeOwners[route] = request.ContentKey;
                        created++;
                    }
                    catch (Exception ex)
                    {
                        // The URL is left out of the log: it is caller data. The key identifies the entry.
                        _logger.LogError(ex, "Could not register a redirect for document {ContentKey}.", request.ContentKey);
                        failed.Add(new RedirectFailure(request.Url, ex.GetType().Name));
                    }

                    continue;
                }
            }

            if (owner == request.ContentKey)
            {
                existing++;
            }
            else
            {
                failed.Add(new RedirectFailure(request.Url, "url already redirects to another document"));
            }
        }

        return Ok(new RedirectsResponse(created, existing, failed));
    }

    /// <summary>
    /// Validates a request path and turns it into the form Umbraco looks redirects up by: percent-decoded,
    /// lower-cased and without a trailing slash. A route stored in any other form is never matched
    /// (verified, research R10).
    /// </summary>
    private static bool TryNormalizeRoute(string? url, out string route, out string reason)
    {
        route = string.Empty;
        reason = string.Empty;

        if (string.IsNullOrEmpty(url) || url[0] != '/')
        {
            reason = "url must start with /";
        }
        else if (url.StartsWith("//", StringComparison.Ordinal))
        {
            reason = "url must not start with //";
        }
        else if (url.Length > MaxUrlLength)
        {
            reason = $"url is longer than {MaxUrlLength} characters";
        }
        else if (url.Contains('?') || url.Contains('#'))
        {
            reason = "url must not contain a query string or fragment";
        }
        else if (url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            reason = "url must not contain whitespace or control characters (percent-encode them)";
        }
        else
        {
            var decoded = Uri.UnescapeDataString(url).ToLowerInvariant();
            if (decoded.Any(char.IsControl))
            {
                reason = "url must not contain control characters";
            }
            else
            {
                var trimmed = decoded.TrimEnd('/');
                if (trimmed.Contains("//", StringComparison.Ordinal))
                {
                    // Also catches "/%2fx", which decodes to "//x" and would be stored as a protocol-relative path.
                    reason = "url must not contain //";
                }
                else if (trimmed.Length == 0)
                {
                    // "/" as a source would redirect the whole site.
                    reason = "url must not be the site root";
                }
                else
                {
                    route = trimmed;
                    return true;
                }
            }
        }

        return false;
    }
}
