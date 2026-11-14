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

    /// <summary>
    /// POST or PUT with an optional JSON body. Throws <see cref="UmbracoApiException"/> on a non-success answer. The tool has no
    /// delete path (FR-015b): the real implementation refuses DELETE with <see cref="InvalidOperationException"/>.
    /// </summary>
    Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct);
}

/// <summary><see cref="IUmbracoApi"/> over <see cref="ManagementApiClient"/> (which keeps the retry and token rules).</summary>
public sealed class UmbracoApi(ManagementApiClient client) : IUmbracoApi
{
    public Task<JsonNode?> GetAsync(string path, CancellationToken ct) => client.GetAsync<JsonNode>(path, ct);

    public Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct) =>
        method == HttpMethod.Delete
            ? throw new InvalidOperationException("The migrator never deletes anything: DELETE is not allowed (FR-015b).")
            : client.SendAsync(method, path, body, ct);
}
