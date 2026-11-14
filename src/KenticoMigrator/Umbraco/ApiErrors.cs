using System.Net;

namespace KenticoMigrator.Umbraco;

/// <summary>
/// A planned value or structure that cannot be written. It may name the property alias (never a value); a report then says
/// "Mapping error in &lt;alias&gt;" instead of the bare "Mapping error".
/// </summary>
public sealed class MappingException(string? alias, string reason) : Exception(reason)
{
    public string? Alias { get; } = alias;
}

/// <summary>Turns a failed call into the only error text a report or the console may carry (FR-028).</summary>
public static class ApiErrors
{
    // The text ManagementApiClient uses when the answer has no problem title.
    private const string UntitledPrefix = "Umbraco API answered HTTP";

    /// <summary>
    /// <c>HTTP &lt;status&gt; &lt;problem title&gt;</c> for an API refusal (just <c>HTTP &lt;status&gt;</c> without a title; never the
    /// detail), a fixed mapping text, and the type name for anything else. Exception messages are never passed through, because
    /// they can echo field content.
    /// </summary>
    public static string Describe(Exception ex) => ex switch
    {
        UmbracoApiException { StatusCode: { } status } api => HttpText(status, api.Message),
        UmbracoApiException api => api.Message,
        UmbracoAuthException auth => auth.Message,
        MappingException { Alias: { Length: > 0 } alias } => $"Mapping error in {alias}",
        MappingException or InvalidDataException or NotSupportedException => "Mapping error",
        _ => ex.GetType().Name,
    };

    /// <summary>
    /// A failure that belongs to one item: an API refusal (not 401), a value that cannot be written, or an answer the tool
    /// cannot read. A cancelled run, a rejected token and programming errors are not item failures and propagate.
    /// </summary>
    public static bool IsItemFailure(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested
        && ex is UmbracoApiException { StatusCode: not HttpStatusCode.Unauthorized }
            or MappingException or InvalidDataException or InvalidOperationException or FormatException or KeyNotFoundException or System.Text.Json.JsonException;

    private static string HttpText(HttpStatusCode status, string title) =>
        string.IsNullOrWhiteSpace(title) || title.StartsWith(UntitledPrefix, StringComparison.Ordinal)
            ? $"HTTP {(int)status}"
            : $"HTTP {(int)status} {title}";
}
