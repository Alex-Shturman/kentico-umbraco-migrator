using System.Globalization;
using System.Text.Json.Nodes;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.Umbraco;

public sealed class FingerprintTests
{
    private const string FixtureMarkup =
        "<p>Intro with <a href=\"/{localLink:a2b7f372-080d-4a73-a0e0-94a9ca5569f8}\" type=\"document\" title=\"Target\">an internal link</a> and <a href=\"/{localLink:62bfc761-43a1-4c6f-99f6-dbd0490af0ca}\" type=\"media\">a media link</a>.</p><p><img src=\"/media/rmhmqr5w/kayak-photo.png\" alt=\"Kayak\" data-udi=\"umb://media/f18ec404bd9f414287e0542d360589a6\"></p><p><a href=\"https://example.test/page\">An external link</a></p>";

    private static readonly Guid Parent = new("11111111-1111-1111-1111-111111111111");

    private static OwnedValues Owned(string name, params (string Alias, JsonNode? Value)[] values) =>
        new(name, Parent, values.ToDictionary(v => v.Alias, v => v.Value));

    private static JsonNode FixtureValues() =>
        JsonNode.Parse(File.ReadAllText(RepoPaths.Combine("tests", "KenticoMigrator.Tests", "Fixtures", "umbraco", "document-all-editors.json")))!["values"]!;

