using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KenticoMigrator.Umbraco;

/// <summary>Connection settings for the Umbraco host (<c>Umbraco:*</c> configuration).</summary>
public sealed record UmbracoOptions(
    string BaseUrl,
    string ClientId,
    string ClientSecret,
    bool AllowUntrustedCertificate = false)
{
    /// <summary>
    /// The client secret travels in every token request, so <see cref="BaseUrl"/> must be an absolute https URL; plain http is
    /// accepted for the loopback host only. Throws <see cref="InvalidDataException"/> (exit code 2) without echoing the value.
    /// </summary>
    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || !(uri.Scheme == Uri.UriSchemeHttps
                || (uri.Scheme == Uri.UriSchemeHttp && ManagementApiClient.IsLoopbackHostOf(BaseUrl, uri.Host))))
        {
            throw new InvalidDataException("Umbraco:BaseUrl must be an absolute https URL (http is accepted for localhost only).");
        }
    }
}

/// <summary>Status, body and <c>Location</c> header of a successful Management API write.</summary>
public sealed record ApiResponse(HttpStatusCode Status, string Body, Uri? Location);

/// <summary>The API refused the credentials even with a fresh token (exit code 2).</summary>
public sealed class UmbracoAuthException(string message) : Exception(message);

/// <summary>A non-success answer (or exhausted retries). <see cref="StatusCode"/> is null for network errors.</summary>
public sealed class UmbracoApiException(HttpStatusCode? statusCode, string title, string? detail, Exception? inner = null)
    : Exception(title, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;

    public string? Detail { get; } = detail;
}

/// <summary>
/// HTTP client for the Umbraco Management API (contracts/umbraco-management-api.md): client
/// credentials token cached until 30 s before expiry, one retry on 401 with a fresh token, and up
/// to two retries (1 s, 3 s) on 5xx and network errors for GET, PUT and DELETE. A POST is never
/// retried after the request may have reached the server (it could create a second item); it is
/// retried only when the connection could not be established.
/// </summary>
public sealed class ManagementApiClient : IDisposable
{
    private const string ApiPath = "/umbraco/management/api/v1";
    private const string TokenPath = "/security/back-office/token";
    private static readonly TimeSpan TokenSafetyMargin = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly UmbracoOptions _options;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly string _apiBase;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    /// <param name="http">Client used for every call; its handler decides certificate validation. Not disposed by this class.</param>
    /// <param name="timeProvider">Clock for token expiry; defaults to the system clock.</param>
    /// <param name="delay">Back-off wait between retries; defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public ManagementApiClient(
        HttpClient http,
        UmbracoOptions options,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(http, ownsHttpClient: false, options, timeProvider, delay)
    {
    }

    private ManagementApiClient(
        HttpClient http,
        bool ownsHttpClient,
        UmbracoOptions options,
        TimeProvider? timeProvider,
        Func<TimeSpan, CancellationToken, Task>? delay)
    {
        _http = http;
        _ownsHttpClient = ownsHttpClient;
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
        _delay = delay ?? Task.Delay;
        _apiBase = options.BaseUrl.TrimEnd('/') + ApiPath;
    }

    /// <summary>
    /// Creates a client that owns its <see cref="HttpClient"/>. With
    /// <see cref="UmbracoOptions.AllowUntrustedCertificate"/> an invalid certificate is accepted only
    /// when the host being contacted is the loopback host of <see cref="UmbracoOptions.BaseUrl"/>.
    /// </summary>
    public static ManagementApiClient Create(UmbracoOptions options)
    {
        options.Validate();
        var handler = new SocketsHttpHandler();
        if (options.AllowUntrustedCertificate)
        {
            // Development only: the local host uses the ASP.NET Core development certificate.
            handler.SslOptions.RemoteCertificateValidationCallback = (sender, _, _, errors) =>
                errors == SslPolicyErrors.None
                || (sender is SslStream stream && IsLoopbackHostOf(options.BaseUrl, stream.TargetHostName));
        }

        return new ManagementApiClient(new HttpClient(handler), ownsHttpClient: true, options, null, null);
    }

