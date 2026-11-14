namespace KenticoMigrator.Migration;

/// <summary>Fixed aliases of the types the tool creates itself (MAPPING.md 2.1).</summary>
internal static class PlanAliases
{
    public const string Folder = "folder";
    public const string Seo = "seoComposition";
    public const string Navigation = "navigationComposition";
    public const string MigrationComposition = "migrationComposition";
    public const string Layout = "migratedLayout";
    public const string PageTemplate = "migratedPage";

    public const string MetaTitle = "metaTitle";
    public const string MetaDescription = "metaDescription";
    public const string MetaKeywords = "metaKeywords";
    public const string NaviHide = "umbracoNaviHide";
    public const string HideFromSitemap = "hideFromSitemap";
    public const string SourceKey = "migrationSourceKey";
    public const string Fingerprint = "migrationFingerprint";

    public const string DefaultGroup = "Content";
}

/// <summary>
/// Umbraco side of each profile editor. Names, aliases and UI aliases of the built-in data types are
/// verified against a running Umbraco 17 host by research R7; change them here only.
/// </summary>
internal sealed record EditorSpec(string DataTypeName, string EditorAlias, string EditorUiAlias, bool IsBuiltIn);

internal static class EditorCatalog
{
    public const string Dropdown = "DropDown";

    private static readonly Dictionary<string, EditorSpec> Specs = new(StringComparer.Ordinal)
    {
        ["TextBox"] = new("Textstring", "Umbraco.TextBox", "Umb.PropertyEditorUi.TextBox", true),
        ["TextArea"] = new("Textarea", "Umbraco.TextArea", "Umb.PropertyEditorUi.TextArea", true),
        ["RichText"] = new("Richtext editor", "Umbraco.RichText", "Umb.PropertyEditorUi.Tiptap", true),
        ["Integer"] = new("Numeric", "Umbraco.Integer", "Umb.PropertyEditorUi.Integer", true),
        ["Decimal"] = new("Decimal", "Umbraco.Decimal", "Umb.PropertyEditorUi.Decimal", false),
        ["TrueFalse"] = new("True/false", "Umbraco.TrueFalse", "Umb.PropertyEditorUi.Toggle", true),
        ["DateTime"] = new("Date Picker with time", "Umbraco.DateTime", "Umb.PropertyEditorUi.DatePicker", true),
        ["Tags"] = new("Tags", "Umbraco.Tags", "Umb.PropertyEditorUi.Tags", true),
        ["MediaSingle"] = new("Image Media Picker", "Umbraco.MediaPicker3", "Umb.PropertyEditorUi.MediaPicker", true),
        ["MediaMultiple"] = new("Multiple Media Picker", "Umbraco.MediaPicker3", "Umb.PropertyEditorUi.MediaPicker", true),
        [Dropdown] = new("Dropdown", "Umbraco.DropDown.Flexible", "Umb.PropertyEditorUi.Dropdown", false),
    };

    public static EditorSpec Get(string editor) => Specs[editor];

    /// <summary>Key of a data type shared by all properties of one editor (built-ins: a placeholder resolved by name).</summary>
    public static Guid SharedKey(string editor) =>
        Keys.Derived("datatype", Specs[editor].IsBuiltIn ? $"builtin:{Specs[editor].DataTypeName}" : editor.ToLowerInvariant());
}

public sealed partial class PlanBuilder
{
    private static readonly (string Alias, string Name, string Editor, string Group)[] SeoProperties =
    [
        (PlanAliases.MetaTitle, "Meta title", "TextBox", "SEO"),
        (PlanAliases.MetaDescription, "Meta description", "TextArea", "SEO"),
        (PlanAliases.MetaKeywords, "Meta keywords", "TextBox", "SEO"),
    ];

    private static readonly (string Alias, string Name, string Editor, string Group)[] NavigationProperties =
    [
        (PlanAliases.NaviHide, "Hide in navigation", "TrueFalse", "Navigation"),
        (PlanAliases.HideFromSitemap, "Hide from sitemap", "TrueFalse", "Navigation"),
    ];

