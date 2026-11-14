using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;

namespace KenticoMigrator.Tests.Support;

/// <summary>Builds invented in-memory source data (a kayak shop) for pure unit tests.</summary>
internal static class SourceFactory
{
    public static Guid GuidFor(int id) => new(id, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    public static SiteProfile SampleProfile() => SiteProfile.Load(RepoPaths.Combine("profiles", "sample.json"));

    public static SourceField Field(string column, string dataType = "text", string? control = null, params string[] options) =>
        new(column, dataType, null, control, options, GuidFor(column.GetHashCode() & 0xFFFF));

    public static SourcePageType Type(string className, params SourceField[] fields) =>
        new(className, null, fields.Length > 0, fields);

    public static IReadOnlyList<SourcePageType> SampleTypes() =>
    [
        Type("CMS.MenuItem"),
        Type("CMS.Article", Field("ArticleName"), Field("ArticleTeaserText", "longtext"), Field("ArticleText", "longtext"),
            Field("ArticleTeaserImage", "file")),
        Type("DEMO.Brand", Field("Name"), Field("HeaderText"), Field("TopText", "longtext"), Field("BottomText", "longtext"),
            Field("Synonyms"), Field("MetaTitle"), Field("MetaDescription"), Field("MetaKeywords"), Field("Order", "integer")),
        Type("DEMO.Product", Field("ProductName"), Field("Featured", "boolean"), Field("ShippingInformation", "longtext"),
            Field("ShippingImage", "text", "RadioButtonsControl", "standard", "express"), Field("Images", "docattachments")),
    ];

    /// <summary>Alias is the last segment of <paramref name="aliasPath"/> unless given.</summary>
    public static SourceDocument Doc(
        int id,
        int? parentId,
        string className,
        string aliasPath,
        string? name = null,
        string? alias = null,
        int order = 1,
        bool published = true,
        DateTime? publishFrom = null,
        DateTime? publishTo = null,
        IReadOnlyDictionary<string, object?>? fields = null,
        IReadOnlyList<EditableRegion>? regions = null,
        IReadOnlyDictionary<string, object?>? sku = null,
        string? urlPath = null,
        string? pageTitle = null,
        bool hideInNavigation = false,
        bool showInSitemap = true,
        int? linkedNodeId = null)
    {
        var last = aliasPath[(aliasPath.LastIndexOf('/') + 1)..];
        return new SourceDocument(
            GuidFor(id), id, parentId, className, name ?? last, alias ?? last, aliasPath, order, published,
            publishFrom, publishTo, hideInNavigation, showInSitemap, pageTitle, null, null,
            new DateTime(2020, 1, 1), new DateTime(2020, 2, 1), urlPath, null,
            fields ?? new Dictionary<string, object?>(), regions ?? [], sku, linkedNodeId);
    }

    public static EditableRegion Region(string id, string html) => new(id, html);

    public static SourceSite Site(
        IReadOnlyList<SourceDocument>? documents = null,
        IReadOnlyList<SourcePageType>? types = null,
        IReadOnlyList<SourceAttachment>? attachments = null,
        IReadOnlyList<SourceAlias>? aliases = null,
        IReadOnlyList<SourceMediaFile>? mediaFiles = null,
        IReadOnlyDictionary<string, IReadOnlyList<SourceRow>>? unlinked = null) =>
        new("kayaks", "11.0.0", types ?? SampleTypes(), documents ?? [], attachments ?? [], aliases ?? [],
            mediaFiles ?? [], unlinked ?? new Dictionary<string, IReadOnlyList<SourceRow>>());

    /// <summary>A small tree: Home, About, Account/Profile, Catalog folder with Brands and Products, Articles.</summary>
    public static SourceSite SampleSite() => Site(
    [
        Doc(1, null, "CMS.MenuItem", "/Home", order: 1),
        Doc(2, null, "CMS.MenuItem", "/About", name: "About us", order: 2,
            regions: [Region("r1", "<p>About</p>")]),
        Doc(3, null, "CMS.MenuItem", "/Account", order: 3),
        Doc(4, 3, "CMS.MenuItem", "/Account/Profile", order: 1),
        Doc(5, null, "CMS.MenuItem", "/Catalog", order: 4),
        Doc(6, 5, "CMS.MenuItem", "/Catalog/Brands", order: 1),
        Doc(7, 6, "DEMO.Brand", "/Catalog/Brands/Sea-Spirit", name: "Sea Spirit", alias: "Sea-Spirit", order: 1,
            fields: Fields(("Name", "Sea Spirit"), ("Synonyms", "ss, sea"))),
        Doc(8, 5, "CMS.MenuItem", "/Catalog/Products", order: 2),
        Doc(9, 8, "DEMO.Product", "/Catalog/Products/Tandem", name: "Tandem", order: 1,
            fields: Fields(("ProductName", "Tandem"), ("Featured", true), ("ShippingImage", "express")),
            sku: Fields(("SKUNumber", "K-100"), ("SKUPrice", 999.5m))),
        Doc(10, null, "CMS.MenuItem", "/Articles", order: 5),
        Doc(11, 10, "CMS.Article", "/Articles/First-trip", name: "First trip", order: 1,
            fields: Fields(("ArticleName", "First trip"), ("ArticleText", "<p>Hello</p>"))),
    ]);

    public static IReadOnlyDictionary<string, object?> Fields(params (string Key, object? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value);
}