    [Fact]
    public void Hash_is_lowercase_sha256_hex()
    {
        var hash = Fingerprint.Compute(Owned("Page"));

        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Same_values_any_key_order_same_hash()
    {
        var first = Owned("Page", ("a", JsonValue.Create("x")), ("b", JsonValue.Create(1)));
        var second = Owned("Page", ("b", JsonValue.Create(1)), ("a", JsonValue.Create("x")));

        Assert.Equal(Fingerprint.Compute(first), Fingerprint.Compute(second));
    }

    [Fact]
    public void Nested_object_key_order_does_not_matter()
    {
        var first = Owned("Page", ("o", JsonNode.Parse("""{"a":1,"b":{"c":2,"d":3}}""")));
        var second = Owned("Page", ("o", JsonNode.Parse("""{"b":{"d":3,"c":2},"a":1}""")));

        Assert.Equal(Fingerprint.Compute(first), Fingerprint.Compute(second));
    }

    [Fact]
    public void Decimal_formatting_is_culture_invariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            var planned = Fingerprint.Compute(Owned("Page", ("price", JsonValue.Create(12.5m))));
            var fromApi = Fingerprint.Compute(Owned("Page", ("price", JsonNode.Parse("12.5"))));

            Assert.Equal(fromApi, planned);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Numbers_that_are_equal_hash_equal_whatever_their_spelling()
    {
        var seven = Fingerprint.Compute(Owned("Page", ("n", JsonValue.Create(7))));

        Assert.Equal(seven, Fingerprint.Compute(Owned("Page", ("n", JsonNode.Parse("7.0")))));
        Assert.Equal(seven, Fingerprint.Compute(Owned("Page", ("n", JsonValue.Create(7.00m)))));
        Assert.NotEqual(seven, Fingerprint.Compute(Owned("Page", ("n", JsonValue.Create(7.5m)))));
    }

    [Fact]
    public void Changed_value_name_or_parent_changes_hash()
    {
        var baseline = Fingerprint.Compute(Owned("Page", ("title", JsonValue.Create("A"))));

        Assert.NotEqual(baseline, Fingerprint.Compute(Owned("Page", ("title", JsonValue.Create("B")))));
        Assert.NotEqual(baseline, Fingerprint.Compute(Owned("Other", ("title", JsonValue.Create("A")))));
        Assert.NotEqual(baseline, Fingerprint.Compute(new OwnedValues("Page", null, new Dictionary<string, JsonNode?> { ["title"] = JsonValue.Create("A") })));
    }

    [Fact]
    public void Rich_text_compares_markup_only()
    {
        var sent = Owned("Page", ("body", JsonNode.Parse("""{"markup":"<p>x</p>","blocks":null}""")));
        var read = Owned("Page", ("body", JsonNode.Parse("""{"markup":"<p>x</p>","blocks":{"layout":{},"contentData":[],"settingsData":[],"expose":[]}}""")));
        var edited = Owned("Page", ("body", JsonNode.Parse("""{"markup":"<p>y</p>","blocks":null}""")));

        Assert.Equal(Fingerprint.Compute(sent), Fingerprint.Compute(read));
        Assert.NotEqual(Fingerprint.Compute(sent), Fingerprint.Compute(edited));
    }

    [Fact]
    public void Media_picker_ignores_server_added_fields_but_keeps_both_keys()
    {
        var sent = Owned("Page", ("hero", JsonNode.Parse("""[{"key":"0701c4de-007e-4e8a-bc3d-cff0e727558a","mediaKey":"f18ec404-bd9f-4142-87e0-542d360589a6"}]""")));
        var read = Owned("Page", ("hero", JsonNode.Parse("""[{"key":"0701c4de-007e-4e8a-bc3d-cff0e727558a","mediaKey":"f18ec404-bd9f-4142-87e0-542d360589a6","mediaTypeAlias":"Image","crops":[],"focalPoint":null}]""")));
        var otherKey = Owned("Page", ("hero", JsonNode.Parse("""[{"key":"c687577d-6eb5-4819-bd0d-b9a01b097f5e","mediaKey":"f18ec404-bd9f-4142-87e0-542d360589a6"}]""")));
        var otherMedia = Owned("Page", ("hero", JsonNode.Parse("""[{"key":"0701c4de-007e-4e8a-bc3d-cff0e727558a","mediaKey":"62bfc761-43a1-4c6f-99f6-dbd0490af0ca"}]""")));

        Assert.Equal(Fingerprint.Compute(sent), Fingerprint.Compute(read));
        Assert.NotEqual(Fingerprint.Compute(sent), Fingerprint.Compute(otherKey));
        Assert.NotEqual(Fingerprint.Compute(sent), Fingerprint.Compute(otherMedia));
    }

    [Fact]
    public void Media_picker_order_matters()
    {
        var one = Owned("Page", ("g", JsonNode.Parse("""[{"key":"a","mediaKey":"b"},{"key":"c","mediaKey":"d"}]""")));
        var two = Owned("Page", ("g", JsonNode.Parse("""[{"key":"c","mediaKey":"d"},{"key":"a","mediaKey":"b"}]""")));

        Assert.NotEqual(Fingerprint.Compute(one), Fingerprint.Compute(two));
    }

    [Theory]
    [InlineData("2026-03-01T10:30:00")]
    [InlineData("2026-03-01 10:30:00")]
    [InlineData("2026-03-01T10:30")]
    [InlineData("2026-03-01T10:30:00.000")]
    public void Dates_normalise_to_the_space_form(string written)
    {
        var expected = Fingerprint.Compute(Owned("Page", ("d", JsonValue.Create("2026-03-01 10:30:00"))));

        Assert.Equal(expected, Fingerprint.Compute(Owned("Page", ("d", JsonValue.Create(written)))));
    }

    [Fact]
    public void Other_text_is_not_rewritten()
    {
        Assert.NotEqual(
            Fingerprint.Compute(Owned("Page", ("t", JsonValue.Create("2026-03-01 10:30:00 and more")))),
            Fingerprint.Compute(Owned("Page", ("t", JsonValue.Create("2026-03-01T10:30:00 and more")))));
    }

    [Fact]
    public void Dropdown_is_an_array_with_one_string_and_differs_from_the_bare_string()
    {
        var array = Fingerprint.Compute(Owned("Page", ("k", JsonNode.Parse("""["Sea kayak"]"""))));

        Assert.NotEqual(array, Fingerprint.Compute(Owned("Page", ("k", JsonValue.Create("Sea kayak")))));
    }

    [Fact]
    public void Empty_values_equal_missing_values()
    {
        var missing = Fingerprint.Compute(Owned("Page"));

        Assert.Equal(missing, Fingerprint.Compute(Owned("Page", ("a", null), ("b", JsonValue.Create("")), ("c", JsonNode.Parse("[]")), ("d", JsonNode.Parse("""{"markup":"","blocks":null}""")))));
    }

    [Fact]
    public void Fingerprint_property_is_not_part_of_the_fingerprint()
    {
        var plain = Owned("Page", ("title", JsonValue.Create("A")));
        var stamped = Owned("Page", ("title", JsonValue.Create("A")), (PlanAliases.Fingerprint, JsonValue.Create("abc")));

        Assert.Equal(Fingerprint.Compute(plain), Fingerprint.Compute(stamped));
    }

    [Fact]
    public void Planned_and_current_shapes_hash_equal_for_same_content()
    {
        var mediaA = "f18ec404-bd9f-4142-87e0-542d360589a6";
        var mediaB = "62bfc761-43a1-4c6f-99f6-dbd0490af0ca";
        var planned = new OwnedValues("Fixture page", Parent, new Dictionary<string, JsonNode?>
        {
            ["umbracoUrlName"] = JsonValue.Create("fixture-page"),
            ["title"] = JsonValue.Create("Fixture page title"),
            ["teaser"] = JsonValue.Create("Line one of the teaser.\nLine two of the teaser."),
            ["body"] = new JsonObject { ["markup"] = FixtureMarkup, ["blocks"] = null },
            ["count"] = JsonValue.Create(42),
            ["price"] = JsonValue.Create(12.5m),
            ["flag"] = JsonValue.Create(true),
            ["published"] = JsonValue.Create("2026-03-01T10:30:00"),
            ["tags"] = new JsonArray("kayak", "touring"),
            ["kind"] = new JsonArray("Sea kayak"),
            ["heroImage"] = JsonNode.Parse($$"""[{"key":"0701c4de-007e-4e8a-bc3d-cff0e727558a","mediaKey":"{{mediaA}}"}]"""),
            ["gallery"] = JsonNode.Parse($$"""[{"key":"c687577d-6eb5-4819-bd0d-b9a01b097f5e","mediaKey":"{{mediaA}}"},{"key":"33adcea6-e4bb-4d9b-b78c-283584c9e5a5","mediaKey":"{{mediaB}}"}]"""),
            [PlanAliases.SourceKey] = JsonValue.Create("not-in-fixture"),
        });

        var owned = planned.Values.Keys.Where(a => a != PlanAliases.SourceKey).ToList();
        var current = Fingerprint.FromCurrent("Fixture page", Parent, FixtureValues(), owned);
        var plannedWithoutSourceKey = planned with { Values = planned.Values.Where(v => v.Key != PlanAliases.SourceKey).ToDictionary(v => v.Key, v => v.Value) };

        Assert.Equal(Fingerprint.Compute(plannedWithoutSourceKey), Fingerprint.Compute(current));
    }

    [Fact]
    public void Current_values_outside_the_owned_aliases_are_ignored()
    {
        var all = Fingerprint.FromCurrent("P", Parent, FixtureValues(), ["title"]);
        var onlyTitle = Owned("P", ("title", JsonValue.Create("Fixture page title")));

        Assert.Equal(Fingerprint.Compute(onlyTitle), Fingerprint.Compute(all));
    }

    [Fact]
    public void Media_fingerprint_follows_name_parent_and_type()
    {
        var folder = new Guid("22222222-2222-2222-2222-222222222222");
        var media = new TargetMedia(new Guid("33333333-3333-3333-3333-333333333333"), folder, MediaType.Image, "a.jpg", null, "x/a.jpg");

        var hash = Fingerprint.Compute(media);

        Assert.Equal(hash, Fingerprint.Compute(OwnedValues.ForMedia("a.jpg", folder, "Image")));
        Assert.NotEqual(hash, Fingerprint.Compute(media with { Name = "b.jpg" }));
        Assert.NotEqual(hash, Fingerprint.Compute(media with { MediaType = MediaType.File }));
        Assert.NotEqual(hash, Fingerprint.Compute(media with { ParentKey = null }));
    }

    [Fact]
    public void Picker_item_key_is_stable_and_depends_on_all_parts()
    {
        var owner = Parent;
        var media = new Guid("f18ec404-bd9f-4142-87e0-542d360589a6");

        Assert.Equal(Fingerprint.PickerItemKey(owner, "hero", media), Fingerprint.PickerItemKey(owner, "hero", media));
        Assert.NotEqual(Fingerprint.PickerItemKey(owner, "hero", media), Fingerprint.PickerItemKey(owner, "gallery", media));
        Assert.NotEqual(Fingerprint.PickerItemKey(owner, "hero", media), Fingerprint.PickerItemKey(Guid.NewGuid(), "hero", media));
    }
}
