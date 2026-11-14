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

    // Verified on Umbraco 17 (research R7): these built-in data types exist; there is no built-in Decimal.
    private static readonly string[] VerifiedBuiltInNames =
        ["Textstring", "Textarea", "Richtext editor", "Numeric", "True/false", "Date Picker with time", "Tags", "Dropdown", "Media Picker", "Multiple Media Picker"];

    [Fact]
    public void Built_in_data_types_use_only_verified_umbraco_names()
    {
        var site = SourceFactory.SampleSite();
        var profile = SourceFactory.SampleProfile();
        var editors = new[] { "TextBox", "TextArea", "RichText", "Integer", "TrueFalse", "DateTime", "Tags", "MediaSingle", "MediaMultiple" };
        var fields = editors.Select(e => new FieldMapping("Name", "p" + e, e, "G")).ToList();
        var broken = profile with { Types = [profile.Types[0] with { Fields = fields }] };

        var plan = PlanBuilder.Build(site, broken);

        var builtIns = plan.DataTypes.Where(d => d.IsBuiltIn).Select(d => d.Name).ToList();
        Assert.Equal(editors.Length, builtIns.Count);
        Assert.All(builtIns, name => Assert.Contains(name, VerifiedBuiltInNames));
        Assert.Contains("Media Picker", builtIns);
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