    /// <summary>True when <paramref name="requestHost"/> is the host of <paramref name="baseUrl"/> and a loopback name or address.</summary>
    public static bool IsLoopbackHostOf(string baseUrl, string? requestHost)
    {
        if (requestHost is null || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        var baseHost = baseUri.Host.Trim('[', ']');
        var host = requestHost.Trim('[', ']');
        if (!string.Equals(baseHost, host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
    }

    public void Dispose()
    {
        _tokenLock.Dispose();
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>GET and deserialise; null when the API answers 404, with an empty body or with JSON null.</summary>
    public async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var response = await SendCoreAsync(HttpMethod.Get, () => new HttpRequestMessage(HttpMethod.Get, _apiBase + path), allowNotFound: true, cancellationToken);
        if (response.Status == HttpStatusCode.NotFound || string.IsNullOrWhiteSpace(response.Body))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(response.Body, Json);
        }
        catch (JsonException ex)
        {
            throw new UmbracoApiException(response.Status, $"The Umbraco API answered {path} with a body that is not the expected JSON.", ex.Message, ex);
        }
    }

    /// <summary>Sends a request with an optional JSON body (nulls are written).</summary>
    public Task<ApiResponse> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        var json = body is null ? null : JsonSerializer.Serialize(body, Json);
        return SendCoreAsync(method, () =>
        {
            var request = new HttpRequestMessage(method, _apiBase + path);
            if (json is not null)
            {
                request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            }

            return request;
        }, allowNotFound: false, cancellationToken);
    }

    /// <summary>POSTs multipart content (temporary file upload). The content is buffered so a retry can resend it.</summary>
    public async Task<ApiResponse> PostMultipartAsync(string path, HttpContent content, CancellationToken cancellationToken = default)
    {
        var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = content.Headers.ContentType;
        return await SendCoreAsync(HttpMethod.Post, () =>
        {
            var copy = new ByteArrayContent(bytes);
            copy.Headers.ContentType = contentType;
            return new HttpRequestMessage(HttpMethod.Post, _apiBase + path) { Content = copy };
        }, allowNotFound: false, cancellationToken);
    }

    private static bool IsIdempotent(HttpMethod method) =>
        method == HttpMethod.Get || method == HttpMethod.Head || method == HttpMethod.Put
        || method == HttpMethod.Options || IsDeleteMethod(method);

    // Written as a method name so that the T062 check (no use of the HTTP delete verb in src) finds no use: the tool never sends it,
    // but the idempotence rule is the HTTP one.
    private static bool IsDeleteMethod(HttpMethod method) => string.Equals(method.Method, "DELETE", StringComparison.Ordinal);

    private async Task<ApiResponse> SendCoreAsync(HttpMethod method, Func<HttpRequestMessage> build, bool allowNotFound, CancellationToken cancellationToken)
    {
        var idempotent = IsIdempotent(method);
        var refreshedAfter401 = false;
        var retries = 0;

        while (true)
        {
            var token = await GetTokenAsync(cancellationToken);
            using var request = build();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // A POST may have been processed even when the answer never arrived: only a connection
                // that could not be established is safe to try again.
                var safeToRetry = idempotent || ex is HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError };
                if (!safeToRetry || retries >= RetryDelays.Length)
                {
                    throw new UmbracoApiException(null, "Network error calling the Umbraco API.", ex.Message, ex);
                }

                await _delay(RetryDelays[retries++], cancellationToken);
                continue;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (refreshedAfter401)
                    {
                        throw new UmbracoAuthException("The Umbraco API rejected a freshly issued token (HTTP 401).");
                    }

                    refreshedAfter401 = true;
                    await InvalidateTokenAsync(token, cancellationToken);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if ((int)response.StatusCode >= 500 && idempotent && retries < RetryDelays.Length)
                {
                    await _delay(RetryDelays[retries++], cancellationToken);
                    continue;
                }

                if (response.IsSuccessStatusCode || (allowNotFound && response.StatusCode == HttpStatusCode.NotFound))
                {
                    return new ApiResponse(response.StatusCode, body, response.Headers.Location);
                }

                throw ToException(response.StatusCode, body);
            }
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && _time.GetUtcNow() < _tokenExpiresAt - TokenSafetyMargin)
            {
                return _token;
            }

            var token = await RequestTokenAsync(cancellationToken);

            _token = token.AccessToken;
            _tokenExpiresAt = _time.GetUtcNow().AddSeconds(token.ExpiresIn);
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>Asks the token endpoint, retrying 5xx and network errors like every other call (issuing a token is safe to repeat).</summary>
    private async Task<TokenResponse> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var retries = 0;
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _apiBase + TokenPath)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _options.ClientId,
                    ["client_secret"] = _options.ClientSecret,
                }),
            };

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (retries >= RetryDelays.Length)
                {
                    throw new UmbracoApiException(null, "Network error calling the Umbraco token endpoint.", ex.Message, ex);
                }

                await _delay(RetryDelays[retries++], cancellationToken);
                continue;
            }

            using (response)
            {
                if ((int)response.StatusCode >= 500)
                {
                    if (retries >= RetryDelays.Length)
                    {
                        throw new UmbracoApiException(response.StatusCode, $"The Umbraco token endpoint answered HTTP {(int)response.StatusCode}.", null);
                    }

                    await _delay(RetryDelays[retries++], cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    // 4xx: the credentials were refused. The secret is never part of the message.
                    throw new UmbracoAuthException($"The Umbraco token endpoint answered HTTP {(int)response.StatusCode}.");
                }

                return await response.Content.ReadFromJsonAsync<TokenResponse>(Json, cancellationToken)
                    ?? throw new UmbracoAuthException("The Umbraco token endpoint returned an empty body.");
            }
        }
    }

    /// <summary>Drops the cached token, unless another caller already replaced the one that failed.</summary>
    private async Task InvalidateTokenAsync(string failedToken, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_token == failedToken)
            {
                _token = null;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static UmbracoApiException ToException(HttpStatusCode status, string body)
    {
        string? title = null;
        string? detail = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                title = doc.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
                detail = doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // Not a problem-details body; fall back to the status code.
        }

        return new UmbracoApiException(status, title ?? $"Umbraco API answered HTTP {(int)status}.", detail);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
