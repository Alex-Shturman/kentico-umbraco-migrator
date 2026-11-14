using KenticoMigrator.Migration;

namespace KenticoMigrator.Tests.Migration;

public sealed class SiteProfileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("profile-tests").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "profile.json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string ValidJson = """
        {
          "name": "sample",
          "ownHosts": ["kayaks.example"],
          "mediaRoot": "sample/media",
          "excludedPathPrefixes": ["/Account"],
          "folderPaths": ["/Catalog"],
          "applicationRoutePrefixes": ["/cart"],
          "notUploadedPaths": ["/Files/feed"],
          "types": [
            {
              "kenticoClass": "CMS.MenuItem",
              "targetAlias": "contentPage",
              "targetName": "Content page",
              "hasTemplate": true,
              "allowedChildren": ["contentPage"],
              "fields": [
                { "source": "doc:DocumentName", "target": "title", "editor": "TextBox", "group": "Content" },
                { "source": "region:first", "target": "bodyText", "editor": "RichText", "group": "Content" }
              ]
            },
            {
              "kenticoClass": "CMS.MenuItem",
              "pathOverride": "/Articles",
              "targetAlias": "articleList",
              "targetName": "Article list",
              "hasTemplate": true,
              "allowedChildren": [],
              "fields": []
            }
          ],
          "reparent": [
            { "kenticoClass": "DEMO.Brand", "newParentPath": "/brands", "urlNameFrom": "NodeAlias", "oldRoutePattern": "/brand-search/{Name}" }
          ],
          "unlinkedRows": [
            { "kenticoClass": "DEMO.Brand", "nameField": "Name", "parentPath": "/brands", "publish": false }
          ]
        }
        """;

    [Fact]
    public void Loads_valid_profile()
    {
        var profile = SiteProfile.Load(Write(ValidJson));

        Assert.Equal("sample", profile.Name);
        Assert.Equal(["kayaks.example"], profile.OwnHosts);
        Assert.Equal("sample/media", profile.MediaRoot);
        Assert.Equal(["/Account"], profile.ExcludedPathPrefixes);
        Assert.Equal(["/Catalog"], profile.FolderPaths);
        Assert.Equal(["/cart"], profile.ApplicationRoutePrefixes);
        Assert.Equal(["/Files/feed"], profile.NotUploadedPaths);

        Assert.Equal(2, profile.Types.Count);
        var page = profile.Types[0];
        Assert.Equal("CMS.MenuItem", page.KenticoClass);
        Assert.Equal("contentPage", page.TargetAlias);
        Assert.True(page.HasTemplate);
        Assert.Null(page.PathOverride);
        Assert.Equal(new FieldMapping("region:first", "bodyText", "RichText", "Content"), page.Fields[1]);
        Assert.Equal("/Articles", profile.Types[1].PathOverride);

        var rule = Assert.Single(profile.Reparent);
        Assert.Equal(new ReparentRule("DEMO.Brand", "/brands", "NodeAlias", "/brand-search/{Name}"), rule);
        Assert.Equal(new UnlinkedRowRule("DEMO.Brand", "Name", "/brands", false), Assert.Single(profile.UnlinkedRows));
    }

    [Fact]
    public void Missing_optional_sections_become_empty()
    {
        var profile = SiteProfile.Load(Write("""{ "name": "tiny" }"""));

        Assert.Empty(profile.OwnHosts);
        Assert.Empty(profile.Types);
        Assert.Empty(profile.Reparent);
        Assert.Null(profile.MediaRoot);
    }

    [Fact]
    public void Rejects_unknown_editor()
    {
        var json = ValidJson.Replace("\"RichText\"", "\"Wysiwyg\"");

        var ex = Assert.Throws<InvalidDataException>(() => SiteProfile.Load(Write(json)));

        Assert.Contains("Wysiwyg", ex.Message);
    }

    [Fact]
    public void Rejects_duplicate_target_alias()
    {
        var json = ValidJson.Replace("\"target\": \"bodyText\"", "\"target\": \"title\"");

        var ex = Assert.Throws<InvalidDataException>(() => SiteProfile.Load(Write(json)));

        Assert.Contains("title", ex.Message);
        Assert.Contains("contentPage", ex.Message);
    }

    [Fact]
    public void Accepts_jsonc_comments()
    {
        var json = "// leading comment\n" + ValidJson.Replace("\"name\": \"sample\",", "\"name\": \"sample\", /* inline */");

        var profile = SiteProfile.Load(Write(json));

        Assert.Equal("sample", profile.Name);
    }

    [Fact]
    public void Rejects_missing_target()
    {
        var json = ValidJson.Replace("\"target\": \"bodyText\", ", "");

        var ex = Assert.Throws<InvalidDataException>(() => SiteProfile.Load(Write(json)));

        Assert.Contains("target", ex.Message);
        Assert.Contains("contentPage", ex.Message);
    }

    [Fact]
    public void Rejects_null_list_element()
    {
        var json = ValidJson.Replace("\"fields\": []", "\"fields\": [null]");

        var ex = Assert.Throws<InvalidDataException>(() => SiteProfile.Load(Write(json)));

        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void Rejects_missing_name()
    {
        var ex = Assert.Throws<InvalidDataException>(() => SiteProfile.Load(Write("{}")));

        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Rejects_missing_kentico_class()
    {
        var json = ValidJson.Replace("\"kenticoClass\": \"DEMO.Brand\", \"nameField\"", "\"nameField\"");

        var ex = Assert.Throws<InvalidDataException>(() => SiteProfile.Load(Write(json)));

        Assert.Contains("kenticoClass", ex.Message);
    }

    [Fact]
    public void Rejects_missing_file()
    {
        var path = Path.Combine(_dir, "nope.json");

        var ex = Assert.Throws<InvalidDataException>(() => SiteProfile.Load(path));

        Assert.Contains("nope.json", ex.Message);
    }

    [Fact]
    public void Rejects_unreadable_path()
    {
        // A directory cannot be read as a file.
        Assert.Throws<InvalidDataException>(() => SiteProfile.Load(_dir));
    }

    [Fact]
    public void Placements_and_settings_folder_are_loaded_and_validated()
    {
        var json = ValidJson.Replace("\"types\":", "\"settingsFolder\": { \"path\": \"/Settings\", \"name\": \"Settings\" }, \"placements\": [ { \"path\": \"/Catalog\", \"newParentPath\": \"/\", \"urlName\": \"shop\" } ], \"types\":");

        var profile = SiteProfile.Load(Write(json));

        Assert.Equal(new SettingsFolderRule("/Settings", "Settings"), profile.SettingsFolder);
        Assert.Equal(new PlacementRule("/Catalog", "/", "shop"), Assert.Single(profile.Placements));
        Assert.Empty(SiteProfile.Load(Write(ValidJson)).Placements);
        Assert.Null(SiteProfile.Load(Write(ValidJson)).SettingsFolder);

        var missing = ValidJson.Replace("\"types\":", "\"placements\": [ { \"path\": \"/Catalog\" } ], \"types\":");
        Assert.Contains("newParentPath", Assert.Throws<InvalidDataException>(() => SiteProfile.Load(Write(missing))).Message);
    }

    [Theory]
    [InlineData("Settings")]
    [InlineData("/")]
    [InlineData("/Settings/Sub")]
    [InlineData("/Set tings")]
    [InlineData("//Settings")]
    public void Rejects_a_settings_folder_path_that_is_not_one_slash_and_one_segment(string path)
    {
        var json = ValidJson.Replace("\"types\":", $"\"settingsFolder\": {{ \"path\": \"{path}\", \"name\": \"Settings\" }}, \"types\":");

        var ex = Assert.Throws<InvalidDataException>(() => SiteProfile.Load(Write(json)));

        Assert.Contains("settingsFolder", ex.Message);
    }

    [Fact]
    public void Placements_are_empty_when_a_profile_is_built_in_code()
    {
        var profile = new SiteProfile("x", [], [], [], [], [], [], [], [], null);

        Assert.Empty(profile.Placements);
    }

    [Fact]
    public void Rejects_an_unknown_key_and_names_it()
    {
        var json = ValidJson.Replace("\"name\": \"sample\",", "\"name\": \"sample\", \"ownHost\": [],");

        var ex = Assert.Throws<InvalidDataException>(() => SiteProfile.Load(Write(json)));

        Assert.Contains("ownHost", ex.Message);
    }

    [Fact]
    public void Rejects_an_unknown_key_in_a_nested_rule()
    {
        var json = ValidJson.Replace("\"publish\": false", "\"publish\": false, \"pubIish\": true");

        var ex = Assert.Throws<InvalidDataException>(() => SiteProfile.Load(Write(json)));

        Assert.Contains("pubIish", ex.Message);
    }

    [Fact]
    public void Every_profile_in_the_repository_loads()
    {
        var files = Directory.GetFiles(Support.RepoPaths.Combine("profiles"), "*.json");

        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.NotNull(SiteProfile.Load(file));
        }
    }
}
