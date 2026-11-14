using KenticoMigrator.Migration;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Migration;

public sealed class RedirectPlannerTests
{
    private static TargetDocument Target(
        int id,
        string oldPath,
        string[] oldUrls,
        bool publish = true,
        int sortOrder = 1) =>
        new(GuidFor(id), null, "contentPage", oldPath.TrimStart('/'), "x", sortOrder, publish,
            new Dictionary<string, PlannedValue>(), oldPath, oldUrls, null);

    private static IReadOnlyDictionary<Guid, string> Actual(params (int Id, string Url)[] urls) =>
        urls.ToDictionary(u => GuidFor(u.Id), u => u.Url);

    [Fact]
    public void No_redirect_when_only_case_differs()
    {
        // Verified on Umbraco 17: routing is case-insensitive and document URLs end with a slash.
        var docs = new[] { Target(1, "/AboutUs", ["/AboutUs"]) };

        var plan = RedirectPlanner.Plan(docs, Actual((1, "/aboutus/")));

        Assert.Empty(plan.Redirects);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Aspx_form_gets_redirect()
    {
        var docs = new[] { Target(1, "/AboutUs", ["/AboutUs", "/AboutUs.aspx"]) };

        var redirect = Assert.Single(RedirectPlanner.Plan(docs, Actual((1, "/aboutus"))).Redirects);

        Assert.Equal(new PlannedRedirect("/AboutUs.aspx", GuidFor(1), "/aboutus", RedirectReasons.Aspx), redirect);
    }

    [Fact]
    public void Old_home_paths_redirect_to_root()
    {
        var docs = new[] { Target(1, "/Home", ["/Home", "/Home.aspx"]) };

        var redirects = RedirectPlanner.Plan(docs, Actual((1, "/"))).Redirects;

        Assert.Equal(["/Home", "/Home.aspx"], redirects.Select(r => r.OldUrl));
        Assert.All(redirects, r =>
        {
            Assert.Equal(RedirectReasons.Home, r.Reason);
            Assert.Equal("/", r.NewUrl);
        });
    }

    [Fact]
    public void Brand_name_with_space_gets_renamed_redirect()
    {
        var docs = new[] { Target(1, "/Catalog/Brands/Sea-Spirit", ["/brand-search/Sea Spirit"]) };

        var redirect = Assert.Single(RedirectPlanner.Plan(docs, Actual((1, "/catalog/brands/sea-spirit"))).Redirects);

        Assert.Equal(new PlannedRedirect("/brand-search/Sea Spirit", GuidFor(1), "/catalog/brands/sea-spirit", RedirectReasons.Renamed), redirect);
    }

    [Fact]
    public void Fixed_alias_gets_alias_redirect_and_moved_page_gets_url_path_redirect()
    {
        var docs = new[] { Target(1, "/Catalog/Products/Tandem", ["/Catalog/Products/Tandem", "/tandem"]) };

        var redirects = RedirectPlanner.Plan(docs, Actual((1, "/products/tandem"))).Redirects;

        Assert.Equal([RedirectReasons.UrlPath, RedirectReasons.Alias], redirects.Select(r => r.Reason));
    }

    [Fact]
    public void Unpublished_document_gets_no_redirect()
    {
        var docs = new[] { Target(1, "/Draft", ["/Draft.aspx"], publish: false) };

        Assert.Empty(RedirectPlanner.Plan(docs, Actual((1, "/draft"))).Redirects);
    }

    [Fact]
    public void Document_without_an_actual_url_gets_no_redirect()
    {
        var docs = new[] { Target(1, "/AboutUs", ["/AboutUs.aspx"]) };

        Assert.Empty(RedirectPlanner.Plan(docs, Actual()).Redirects);
    }

    [Fact]
    public void Duplicate_old_url_goes_to_first_by_claim_order_with_warning_that_names_no_url()
    {
        var docs = new[]
        {
            Target(2, "/Second", ["/shared-secret-route"], sortOrder: 5),
            Target(1, "/First", ["/shared-secret-route"], sortOrder: 2),
        };

        var plan = RedirectPlanner.Plan(docs, Actual((1, "/first"), (2, "/second")));

        var redirect = Assert.Single(plan.Redirects);
        Assert.Equal(GuidFor(1), redirect.TargetKey);
        var warning = Assert.Single(plan.Warnings);
        Assert.Equal(PlanSeverities.Warning, warning.Severity);
        Assert.Equal("/Second", warning.OldPath);
        Assert.Contains("/First", warning.Message);
        Assert.Contains(GuidFor(1).ToString(), warning.Message);
        Assert.DoesNotContain("shared-secret-route", warning.Message);
    }

    [Fact]
    public void Repeated_old_url_of_the_same_document_is_planned_once()
    {
        var docs = new[] { Target(1, "/AboutUs", ["/old", "/OLD"]) };

        var plan = RedirectPlanner.Plan(docs, Actual((1, "/aboutus")));

        Assert.Single(plan.Redirects);
        Assert.Empty(plan.Warnings);
    }
}