    private static readonly (string Alias, string Name, string Editor, string Group)[] MigrationProperties =
    [
        (PlanAliases.SourceKey, "Migration source key", "TextBox", "Migration"),
        (PlanAliases.Fingerprint, "Migration fingerprint", "TextBox", "Migration"),
    ];

    private static readonly string[] PageCompositions = [PlanAliases.Seo, PlanAliases.Navigation, PlanAliases.MigrationComposition];

    /// <summary>Aliases owned by compositions; a profile field that targets one writes the composition property.</summary>
    private static readonly HashSet<string> CompositionPropertyAliases = new(
        SeoProperties.Concat(NavigationProperties).Concat(MigrationProperties).Select(p => p.Alias),
        StringComparer.OrdinalIgnoreCase);

    private sealed record Schema(
        IReadOnlyList<TargetDataType> DataTypes,
        IReadOnlyList<TargetDocumentType> DocumentTypes,
        IReadOnlyList<TargetTemplate> Templates);

    private Schema BuildSchema()
    {
        var dataTypes = new Dictionary<Guid, TargetDataType>();

        Guid DataTypeFor(string editor, string typeAlias, string property, string? kenticoClass, string source)
        {
            var spec = EditorCatalog.Get(editor);
            if (editor == EditorCatalog.Dropdown)
            {
                var key = Keys.Derived("datatype", $"dropdown:{typeAlias}.{property}");
                dataTypes.TryAdd(key, new TargetDataType(
                    key,
                    $"Dropdown {typeAlias} {property}",
                    spec.EditorAlias,
                    spec.EditorUiAlias,
                    new Dictionary<string, object?>
                    {
                        ["items"] = DropdownOptions(kenticoClass, source),
                        ["multiple"] = false,
                    },
                    IsBuiltIn: false));
                return key;
            }

            var sharedKey = EditorCatalog.SharedKey(editor);
            dataTypes.TryAdd(sharedKey, new TargetDataType(
                sharedKey,
                spec.DataTypeName,
                spec.EditorAlias,
                spec.EditorUiAlias,
                editor == "MediaMultiple"
                    ? new Dictionary<string, object?> { ["multiple"] = true }
                    : new Dictionary<string, object?>(),
                spec.IsBuiltIn));
            return sharedKey;
        }

        TargetProperty[] CompositionProps((string Alias, string Name, string Editor, string Group)[] defs) =>
            [.. defs.Select(d => new TargetProperty(d.Alias, d.Name, DataTypeFor(d.Editor, "composition", d.Alias, null, d.Alias), d.Group, false))];

        var documentTypes = new List<TargetDocumentType>
        {
            CompositionType(PlanAliases.Seo, "SEO composition", CompositionProps(SeoProperties)),
            CompositionType(PlanAliases.Navigation, "Navigation composition", CompositionProps(NavigationProperties)),
            CompositionType(PlanAliases.MigrationComposition, "Migration composition", CompositionProps(MigrationProperties)),
        };

        var mapped = new List<TargetDocumentType>();
        var seenAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in _profile.Types)
        {
            if (!seenAliases.Add(mapping.TargetAlias))
            {
                Error("profile", $"Target alias '{mapping.TargetAlias}' is used by more than one type mapping.");
                continue;
            }

            var properties = mapping.Fields
                .Where(f => !CompositionPropertyAliases.Contains(f.TargetAlias))
                .Select(f => new TargetProperty(
                    f.TargetAlias,
                    HumanName(f.TargetAlias),
                    DataTypeFor(f.Editor, mapping.TargetAlias, f.TargetAlias, mapping.KenticoClass, f.Source),
                    string.IsNullOrWhiteSpace(f.Group) ? PlanAliases.DefaultGroup : f.Group,
                    Mandatory: false))
                .ToList();

            mapped.Add(new TargetDocumentType(
                Keys.Derived("doctype", mapping.TargetAlias),
                mapping.TargetAlias,
                mapping.TargetName,
                mapping.HasTemplate ? PlanAliases.PageTemplate : null,
                AllowedAtRoot: false,
                [.. mapping.AllowedChildren.Distinct(StringComparer.Ordinal)],
                PageCompositions,
                properties,
                IsElementOrComposition: false));
        }

