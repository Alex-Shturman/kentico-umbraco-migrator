using System.Text.Json.Nodes;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;

namespace KenticoMigrator.Tests.Migration;

public sealed class RichTextRewriterTests
{
    // Keys from the verified fixture (research R6).
    private static readonly Guid PageKey = new("a2b7f372-080d-4a73-a0e0-94a9ca5569f8");
    private static readonly Guid FileKey = new("62bfc761-43a1-4c6f-99f6-dbd0490af0ca");
    private static readonly Guid ImageKey = new("f18ec404-bd9f-4142-87e0-542d360589a6");

    private static string FixtureMarkup() =>
        (string)JsonNode.Parse(File.ReadAllText(RepoPaths.Combine("tests", "KenticoMigrator.Tests", "Fixtures", "umbraco", "document-all-editors.json")))!
            ["values"]!.AsArray().Single(v => (string?)v!["alias"] == "body")!["value"]!["markup"]!;

    private static RichTextRewriter Create(Func<Guid, string?>? mediaUrl = null)
    {
        var page = new TargetDocument(PageKey, null, "contentPage", "About", "about", 1, true,
            new Dictionary<string, PlannedValue>(), "/About", ["/About", "/About.aspx"], null);
        var media = new[]
        {
            new TargetMedia(FileKey, null, MediaType.File, "file.pdf", null, "x/file.pdf"),
            new TargetMedia(ImageKey, null, MediaType.Image, "kayak-photo.png", null, "x/kayak-photo.png"),
        };
        var plan = new MigrationPlan([], [], [], media, [page], [], []);
        var index = UrlIndex.Create(plan, [], SourceFactory.SampleProfile());
        return new RichTextRewriter(index, mediaUrl);
    }

    [Fact]
    public void Page_link_becomes_local_link()
    {
        var result = Create().Rewrite("""<p>See <a href="/About" title="Target">about</a> and <a href="~/about.aspx">again</a>.</p>""");

        Assert.Equal(
            $$"""<p>See <a href="/{localLink:{{PageKey}}}" type="document" title="Target">about</a> and <a href="/{localLink:{{PageKey}}}" type="document">again</a>.</p>""",
            result.Html);
        Assert.Equal(2, result.Rewritten);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void Media_link_and_image_become_media_references()
    {
        var html = $"""<p><a href="~/getmedia/{FileKey}/file.pdf">file</a></p><p><img src="~/getattachment/{ImageKey}/kayak-photo.png" alt="Kayak"></p>""";

        var result = Create(key => key == ImageKey ? "/media/rmhmqr5w/kayak-photo.png" : null).Rewrite(html);

        Assert.Equal(
            $$"""<p><a href="/{localLink:{{FileKey}}}" type="media">file</a></p><p><img src="/media/rmhmqr5w/kayak-photo.png" alt="Kayak" data-udi="umb://media/f18ec404bd9f414287e0542d360589a6"></p>""",
            result.Html);
        Assert.Equal(2, result.Rewritten);
    }

    [Fact]
    public void Image_keeps_its_src_when_no_media_url_is_known()
    {
        var result = Create().Rewrite($"""<img src="/getattachment/{ImageKey}/kayak-photo.png">""");

        Assert.Equal($"""<img src="/getattachment/{ImageKey}/kayak-photo.png" data-udi="umb://media/f18ec404bd9f414287e0542d360589a6">""", result.Html);
        Assert.Equal(1, result.Rewritten);
    }

    [Fact]
    public void External_mail_anchor_application_unchanged_and_counted()
    {
        const string html = """<a href="https://example.test/page">e</a><a href="mailto:shop@kayaks.example">m</a><a href="#top">a</a><a href="/cart">c</a><a href="/Cart/Checkout.aspx">c2</a>""";

        var result = Create().Rewrite(html);

        Assert.Equal(html, result.Html);
        Assert.Equal(0, result.Rewritten);
        Assert.Equal(1, result.External);
        Assert.Equal(2, result.ToApplication);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void Unknown_internal_link_unchanged_and_listed()
    {
        const string html = """<a href="/no-such-page">x</a><a href="/no-such-page">y</a><img src="/images/logo.png">""";

        var result = Create().Rewrite(html);

        Assert.Equal(html, result.Html);
        Assert.Equal(["/no-such-page", "/images/logo.png"], result.Unresolved);
    }

    [Fact]
    public void Embedded_data_images_and_empty_links_are_ignored()
    {
        const string html = """<img src="data:image/png;base64,AAAA"><a href="">x</a><a name="anchor">y</a>""";

        var result = Create().Rewrite(html);

        Assert.Equal(html, result.Html);
        Assert.Empty(result.Unresolved);
        Assert.Equal(0, result.External);
    }

    [Fact]
    public void Rewriting_twice_is_idempotent()
    {
        var html = $"""<p><a href="/About">a</a> <a href="/getmedia/{FileKey}/f.pdf">f</a> <img src="/getattachment/{ImageKey}/k.png"> <a href="/zzz">z</a></p>""";
        var rewriter = Create(_ => "/media/x/k.png");

        var first = rewriter.Rewrite(html);
        var second = rewriter.Rewrite(first.Html);

        Assert.Equal(first.Html, second.Html);
        Assert.Equal(0, second.Rewritten);
        Assert.Equal(["/zzz"], second.Unresolved);
    }

    [Fact]
    public void Unrelated_markup_is_preserved()
    {
        // The fixture markup is already in the verified Umbraco form: nothing in it may change.
        var markup = FixtureMarkup();

        var result = Create().Rewrite(markup);

        Assert.Equal(markup, result.Html);
        Assert.Equal(0, result.Rewritten);
        Assert.Equal(1, result.External);

        const string plain = """<h2 class="x">Tom &amp; Jerry&nbsp;now</h2><ul><li>one</li><li>two<br>three</li></ul><table><tbody><tr><td>c</td></tr></tbody></table><!-- note --><p style="color:red">text</p>""";
        Assert.Equal(plain, Create().Rewrite(plain).Html);
    }

    [Fact]
    public void Old_form_of_the_fixture_markup_is_rewritten_to_the_verified_form()
    {
        var old =
            $"""<p>Intro with <a href="/About" title="Target">an internal link</a> and <a href="~/getmedia/{FileKey}/file.pdf">a media link</a>.</p><p><img src="~/getattachment/{ImageKey}/kayak-photo.png" alt="Kayak"></p><p><a href="https://example.test/page">An external link</a></p>""";

        var result = Create(key => key == ImageKey ? "/media/rmhmqr5w/kayak-photo.png" : null).Rewrite(old);

        Assert.Equal(FixtureMarkup(), result.Html);
        Assert.Equal(3, result.Rewritten);
    }

    [Fact]
    public void Empty_input_gives_empty_result()
    {
        var result = Create().Rewrite(string.Empty);

        Assert.Equal(string.Empty, result.Html);
        Assert.Equal(0, result.Rewritten);
    }

    [Fact]
    public void Fragment_and_query_of_a_rewritten_link_are_kept()
    {
        var html = $"""<a href="/About#team">t</a><a href="/about.aspx?tab=2#x">q</a><a href="/getmedia/{FileKey}/f.pdf?download=1">m</a>""";

        var result = Create().Rewrite(html);

        Assert.Equal(
            $$"""<a href="/{localLink:{{PageKey}}}#team" type="document">t</a><a href="/{localLink:{{PageKey}}}?tab=2#x" type="document">q</a><a href="/{localLink:{{FileKey}}}?download=1" type="media">m</a>""",
            result.Html);
        Assert.Equal(result.Html, Create().Rewrite(result.Html).Html);
    }
}
