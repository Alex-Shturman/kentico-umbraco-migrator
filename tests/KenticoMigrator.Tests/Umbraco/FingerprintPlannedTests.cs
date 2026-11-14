using System.Text.Json.Nodes;
using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;
using KenticoMigrator.Umbraco;

namespace KenticoMigrator.Tests.Umbraco;

/// <summary>The planned side of the fingerprint and the Management API write shape (OwnedValues.FromPlanned).</summary>
public sealed class FingerprintPlannedTests
{
    private const string Markup =
        "<p>Intro with <a href=\"/{localLink:a2b7f372-080d-4a73-a0e0-94a9ca5569f8}\" type=\"document\" title=\"Target\">an internal link</a> and <a href=\"/{localLink:62bfc761-43a1-4c6f-99f6-dbd0490af0ca}\" type=\"media\">a media link</a>.</p><p><img src=\"/media/rmhmqr5w/kayak-photo.png\" alt=\"Kayak\" data-udi=\"umb://media/f18ec404bd9f414287e0542d360589a6\"></p><p><a href=\"https://example.test/page\">An external link</a></p>";

    private static readonly Guid Parent = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MediaA = new("f18ec404-bd9f-4142-87e0-542d360589a6");
    private static readonly Guid MediaB = new("62bfc761-43a1-4c6f-99f6-dbd0490af0ca");
    private static readonly Guid DocKey = new("a497af8c-9481-4145-a80a-caeba50ac98b");

    private static TargetDocument Document(Dictionary<string, PlannedValue> values) =>
        new(DocKey, Parent, "contentPage", "Fixture page", "fixture-page", 1, true, values, "/Fixture", [], null);

    private static TargetDocument AllEditorsDocument() => Document(new Dictionary<string, PlannedValue>
    {
        ["title"] = new("TextBox", "Fixture page title"),
        ["teaser"] = new("TextArea", "Line one of the teaser.\nLine two of the teaser."),
        ["body"] = new("RichText", Markup),
        ["count"] = new("Integer", 42),
        ["price"] = new("Decimal", 12.5m),
        ["flag"] = new("TrueFalse", true),
        ["published"] = new("DateTime", new DateTime(2026, 3, 1, 10, 30, 0, DateTimeKind.Unspecified)),
        ["tags"] = new("Tags", new List<string> { "kayak", "touring" }),
        ["kind"] = new("DropDown", "Sea kayak"),
        ["heroImage"] = new("MediaSingle", MediaA),
        ["gallery"] = new("MediaMultiple", new[] { MediaA, MediaB }),
    });

    private static JsonNode FixtureWithDerivedPickerKeys()
    {
        var path = RepoPaths.Combine("tests", "KenticoMigrator.Tests", "Fixtures", "umbraco", "document-all-editors.json");
        var values = JsonNode.Parse(File.ReadAllText(path))!["values"]!;
        foreach (var entry in values.AsArray().OfType<JsonObject>())
        {
            var alias = (string?)entry["alias"];
            if (alias is "heroImage" or "gallery" && entry["value"] is JsonArray items)
            {
                for (var i = 0; i < items.Count; i++)
                {
                    var item = items[i]!.AsObject();
                    item["key"] = Fingerprint.PickerItemKey(DocKey, alias!, i, Guid.Parse((string)item["mediaKey"]!)).ToString("D");
                }
            }
        }

        return values;
    }

    [Fact]
    public void Planned_document_hashes_equal_the_fixture_it_was_modelled_on()
    {
        var planned = OwnedValues.FromPlanned(AllEditorsDocument());

        var current = Fingerprint.FromCurrent("Fixture page", Parent, FixtureWithDerivedPickerKeys(), planned.Values.Keys.ToList());

        Assert.Equal(Fingerprint.Compute(current), Fingerprint.Compute(planned));
    }

