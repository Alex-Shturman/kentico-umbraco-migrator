using System.Text.Json;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;

namespace KenticoMigrator.Tests.Migration;

public sealed class PlanBuilderSchemaTests
{
    private static readonly string[] MappedAliases =
        ["contentPage", "articleList", "brandList", "productList", "article", "brand", "product"];

    private static MigrationPlan Build() =>
        PlanBuilder.Build(SourceFactory.SampleSite(), SourceFactory.SampleProfile());

    [Fact]
    public void Creates_one_document_type_per_mapping()
    {
        var plan = Build();

        foreach (var alias in MappedAliases)
        {
            Assert.Single(plan.DocumentTypes, t => t.Alias == alias);
        }

        Assert.Single(plan.DocumentTypes, t => t.Alias == "folder");
        Assert.Equal(Keys.Derived("doctype", "brand"), plan.DocumentTypes.Single(t => t.Alias == "brand").Key);
    }

    [Fact]
    public void Adds_three_compositions_to_every_page_type()
    {
        var plan = Build();

        foreach (var alias in MappedAliases)
        {
            var type = plan.DocumentTypes.Single(t => t.Alias == alias);
            Assert.Equal(["seoComposition", "navigationComposition", "migrationComposition"], type.Compositions);
            Assert.False(type.IsElementOrComposition);
        }

        foreach (var alias in new[] { "seoComposition", "navigationComposition", "migrationComposition" })
        {
            Assert.True(plan.DocumentTypes.Single(t => t.Alias == alias).IsElementOrComposition);
        }

        var migration = plan.DocumentTypes.Single(t => t.Alias == "migrationComposition");
        Assert.Equal(["migrationSourceKey", "migrationFingerprint"], migration.Properties.Select(p => p.Alias));
    }

    [Fact]
    public void Folder_type_has_no_template()
    {
        var plan = Build();

        var folder = plan.DocumentTypes.Single(t => t.Alias == "folder");
        Assert.Null(folder.TemplateAlias);
        Assert.Contains("migrationComposition", folder.Compositions);
        Assert.Equal("migratedPage", plan.DocumentTypes.Single(t => t.Alias == "article").TemplateAlias);
    }

    [Fact]
    public void Dropdown_options_come_from_kentico_field()
    {
        var plan = Build();

        var property = plan.DocumentTypes.Single(t => t.Alias == "product").Properties.Single(p => p.Alias == "shippingImage");
        var dataType = plan.DataTypes.Single(d => d.Key == property.DataTypeKey);

        Assert.False(dataType.IsBuiltIn);
        Assert.Equal("Umbraco.DropDown.Flexible", dataType.EditorAlias);
        Assert.Equal(["standard", "express"], Assert.IsAssignableFrom<IEnumerable<string>>(dataType.Configuration["items"]));
    }

    [Fact]
    public void Decimal_data_type_is_created_by_the_tool_and_others_are_built_in()
    {
        var plan = Build();

        var price = plan.DocumentTypes.Single(t => t.Alias == "product").Properties.Single(p => p.Alias == "price");
        Assert.False(plan.DataTypes.Single(d => d.Key == price.DataTypeKey).IsBuiltIn);

        var title = plan.DocumentTypes.Single(t => t.Alias == "article").Properties.Single(p => p.Alias == "title");
        var textbox = plan.DataTypes.Single(d => d.Key == title.DataTypeKey);
        Assert.True(textbox.IsBuiltIn);
        Assert.Equal("Umbraco.TextBox", textbox.EditorAlias);
        Assert.Equal(plan.DataTypes.Count, plan.DataTypes.Select(d => d.Key).Distinct().Count());
    }

    [Fact]
    public void Templates_are_a_layout_and_one_page_template()
    {
        var plan = Build();

        Assert.Equal(["migratedLayout", "migratedPage"], plan.Templates.Select(t => t.Alias));
        Assert.Null(plan.Templates[0].MasterAlias);
        Assert.Equal("migratedLayout", plan.Templates[1].MasterAlias);
    }

    [Fact]
    public void Keys_are_stable_across_builds()
    {
        var first = JsonSerializer.Serialize(Build());
        var second = JsonSerializer.Serialize(Build());

        Assert.Equal(first, second);
    }
}
