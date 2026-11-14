using System.Net;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class UmbracoApiTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task Delete_is_refused_before_any_request_is_made()
    {
        var handler = new CountingHandler();
        using var client = new ManagementApiClient(new HttpClient(handler), new UmbracoOptions("https://umbraco.test", "umbraco-back-office-x", "s"));
        var api = new UmbracoApi(client);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => api.SendAsync(HttpMethod.Delete, "/document/1", null, TestContext.Current.CancellationToken));

        Assert.Contains("delete", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.Requests);
    }
}