    [Fact]
    public void Write_shape_matches_the_shapes_verified_in_the_fixture()
    {
        var body = OwnedValues.FromPlanned(AllEditorsDocument()).ToApiValues();
        JsonNode? Value(string alias) => body.OfType<JsonObject>().Single(e => (string?)e["alias"] == alias)["value"];

        Assert.All(body.OfType<JsonObject>(), e =>
        {
            Assert.Equal(["alias", "culture", "segment", "value"], e.Select(p => p.Key).Order().ToList());
            Assert.Null(e["culture"]);
            Assert.Null(e["segment"]);
        });
        Assert.Equal("fixture-page", (string?)Value("umbracoUrlName"));
        Assert.Equal(Markup, (string?)Value("body")!["markup"]);
        Assert.True(Value("body")!.AsObject().ContainsKey("blocks"));
        Assert.Null(Value("body")!["blocks"]);
        Assert.Equal(42L, (long)Value("count")!);
        Assert.Equal(12.5m, (decimal)Value("price")!);
        Assert.True((bool)Value("flag")!);
        Assert.Equal("2026-03-01 10:30:00", (string?)Value("published"));
        Assert.Equal(["kayak", "touring"], Value("tags")!.AsArray().Select(n => (string?)n));
        Assert.Equal(["Sea kayak"], Value("kind")!.AsArray().Select(n => (string?)n));

        var hero = Assert.Single(Value("heroImage")!.AsArray())!.AsObject();
        Assert.Equal(["key", "mediaKey"], hero.Select(p => p.Key).Order().ToList());
        Assert.Equal(MediaA.ToString("D"), (string?)hero["mediaKey"]);
        Assert.Equal(Fingerprint.PickerItemKey(DocKey, "heroImage", 0, MediaA).ToString("D"), (string?)hero["key"]);
        Assert.Equal(2, Value("gallery")!.AsArray().Count);
    }

    [Fact]
    public void Same_media_twice_in_one_picker_gets_two_keys()
    {
        var doc = Document(new Dictionary<string, PlannedValue> { ["gallery"] = new("MediaMultiple", new[] { MediaA, MediaA }) });

        var items = OwnedValues.FromPlanned(doc).Values["gallery"]!.AsArray();

        Assert.NotEqual((string?)items[0]!["key"], (string?)items[1]!["key"]);
    }

    [Fact]
    public void Owned_aliases_include_empty_planned_values_and_the_write_body_clears_them()
    {
        var doc = Document(new Dictionary<string, PlannedValue>
        {
            ["title"] = new("TextBox", null),
            ["gallery"] = new("MediaMultiple", Array.Empty<Guid>()),
            ["hero"] = new("MediaSingle", null),
            ["body"] = new("RichText", null),
        });

        var owned = OwnedValues.FromPlanned(doc);

        Assert.Equal(["body", "gallery", "hero", "title", "umbracoUrlName"], owned.Values.Keys.Order().ToList());
        Assert.All(
            owned.ToApiValues().OfType<JsonObject>().Where(e => (string?)e["alias"] != "umbracoUrlName"),
            e => Assert.Null(e["value"]));
        var onlyUrlName = new OwnedValues("Fixture page", Parent, new Dictionary<string, JsonNode?> { ["umbracoUrlName"] = JsonValue.Create("fixture-page") });
        Assert.Equal(Fingerprint.Compute(onlyUrlName), Fingerprint.Compute(owned));
    }

    [Fact]
    public void Rich_text_goes_through_the_rewrite_delegate_and_the_stamp_is_added_to_the_body()
    {
        var doc = Document(new Dictionary<string, PlannedValue> { ["body"] = new("RichText", "<p>old</p>") });

        var owned = OwnedValues.FromPlanned(doc, html => html.Replace("old", "new", StringComparison.Ordinal));

        Assert.Equal("<p>new</p>", (string?)owned.Values["body"]!["markup"]);
        var stamp = owned.ToApiValues("stamp").OfType<JsonObject>().Single(e => (string?)e["alias"] == PlanAliases.Fingerprint);
        Assert.Equal("stamp", (string?)stamp["value"]);
        Assert.DoesNotContain(PlanAliases.Fingerprint, owned.Values.Keys);
    }

