using System.Net;
using System.Text;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class ManagementApiClientTests
{
    private static readonly UmbracoOptions Options = new("https://umbraco.test", "umbraco-back-office-migrator", "secret");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Sample(string Name);

    /// <summary>Scripted handler: records every request and answers from a queue per route.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Auth)> Requests { get; } = [];
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public int TokenRequests => Requests.Count(r => r.Path.EndsWith("/security/back-office/token", StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
            return Task.FromResult(Respond(request));
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Token(string token, int expiresIn = 300) =>
        Json(HttpStatusCode.OK, $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":{{expiresIn}}}""");

    private static bool IsTokenRequest(HttpRequestMessage r) => r.RequestUri!.AbsolutePath.EndsWith("/security/back-office/token", StringComparison.Ordinal);

    private static ManagementApiClient NewClient(FakeHandler handler, ManualTime? time = null, List<TimeSpan>? delays = null) =>
        new(new HttpClient(handler), Options, time ?? new ManualTime(), (d, _) =>
        {
            delays?.Add(d);
            return Task.CompletedTask;
        });

    [Fact]
    public async Task Refreshes_token_on_401()
    {
        var handler = new FakeHandler();
        var tokens = new Queue<string>(["old", "fresh"]);
        handler.Respond = r =>
        {
            if (IsTokenRequest(r))
            {
                return Token(tokens.Dequeue());
            }

            return r.Headers.Authorization?.Parameter == "old"
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json(HttpStatusCode.OK, """{"name":"kayak"}""");
        };
        var client = NewClient(handler);

        var result = await client.GetAsync<Sample>("/document/x", Ct);

        Assert.Equal(new Sample("kayak"), result);
        Assert.Equal(2, handler.TokenRequests);
        Assert.Equal("Bearer fresh", handler.Requests[^1].Auth);
    }

    [Fact]
    public async Task Second_401_throws()
    {
        var handler = new FakeHandler
        {
            Respond = r => IsTokenRequest(r) ? Token("t") : new HttpResponseMessage(HttpStatusCode.Unauthorized),
        };
        var client = NewClient(handler);

        await Assert.ThrowsAsync<UmbracoAuthException>(() => client.GetAsync<Sample>("/document/x", Ct));

        Assert.Equal(2, handler.Requests.Count(r => r.Path.EndsWith("/document/x", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Reuses_token_until_expiry()
    {
        var handler = new FakeHandler
        {
            Respond = r => IsTokenRequest(r) ? Token("t", expiresIn: 300) : Json(HttpStatusCode.OK, """{"name":"a"}"""),
        };
        var time = new ManualTime();
        var client = NewClient(handler, time);

        for (var i = 0; i < 10; i++)
        {
            await client.GetAsync<Sample>("/document/x", Ct);
        }

        Assert.Equal(1, handler.TokenRequests);

        time.Advance(TimeSpan.FromSeconds(271)); // inside the 30 s safety margin of a 300 s token
        await client.GetAsync<Sample>("/document/x", Ct);

        Assert.Equal(2, handler.TokenRequests);
    }

    [Fact]
    public async Task Get_returns_null_on_404()
    {
        var handler = new FakeHandler
        {
            Respond = r => IsTokenRequest(r) ? Token("t") : new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        var client = NewClient(handler);

        Assert.Null(await client.GetAsync<Sample>("/document/missing", Ct));
    }

    [Fact]
    public async Task Token_endpoint_5xx_is_retried_not_reported_as_bad_credentials()
    {
        var handler = new FakeHandler();
        var tokenCalls = 0;
        handler.Respond = r =>
        {
            if (!IsTokenRequest(r))
            {
                return Json(HttpStatusCode.OK, """{"name":"a"}""");
            }

            return ++tokenCalls < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Token("t");
        };
        var delays = new List<TimeSpan>();
        var client = NewClient(handler, delays: delays);

        Assert.Equal(new Sample("a"), await client.GetAsync<Sample>("/document/x", Ct));
        Assert.Equal(3, handler.TokenRequests);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)], delays);
    }

    [Fact]
    public async Task Token_endpoint_rejecting_credentials_throws_auth_exception_without_retry()
    {
        var handler = new FakeHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.BadRequest) };
        var client = NewClient(handler);

        await Assert.ThrowsAsync<UmbracoAuthException>(() => client.GetAsync<Sample>("/document/x", Ct));

        Assert.Equal(1, handler.TokenRequests);
    }

    [Fact]
    public async Task Retries_5xx_twice_then_throws()
    {
        var handler = new FakeHandler
        {
            Respond = r => IsTokenRequest(r) ? Token("t") : new HttpResponseMessage(HttpStatusCode.BadGateway),
        };
        var delays = new List<TimeSpan>();
        var client = NewClient(handler, delays: delays);

        var ex = await Assert.ThrowsAsync<UmbracoApiException>(() => client.SendAsync(HttpMethod.Put, "/document/x", new { name = "a" }, Ct));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(3, handler.Requests.Count(r => r.Path.EndsWith("/document/x", StringComparison.Ordinal)));
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)], delays);
    }

    [Fact]
    public async Task Post_is_not_retried_on_5xx()
    {
        var handler = new FakeHandler
        {
            Respond = r => IsTokenRequest(r) ? Token("t") : new HttpResponseMessage(HttpStatusCode.BadGateway),
        };
        var delays = new List<TimeSpan>();
        var client = NewClient(handler, delays: delays);

        var ex = await Assert.ThrowsAsync<UmbracoApiException>(() => client.SendAsync(HttpMethod.Post, "/document", new { name = "a" }, Ct));

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(1, handler.Requests.Count(r => r.Method == HttpMethod.Post && !r.Path.EndsWith("/token", StringComparison.Ordinal)));
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Post_is_retried_when_the_connection_was_refused_before_sending()
    {
        var handler = new FakeHandler();
        var failures = 1;
        handler.Respond = r =>
        {
            if (IsTokenRequest(r))
            {
                return Token("t");
            }

            if (failures-- > 0)
            {
                throw new HttpRequestException(HttpRequestError.ConnectionError, "refused");
            }

            return new HttpResponseMessage(HttpStatusCode.Created);
        };
        var client = NewClient(handler);

        var response = await client.SendAsync(HttpMethod.Post, "/document", new { name = "a" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.Status);
    }

    [Fact]
    public async Task Post_is_not_retried_on_a_network_error_after_the_request_may_have_been_sent()
    {
        var handler = new FakeHandler();
        handler.Respond = r => IsTokenRequest(r)
            ? Token("t")
            : throw new HttpRequestException(HttpRequestError.ResponseEnded, "connection closed");
        var client = NewClient(handler);

        await Assert.ThrowsAsync<UmbracoApiException>(() => client.SendAsync(HttpMethod.Post, "/document", new { name = "a" }, Ct));

        Assert.Equal(1, handler.Requests.Count(r => r.Path.EndsWith("/document", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Get_returns_default_for_an_empty_body_and_for_json_null()
    {
        var body = "";
        var handler = new FakeHandler
        {
            Respond = r => IsTokenRequest(r) ? Token("t") : Json(HttpStatusCode.OK, body),
        };
        var client = NewClient(handler);

        Assert.Null(await client.GetAsync<Sample>("/document/x", Ct));

        body = "null";
        Assert.Null(await client.GetAsync<Sample>("/document/x", Ct));
    }

    [Fact]
    public async Task Get_wraps_invalid_json_in_api_exception_naming_the_path()
    {
        var handler = new FakeHandler
        {
            Respond = r => IsTokenRequest(r) ? Token("t") : Json(HttpStatusCode.OK, "<html>not json</html>"),
        };
        var client = NewClient(handler);

        var ex = await Assert.ThrowsAsync<UmbracoApiException>(() => client.GetAsync<Sample>("/document/x", Ct));

        Assert.Contains("/document/x", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://localhost:44317", "localhost", true)]
    [InlineData("https://127.0.0.1:44317", "127.0.0.1", true)]
    [InlineData("https://[::1]:44317", "::1", true)]
    [InlineData("https://[::1]:44317", "[::1]", true)]
    [InlineData("https://localhost:44317", "evil.example", false)]
    [InlineData("https://umbraco.example", "umbraco.example", false)]
    [InlineData("https://localhost:44317", null, false)]
    public void Untrusted_certificates_are_accepted_only_for_the_loopback_host_of_the_base_url(string baseUrl, string? requestHost, bool expected)
    {
        Assert.Equal(expected, ManagementApiClient.IsLoopbackHostOf(baseUrl, requestHost));
    }
}
