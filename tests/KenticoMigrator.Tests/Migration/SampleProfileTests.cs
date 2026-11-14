using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;

namespace KenticoMigrator.Tests.Migration;

public sealed class SampleProfileTests
{
    [Fact]
    public void Sample_profile_loads()
    {
        var profile = SiteProfile.Load(RepoPaths.Combine("profiles", "sample.json"));

        Assert.Equal("sample", profile.Name);
        Assert.Contains("/Account", profile.ExcludedPathPrefixes);
        Assert.Contains("/Catalog", profile.FolderPaths);
        Assert.Contains(profile.Types, t => t.KenticoClass == "CMS.Article");
        Assert.Contains(profile.Types, t => t.KenticoClass == "DEMO.Product");
        Assert.Contains(profile.Types, t => t.PathOverride == "/Articles");
        Assert.Single(profile.Reparent, r => r.KenticoClass == "DEMO.Brand" && r.OldRoutePattern == "/brand-search/{Name}");
        Assert.Single(profile.UnlinkedRows, r => r.KenticoClass == "DEMO.Brand" && !r.Publish);
    }
}