    [Fact]
    public void Planned_dates_are_written_as_the_wall_clock_value_without_timezone_conversion()
    {
        // The tool writes the Kentico value as it is: a UTC-kind and a local-kind DateTime with the same wall clock
        // give the same text and the same hash as Umbraco's space form.
        OwnedValues Planned(DateTimeKind kind) => OwnedValues.FromPlanned(Document(new Dictionary<string, PlannedValue>
        {
            ["published"] = new("DateTime", new DateTime(2026, 3, 1, 10, 30, 0, kind)),
        }));

        var utc = Planned(DateTimeKind.Utc);

        Assert.Equal("2026-03-01 10:30:00", (string?)utc.Values["published"]);
        Assert.Equal(Fingerprint.Compute(Planned(DateTimeKind.Local)), Fingerprint.Compute(utc));
        var spaceForm = new OwnedValues("Fixture page", Parent, new Dictionary<string, JsonNode?>
        {
            ["umbracoUrlName"] = JsonValue.Create("fixture-page"),
            ["published"] = JsonValue.Create("2026-03-01 10:30:00"),
        });
        Assert.Equal(Fingerprint.Compute(spaceForm), Fingerprint.Compute(utc));
    }

    [Fact]
    public void A_plan_built_document_hashes_equal_a_matching_current_shape()
    {
        var imageA = SourceFactory.GuidFor(1001);
        var imageB = SourceFactory.GuidFor(1002);
        var site = SourceFactory.SampleSite() with
        {
            Attachments =
            [
                SourceFactory.Attachment(imageA, 9, "a.jpg", "Images", order: 1),
                SourceFactory.Attachment(imageB, 9, "b.jpg", "Images", order: 2),
            ],
        };
        var plan = PlanBuilder.Build(site, SourceFactory.SampleProfile());
        var product = plan.Documents.Single(d => d.OldPath == "/Catalog/Products/Tandem");

        var planned = OwnedValues.FromPlanned(product);

        // What Umbraco returns for that product: only non-empty values, server-added fields on the pickers.
        var currentJson = $$"""
            [
              {"editorAlias":"Umbraco.TextBox","culture":null,"segment":null,"alias":"umbracoUrlName","value":"{{product.UrlName}}"},
              {"editorAlias":"Umbraco.TrueFalse","culture":null,"segment":null,"alias":"umbracoNaviHide","value":false},
              {"editorAlias":"Umbraco.TrueFalse","culture":null,"segment":null,"alias":"hideFromSitemap","value":false},
              {"editorAlias":"Umbraco.TextBox","culture":null,"segment":null,"alias":"migrationSourceKey","value":"{{product.Key}}"},
              {"editorAlias":"Umbraco.TextBox","culture":null,"segment":null,"alias":"title","value":"Tandem"},
              {"editorAlias":"Umbraco.TrueFalse","culture":null,"segment":null,"alias":"featured","value":true},
              {"editorAlias":"Umbraco.MediaPicker3","culture":null,"segment":null,"alias":"images","value":[
                {"key":"{{Fingerprint.PickerItemKey(product.Key, "images", 0, imageA)}}","mediaKey":"{{imageA}}","mediaTypeAlias":"Image","crops":[],"focalPoint":null},
                {"key":"{{Fingerprint.PickerItemKey(product.Key, "images", 1, imageB)}}","mediaKey":"{{imageB}}","mediaTypeAlias":"Image","crops":[],"focalPoint":null}]},
              {"editorAlias":"Umbraco.DropDown.Flexible","culture":null,"segment":null,"alias":"shippingImage","value":["express"]},
              {"editorAlias":"Umbraco.TextBox","culture":null,"segment":null,"alias":"sku","value":"K-100"},
              {"editorAlias":"Umbraco.Decimal","culture":null,"segment":null,"alias":"price","value":999.5}
            ]
            """;
        var aliases = planned.Values.Keys.ToList();

        var read = Fingerprint.FromCurrent(product.Name, product.ParentKey, JsonNode.Parse(currentJson), aliases);
        var edited = Fingerprint.FromCurrent(product.Name, product.ParentKey, JsonNode.Parse(currentJson.Replace("K-100", "K-999", StringComparison.Ordinal)), aliases);

        Assert.Equal(Fingerprint.Compute(read), Fingerprint.Compute(planned));
        Assert.NotEqual(Fingerprint.Compute(planned), Fingerprint.Compute(edited));
    }

