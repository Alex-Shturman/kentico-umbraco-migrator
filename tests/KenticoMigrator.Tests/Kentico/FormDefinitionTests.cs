using KenticoMigrator.Kentico;

namespace KenticoMigrator.Tests.Kentico;

public sealed class FormDefinitionTests
{
    private const string BoatLikeForm = """
        <form version="2">
          <field column="ThingID" columntype="integer" guid="11111111-0000-4000-8000-000000000001" isPK="true" publicfield="false" visibility="none" />
          <field column="ThingName" columnsize="200" columntype="text" guid="11111111-0000-4000-8000-000000000002" visible="true">
            <properties><fieldcaption>Name</fieldcaption></properties>
            <settings><controlname>TextBoxControl</controlname></settings>
          </field>
          <field column="Price" columntype="decimal" guid="11111111-0000-4000-8000-000000000003" visible="true">
            <settings><controlname>CalculatedFieldControl</controlname></settings>
          </field>
          <field column="Body" columntype="longtext" guid="11111111-0000-4000-8000-000000000004">
            <settings><controlname>HtmlAreaControl</controlname></settings>
          </field>
        </form>
        """;

    [Fact]
    public void Parses_columns_and_types()
    {
        var fields = FormDefinition.Parse(BoatLikeForm);

        Assert.Equal(["ThingName", "Price", "Body"], fields.Select(f => f.Column));
        Assert.Equal(["text", "decimal", "longtext"], fields.Select(f => f.DataType));
        Assert.Equal(200, fields[0].Size);
        Assert.Null(fields[1].Size);
        Assert.Equal("TextBoxControl", fields[0].Control);
        Assert.Equal("HtmlAreaControl", fields[2].Control);
    }

    [Fact]
    public void Parses_dropdown_options()
    {
        const string xml = """
            <form version="2">
              <field column="Ship" columnsize="50" columntype="text" guid="22222222-0000-4000-8000-000000000001">
                <settings>
                  <controlname>RadioButtonsControl</controlname>
                  <Options>standard;Standard shipping
            freight;Freight shipping</Options>
                </settings>
              </field>
              <field column="Group" columnsize="100" columntype="text" guid="22222222-0000-4000-8000-000000000002">
                <settings>
                  <controlname>dropdownlistcontrol</controlname>
                  <Options>&lt;item value="" text="(none)" /&gt;&lt;item value="top" text="Top menu" /&gt;&lt;item value="footer" text="Footer" /&gt;</Options>
                </settings>
              </field>
              <field column="Plain" columntype="text" guid="22222222-0000-4000-8000-000000000003">
                <settings><controlname>DropDownListControl</controlname><Options>a
            b;Bee

            c</Options></settings>
              </field>
            </form>
            """;

        var fields = FormDefinition.Parse(xml);

        // "value;text" keeps the value, item markup keeps the value attribute, empty values are dropped.
        Assert.Equal(["standard", "freight"], fields[0].Options);
        Assert.Equal(["top", "footer"], fields[1].Options);
        Assert.Equal(["a", "b", "c"], fields[2].Options);
    }

    [Fact]
    public void Options_are_empty_for_other_controls()
    {
        var fields = FormDefinition.Parse(BoatLikeForm);

        Assert.All(fields, f => Assert.Empty(f.Options));
    }

    [Fact]
    public void Ignores_fields_without_column()
    {
        const string xml = """
            <form version="2">
              <category name="Basics" />
              <field columntype="text" guid="33333333-0000-4000-8000-000000000001" />
              <field column="Kept" columntype="integer" guid="33333333-0000-4000-8000-000000000002" />
            </form>
            """;

        var fields = FormDefinition.Parse(xml);

        Assert.Equal("Kept", Assert.Single(fields).Column);
    }

    [Fact]
    public void Reads_field_guid()
    {
        var fields = FormDefinition.Parse(BoatLikeForm);

        Assert.Equal(Guid.Parse("11111111-0000-4000-8000-000000000002"), fields[0].FieldGuid);
    }

    [Fact]
    public void Primary_key_is_excluded_from_fields_and_exposed_separately()
    {
        Assert.DoesNotContain(FormDefinition.Parse(BoatLikeForm), f => f.Column == "ThingID");
        Assert.Equal("ThingID", FormDefinition.PrimaryKeyColumn(BoatLikeForm));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<form version=\"2\" />")]
    public void Empty_definition_has_no_fields_and_no_primary_key(string xml)
    {
        Assert.Empty(FormDefinition.Parse(xml));
        Assert.Null(FormDefinition.PrimaryKeyColumn(xml));
    }

    [Fact]
    public void Attachment_group_and_file_fields_keep_their_type()
    {
        const string xml = """
            <form version="2">
              <field column="Images" columntype="docattachments" guid="44444444-0000-4000-8000-000000000001" />
              <field column="Teaser" columntype="file" guid="44444444-0000-4000-8000-000000000002" />
            </form>
            """;

        var fields = FormDefinition.Parse(xml);

        Assert.Equal(["docattachments", "file"], fields.Select(f => f.DataType));
    }
}
