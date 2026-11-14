using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Migration;

public sealed class UrlIndexTests
{
    private static readonly Guid Attachment1 = GuidFor(1001);
    private static readonly Guid LibraryFile = GuidFor(2001);

    private static readonly SourceAlias[] Aliases =
    [
        new(2, "/contact", false),
        new(4, "/{title}-i{ID}", true),
    ];

    private static (UrlIndex Index, MigrationPlan Plan) Create()
    {
        var site = SampleSite() with
        {
            Aliases = Aliases,
            Attachments = [Attachment(Attachment1, 9, "tandem.jpg", "Images", order: 1)],
            MediaFiles = [MediaFile(LibraryFile, "Gallery", "boats/kayak.jpg")],
        };
        var profile = SampleProfile();
        var plan = PlanBuilder.Build(site, profile);
        return (UrlIndex.Create(plan, Aliases, profile), plan);
    }

    private static Guid KeyOf(MigrationPlan plan, string oldPath) => plan.Documents.Single(d => d.OldPath == oldPath).Key;

    [Theory]
    [InlineData("/Search-Featured/Sea%20Spirit.aspx?x=1", "/search-featured/sea spirit")]
    [InlineData("~/About/", "/about")]
    [InlineData("About", "/about")]
    [InlineData("/Home.aspx#top", "/home")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData("https://WWW.kayaks.example/About.aspx", "/about")]
    [InlineData("https://www.kayaks.example", "/")]
    public void Normalize_examples(string url, string expected)
    {
        Assert.Equal(expected, UrlIndex.Normalize(url, ["www.kayaks.example"]));
    }

    [Fact]
    public void Normalize_keeps_foreign_hosts()
    {
        Assert.Equal("https://other.example/page", UrlIndex.Normalize("https://other.example/Page/?q=1", ["www.kayaks.example"]));
    }

    [Fact]
    public void Resolves_alias_path_and_aspx_form()
    {
        var (index, plan) = Create();

        Assert.Equal((LinkKind.Document, KeyOf(plan, "/About")), index.Resolve("/About"));
        Assert.Equal((LinkKind.Document, KeyOf(plan, "/About")), index.Resolve("/about.aspx"));
        Assert.Equal((LinkKind.Document, KeyOf(plan, "/Articles/First-trip")), index.Resolve("~/Articles/First-trip.aspx?x=1#a"));
    }

    [Fact]
    public void Resolves_document_alias()
    {
        var (index, plan) = Create();

        Assert.Equal((LinkKind.Document, KeyOf(plan, "/About")), index.Resolve("/contact"));
    }

    [Fact]
    public void Resolves_own_host_absolute_url()
    {
        var (index, plan) = Create();

        Assert.Equal((LinkKind.Document, KeyOf(plan, "/About")), index.Resolve("https://www.kayaks.example/About.aspx"));
        Assert.Equal((LinkKind.Document, KeyOf(plan, "/About")), index.Resolve("http://kayaks.example/about"));
    }

    [Fact]
    public void Resolves_root_to_home()
    {
        var (index, plan) = Create();

        Assert.Equal((LinkKind.Document, KeyOf(plan, "/Home")), index.Resolve("/"));
    }

    [Fact]
    public void Resolves_brand_route_by_name_any_case()
    {
        var (index, plan) = Create();
        var brand = KeyOf(plan, "/Catalog/Brands/Sea-Spirit");

        Assert.Equal((LinkKind.Document, brand), index.Resolve("/brand-search/Sea Spirit"));
        Assert.Equal((LinkKind.Document, brand), index.Resolve("/BRAND-SEARCH/sea%20spirit"));
    }

    [Fact]
    public void Resolves_getattachment_and_getmedia_guids()
    {
        var (index, _) = Create();

        Assert.Equal((LinkKind.Media, Attachment1), index.Resolve($"~/getattachment/{Attachment1}/tandem.jpg"));
        Assert.Equal((LinkKind.Media, Attachment1), index.Resolve($"/GetAttachment/{Attachment1:D}/tandem.jpg?width=100"));
        Assert.Equal((LinkKind.Media, LibraryFile), index.Resolve($"/getmedia/{LibraryFile}/kayak.jpg"));
        Assert.Equal((LinkKind.Unknown, (Guid?)null), index.Resolve($"/getmedia/{GuidFor(9999)}/gone.jpg"));
    }

    [Theory]
    [InlineData("https://other.example/page", LinkKind.External)]
    [InlineData("//cdn.other.example/x.js", LinkKind.External)]
    [InlineData("mailto:shop@kayaks.example", LinkKind.Mail)]
    [InlineData("#top", LinkKind.Anchor)]
    [InlineData("/cart", LinkKind.Application)]
    [InlineData("/Cart/Checkout.aspx", LinkKind.Application)]
    [InlineData("/Account/Profile", LinkKind.Application)]
    [InlineData("/kayak-sale-i42", LinkKind.Application)]
    [InlineData("/no-such-page", LinkKind.Unknown)]
    [InlineData("javascript:void(0)", LinkKind.Unknown)]
    [InlineData("", LinkKind.Unknown)]
    public void Classifies_external_mail_anchor_application_unknown(string href, LinkKind expected)
    {
        var (index, _) = Create();

        var (kind, key) = index.Resolve(href);

        Assert.Equal(expected, kind);
        Assert.Null(key);
    }
}