    [Fact]
    public void Every_owned_alias_of_every_planned_document_is_a_property_of_its_type_or_compositions()
    {
        var site = SourceFactory.SampleSite() with
        {
            Attachments = [SourceFactory.Attachment(SourceFactory.GuidFor(1001), 9, "a.jpg", "Images", order: 1)],
            UnlinkedRows = new Dictionary<string, IReadOnlyList<SourceRow>>
            {
                ["DEMO.Brand"] = [new SourceRow(99, SourceFactory.Fields(("Name", "Ghost")))],
            },
        };
        var plan = PlanBuilder.Build(site, SourceFactory.SampleProfile());
        Assert.Contains(plan.Documents, d => d.TypeAlias == "contentFolder");

        foreach (var doc in plan.Documents)
        {
            var type = plan.DocumentTypes.Single(t => t.Alias == doc.TypeAlias);
            var defined = type.Properties.Concat(type.Compositions.SelectMany(c => plan.DocumentTypes.Single(t => t.Alias == c).Properties))
                .Select(p => p.Alias)
                .ToHashSet();

            foreach (var alias in OwnedValues.FromPlanned(doc).Values.Keys)
            {
                Assert.True(defined.Contains(alias), $"{doc.OldPath}: '{alias}' is not a property of '{doc.TypeAlias}'");
            }
        }
    }

    [Fact]
    public void Dropdown_choice_that_is_not_text_is_written_in_invariant_culture()
    {
        var doc = Document(new Dictionary<string, PlannedValue> { ["kind"] = new("DropDown", 3) });

        Assert.Equal(["3"], OwnedValues.FromPlanned(doc).Values["kind"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public void Integer_with_a_fractional_part_is_refused_not_rounded()
    {
        var fractional = Document(new Dictionary<string, PlannedValue> { ["count"] = new("Integer", 1.5m) });
        var whole = Document(new Dictionary<string, PlannedValue> { ["count"] = new("Integer", 2.0m) });

        var ex = Assert.Throws<InvalidDataException>(() => OwnedValues.FromPlanned(fractional));

        Assert.Contains("count", ex.Message);
        Assert.DoesNotContain("1.5", ex.Message);
        Assert.Equal(2L, (long)OwnedValues.FromPlanned(whole).Values["count"]!);
    }

    [Theory]
    [InlineData(2147483648L)]
    [InlineData(-2147483649L)]
    [InlineData(long.MaxValue)]
    [InlineData(1e30)]
    public void Integer_outside_the_int32_range_is_refused_naming_the_property_not_the_value(object value)
    {
        var doc = Document(new Dictionary<string, PlannedValue> { ["count"] = new("Integer", value) });

        var ex = Assert.Throws<InvalidDataException>(() => OwnedValues.FromPlanned(doc));

        Assert.Contains("count", ex.Message);
        Assert.DoesNotContain("2147483648", ex.Message);
    }

    [Fact]
    public void Integer_at_the_int32_limits_is_written()
    {
        var doc = Document(new Dictionary<string, PlannedValue>
        {
            ["max"] = new("Integer", int.MaxValue),
            ["min"] = new("Integer", int.MinValue),
        });

        var values = OwnedValues.FromPlanned(doc).Values;

        Assert.Equal((long)int.MaxValue, (long)values["max"]!);
        Assert.Equal((long)int.MinValue, (long)values["min"]!);
    }
}
