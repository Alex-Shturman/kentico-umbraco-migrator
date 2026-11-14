using System.Text.Json.Nodes;

namespace KenticoMigrator.Umbraco;

/// <summary>
/// The Management API calls the sync classes make, as JSON. The real implementation is <see cref="UmbracoApi"/>;
/// tests use an in-memory fake. Paths are relative to <c>/umbraco/management/api/v1</c>.
/// </summary>
public interface IUmbracoApi
{
    /// <summary>GET; null when the item does not exist (404, empty body or JSON null).</summary>
    Task<JsonNode?> GetAsync(string path, CancellationToken ct);

    /// <summary>POST, PUT or DELETE with an optional JSON body. Throws <see cref="UmbracoApiException"/> on a non-success answer.</summary>
    Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct);
}

/// <summary><see cref="IUmbracoApi"/> over <see cref="ManagementApiClient"/> (which keeps the retry and token rules).</summary>
public sealed class UmbracoApi(ManagementApiClient client) : IUmbracoApi
{
    public Task<JsonNode?> GetAsync(string path, CancellationToken ct) => client.GetAsync<JsonNode>(path, ct);

    public Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct) =>
        client.SendAsync(method, path, body, ct);
}

/// <summary>Turns a failed API call into the only error text a report may carry (FR-028): status and problem title.</summary>
public static class ApiErrors
{
    public static string Describe(Exception ex) => ex switch
    {
        UmbracoApiException { StatusCode: { } status } api => $"HTTP {(int)status} {api.Message}",
        UmbracoApiException api => api.Message,
        UmbracoAuthException auth => auth.Message,
        InvalidDataException or NotSupportedException => ex.Message,
        _ => ex.GetType().Name,
    };
}
