using KenticoMigrator.Kentico;

namespace KenticoMigrator.Tests.Kentico;

public sealed class EditableRegionsTests
{
    private const string Template = """
        <page>
          <webpartzone id="zoneMain" v="1">
            <webpart controlid="intro" guid="aaaaaaaa-0000-4000-8000-000000000001" type="editabletext" v="1">
              <property name="regiontitle">Intro</property>
            </webpart>
            <webpart controlid="menu" guid="aaaaaaaa-0000-4000-8000-000000000002" type="repeater" v="1" />
          </webpartzone>
          <webpartzone id="zoneBottom" v="1">
            <webpart controlid="body" guid="aaaaaaaa-0000-4000-8000-000000000003" type="editabletext" v="1" />
          </webpartzone>
        </page>
        """;

    [Fact]
    public void Template_order_lists_only_editable_text_web_parts_in_order()
    {
        Assert.Equal(["intro", "body"], EditableRegions.TemplateOrder(Template));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Template_order_of_null_or_empty_is_empty(string? xml)
    {
        Assert.Empty(EditableRegions.TemplateOrder(xml));
    }

    [Fact]
    public void Returns_regions_in_template_order()
    {
        // The document stores body before intro; the template says intro comes first.
        const string content = """
            <content>
              <webpart id="body"><![CDATA[<p>Second</p>]]></webpart>
              <webpart id="intro"><![CDATA[<p>First</p>]]></webpart>
            </content>
            """;

        var regions = EditableRegions.Parse(content, ["intro", "body"]);

        Assert.Equal(["intro", "body"], regions.Select(r => r.Id));
        Assert.Equal(["<p>First</p>", "<p>Second</p>"], regions.Select(r => r.Html));
    }

    [Fact]
    public void Matches_ids_stored_as_control_id_and_guid()
    {
        const string content = """
            <content>
              <webpart id="body;aaaaaaaa-0000-4000-8000-000000000003"><![CDATA[B]]></webpart>
              <webpart id="intro;aaaaaaaa-0000-4000-8000-000000000001"><![CDATA[A]]></webpart>
            </content>
            """;

        var regions = EditableRegions.Parse(content, ["intro", "body"]);

        Assert.Equal(["A", "B"], regions.Select(r => r.Html));
    }

    [Fact]
    public void Unknown_regions_are_appended_in_document_order()
    {
        const string content = """
            <content>
              <webpart id="zeta"><![CDATA[Z]]></webpart>
              <webpart id="intro"><![CDATA[I]]></webpart>
              <webpart id="alpha"><![CDATA[A]]></webpart>
            </content>
            """;

        var regions = EditableRegions.Parse(content, ["intro"]);

        Assert.Equal(["intro", "zeta", "alpha"], regions.Select(r => r.Id));
    }

    [Fact]
    public void Html_is_decoded_exactly_once()
    {
        // Without CDATA the XML layer decodes one level: &amp;lt; becomes &lt; and stays that way.
        const string content = """
            <content>
              <webpart id="a">&lt;p&gt;Fish &amp;amp; chips &amp;lt;b&amp;gt;&lt;/p&gt;</webpart>
            </content>
            """;

        var region = Assert.Single(EditableRegions.Parse(content, []));

        Assert.Equal("<p>Fish &amp; chips &lt;b&gt;</p>", region.Html);
    }

    [Fact]
    public void Cdata_content_is_not_decoded()
    {
        const string content = "<content><webpart id=\"a\"><![CDATA[<p>Fish &amp; chips</p>]]></webpart></content>";

        var region = Assert.Single(EditableRegions.Parse(content, []));

        Assert.Equal("<p>Fish &amp; chips</p>", region.Html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<content />")]
    public void Null_or_empty_gives_empty_list(string? content)
    {
        Assert.Empty(EditableRegions.Parse(content, ["intro"]));
    }
}
