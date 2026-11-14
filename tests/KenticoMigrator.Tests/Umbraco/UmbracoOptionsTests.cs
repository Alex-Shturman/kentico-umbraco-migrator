using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class UmbracoOptionsTests
{
    private static UmbracoOptions With(string baseUrl) => new(baseUrl, "umbraco-back-office-x", "secret");

    [Theory]
    [InlineData("https://umbraco.example")]
    [InlineData("https://localhost:44317")]
    [InlineData("http://localhost:11317")]
    [InlineData("http://127.0.0.1:11317")]
    [InlineData("http://[::1]:11317")]
    public void Accepts_https_and_http_on_loopback(string baseUrl) => With(baseUrl).Validate();

    [Theory]
    [InlineData("http://umbraco.example")]
    [InlineData("http://192.168.1.5")]
    [InlineData("ftp://localhost")]
    [InlineData("localhost:44317")]
    [InlineData("/relative")]
    [InlineData("")]
    public void Refuses_everything_else_without_echoing_the_value(string baseUrl)
    {
        var ex = Assert.Throws<InvalidDataException>(() => With(baseUrl).Validate());

        Assert.DoesNotContain("umbraco.example", ex.Message);
        Assert.Contains("Umbraco:BaseUrl", ex.Message);
    }

    [Fact]
    public void Create_validates_before_building_a_client()
    {
        Assert.Throws<InvalidDataException>(() => ManagementApiClient.Create(With("http://umbraco.example")));
    }
}
