using System.Net;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class ApiErrorsTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public void Api_error_is_status_and_title_without_detail()
    {
        var ex = new UmbracoApiException(HttpStatusCode.BadRequest, "Validation failed", "secret detail");

        Assert.Equal("HTTP 400 Validation failed", ApiErrors.Describe(ex));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Umbraco API answered HTTP 502.")]
    public void Missing_problem_title_gives_only_the_status(string title)
    {
        var ex = new UmbracoApiException(HttpStatusCode.BadGateway, title, null);

        Assert.Equal("HTTP 502", ApiErrors.Describe(ex));
    }

    [Fact]
    public void Mapping_error_names_the_alias_only_when_the_exception_carries_one()
    {
        Assert.Equal("Mapping error in price", ApiErrors.Describe(new MappingException("price", "value 12.5 is wrong")));
        Assert.Equal("Mapping error", ApiErrors.Describe(new MappingException(null, "text with a value")));
        Assert.Equal("Mapping error", ApiErrors.Describe(new InvalidDataException("Property 'x' got value 7")));
        Assert.Equal("Mapping error", ApiErrors.Describe(new NotSupportedException("Editor 'Y' of property 'x'")));
    }

    [Fact]
    public void Other_exceptions_give_the_type_name_only()
    {
        Assert.Equal("InvalidOperationException", ApiErrors.Describe(new InvalidOperationException("content of a field")));
    }

    [Fact]
    public void Item_failures_are_api_refusals_mapping_and_read_errors_but_not_401_or_cancellation()
    {
        Assert.True(ApiErrors.IsItemFailure(new UmbracoApiException(HttpStatusCode.BadRequest, "x", null), Ct));
        Assert.True(ApiErrors.IsItemFailure(new MappingException("a", "b"), Ct));
        Assert.True(ApiErrors.IsItemFailure(new InvalidOperationException(), Ct));
        Assert.False(ApiErrors.IsItemFailure(new UmbracoApiException(HttpStatusCode.Unauthorized, "x", null), Ct));
        Assert.False(ApiErrors.IsItemFailure(new UmbracoAuthException("x"), Ct));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.False(ApiErrors.IsItemFailure(new InvalidOperationException(), cancelled.Token));
    }
}