        documentTypes.Add(new TargetDocumentType(
            Keys.Derived("doctype", PlanAliases.Folder),
            PlanAliases.Folder,
            "Folder",
            TemplateAlias: null,
            AllowedAtRoot: false,
            [.. mapped.Select(t => t.Alias), PlanAliases.Folder],
            [PlanAliases.MigrationComposition],
            [],
            IsElementOrComposition: false));
        documentTypes.AddRange(mapped);

        TargetTemplate[] templates =
        [
            new(Keys.Derived("template", PlanAliases.Layout), PlanAliases.Layout, "Migrated layout", null, string.Empty),
            new(Keys.Derived("template", PlanAliases.PageTemplate), PlanAliases.PageTemplate, "Migrated page", PlanAliases.Layout, string.Empty),
        ];

        return new Schema([.. dataTypes.Values.OrderBy(d => d.Name, StringComparer.Ordinal).ThenBy(d => d.Key)], documentTypes, templates);
    }

    /// <summary>
    /// Makes the document types fit the planned tree: types of top-level pages are allowed at the root, and every
    /// type allows the types of its planned children, so no create call is refused for a structural reason.
    /// </summary>
    private static List<TargetDocumentType> ApplyHierarchy(IReadOnlyList<TargetDocumentType> types, IReadOnlyList<TargetDocument> documents)
    {
        var typeByKey = documents.GroupBy(d => d.Key).ToDictionary(g => g.Key, g => g.First().TypeAlias);
        var childAliases = documents
            .Where(d => d.ParentKey is not null)
            .GroupBy(d => typeByKey[d.ParentKey!.Value])
            .ToDictionary(g => g.Key, g => g.Select(d => d.TypeAlias).Distinct().ToList());
        var rootAliases = documents.Where(d => d.ParentKey is null).Select(d => d.TypeAlias).ToHashSet();

        return [.. types.Select(t => t with
        {
            AllowedAtRoot = rootAliases.Contains(t.Alias),
            AllowedChildAliases = childAliases.TryGetValue(t.Alias, out var extra)
                ? [.. t.AllowedChildAliases.Union(extra.OrderBy(a => a, StringComparer.Ordinal))]
                : t.AllowedChildAliases,
        })];
    }

    private static TargetDocumentType CompositionType(string alias, string name, IReadOnlyList<TargetProperty> properties) =>
        new(Keys.Derived("doctype", alias), alias, name, null, false, [], [], properties, IsElementOrComposition: true);

    /// <summary>Options of the Kentico field behind a dropdown mapping; empty when the field is not a plain column.</summary>
    private IReadOnlyList<string> DropdownOptions(string? kenticoClass, string source)
    {
        var field = _site.PageTypes
            .FirstOrDefault(t => string.Equals(t.ClassName, kenticoClass, StringComparison.OrdinalIgnoreCase))?
            .Fields.FirstOrDefault(f => string.Equals(f.Column, source, StringComparison.OrdinalIgnoreCase));
        return field?.Options ?? [];
    }

    private static string HumanName(string alias)
    {
        var chars = new List<char>(alias.Length + 4);
        for (var i = 0; i < alias.Length; i++)
        {
            if (i > 0 && char.IsUpper(alias[i]))
            {
                chars.Add(' ');
                chars.Add(char.ToLowerInvariant(alias[i]));
            }
            else
            {
                chars.Add(i == 0 ? char.ToUpperInvariant(alias[i]) : alias[i]);
            }
        }

        return new string([.. chars]);
    }
}
