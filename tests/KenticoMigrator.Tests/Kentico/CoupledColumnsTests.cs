using KenticoMigrator.Kentico;

namespace KenticoMigrator.Tests.Kentico;

public sealed class CoupledColumnsTests
{
    [Fact]
    public void Resolves_names_case_insensitively_to_the_table_spelling()
    {
        var resolved = CoupledColumns.Resolve("DEMO.Brand", "DEMO_Brand", ["brandid", "NAME"], ["BrandID", "Name", "Extra"]);

        Assert.Equal(["BrandID", "Name"], resolved);
    }

    [Fact]
    public void Missing_column_names_page_type_table_and_column()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            CoupledColumns.Resolve("DEMO.Brand", "DEMO_Brand", ["BrandID", "Gone"], ["BrandID"]));

        Assert.Contains("DEMO.Brand", ex.Message);
        Assert.Contains("DEMO_Brand", ex.Message);
        Assert.Contains("Gone", ex.Message);
    }

    [Fact]
    public void Missing_table_has_no_columns_and_is_reported()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            CoupledColumns.Resolve("DEMO.Brand", "DEMO_Brand", ["BrandID"], []));

        Assert.Contains("DEMO_Brand", ex.Message);
    }
}
