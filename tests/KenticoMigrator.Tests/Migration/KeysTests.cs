using KenticoMigrator.Migration;

namespace KenticoMigrator.Tests.Migration;

public sealed class KeysTests
{
    [Fact]
    public void Same_input_same_key()
    {
        Assert.Equal(Keys.Derived("doctype", "contentPage"), Keys.Derived("doctype", "contentPage"));
    }

    [Fact]
    public void Different_kind_gives_different_key()
    {
        Assert.NotEqual(Keys.Derived("doctype", "x"), Keys.Derived("datatype", "x"));
    }

    [Fact]
    public void Case_insensitive_natural_key()
    {
        Assert.Equal(Keys.Derived("DocType", "ContentPage"), Keys.Derived("doctype", "contentpage"));
    }

    [Fact]
    public void Known_vector()
    {
        // Computed with Python: uuid.uuid5(UUID('6f1b6d0e-6c2f-5a8e-9f41-6b2c0b7a4d11'), 'doctype:contentpage')
        Assert.Equal(Guid.Parse("ad3cc52d-6835-5de2-86f8-7a503e5cf11f"), Keys.Derived("doctype", "contentPage"));
        // uuid.uuid5(..., 'datatype:dropdown-brand status')
        Assert.Equal(Guid.Parse("0e5c2e15-ba48-5531-a727-1e64df11232f"), Keys.Derived("datatype", "dropdown-Brand Status"));
    }

    [Fact]
    public void Version_nibble_is_5()
    {
        var text = Keys.Derived("doctype", "any").ToString("D");

        Assert.Equal('5', text[14]);
        Assert.Contains(text[19], "89ab"); // RFC variant bits
    }

    [Fact]
    public void Non_ascii_natural_key_vector()
    {
        // Computed with Python: uuid.uuid5(ns, 'datatype:dropdown-\u00e5ngstr\u00f6m \u65e5\u672c') (name is lowercased first)
        Assert.Equal(
            Guid.Parse("2c06b30e-21f6-533b-9d3e-dbafdb77d4df"),
            Keys.Derived("datatype", "dropdown-\u00c5ngstr\u00f6m \u65e5\u672c"));
    }

    [Fact]
    public void Source_guids_are_used_as_keys()
    {
        var guid = Guid.NewGuid();

        Assert.Equal(guid, Keys.ForAttachment(guid));
        Assert.Equal(guid, Keys.ForMediaFile(guid));
    }
}
