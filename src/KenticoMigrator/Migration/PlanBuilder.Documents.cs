using System.Globalization;
using System.Text;
using KenticoMigrator.Kentico;

namespace KenticoMigrator.Migration;

public sealed partial class PlanBuilder
{
    /// <summary>Kentico alias path of the home page; top-level pages are placed below it (MAPPING.md section 3).</summary>
    internal const string HomeAliasPath = "/Home";

    /// <summary>A page or unlinked row that will become a <see cref="TargetDocument"/>.</summary>
    private sealed class Node
    {
        public required Guid Key { get; init; }
        public required string OldPath { get; init; }
        public required string Name { get; init; }
        public required string TypeAlias { get; init; }
        public required ValueSource Source { get; init; }
        public required bool Publish { get; init; }
        public required int SortOrder { get; init; }
        public SourceDocument? Document { get; init; }
        public TypeMapping? Mapping { get; init; }
        public string? ScheduleNote { get; init; }
        public string? RowClass { get; init; }
        public bool IsSettings { get; init; }
        public string UrlName { get; set; } = string.Empty;
        public Node? Parent { get; set; }
        public int Depth { get; set; }
    }

    /// <summary>Values a field mapping can read, for a page or for an unlinked row.</summary>
    private sealed record ValueSource(
        string Name,
        SourceDocument? Document,
        IReadOnlyDictionary<string, object?> Fields,
        IReadOnlyList<EditableRegion> Regions,
        IReadOnlyDictionary<string, object?>? Sku);

    private List<TargetDocument> BuildDocuments()
    {
        CheckProfileClasses();

        var candidates = new List<Node>();
        var ordered = _site.Documents
            .OrderBy(d => d.AliasPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.NodeId)
            .ThenBy(d => d.NodeGuid);
        var seenNodeIds = new HashSet<int>();
        var seenGuids = new HashSet<Guid>();
        foreach (var doc in ordered)
        {
            if (!seenNodeIds.Add(doc.NodeId) || !seenGuids.Add(doc.NodeGuid))
            {
                Error(doc.AliasPath, "The source has more than one page with the same node id or node GUID; this one is ignored.");
                continue;
            }

            var node = Classify(doc);
            if (node is not null)
            {
                candidates.Add(node);
            }
        }

        PlanMedia(candidates);
        PrepareAliases();

        var byNodeId = candidates.ToDictionary(n => n.Document!.NodeId);
        var byAliasPath = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in candidates)
        {
            byAliasPath.TryAdd(node.OldPath, node);
        }

        var sourceById = SourceById();
        byAliasPath.TryGetValue(HomeAliasPath, out var home);

        foreach (var node in candidates)
        {
            node.Parent = ResolveParent(node, byNodeId, sourceById, byAliasPath, home);
            node.UrlName = ResolveUrlName(node);
        }

        AddSettingsFolder(candidates, byAliasPath, home);
        ApplyPlacements(candidates, byAliasPath, home);
        BreakParentCycles(candidates, home);
        AddUnlinkedRows(candidates, byAliasPath);
        CheckSiblingNames(candidates);
        AssignDepths(candidates);

        var sorted = candidates
            .OrderBy(n => n.Depth)
            .ThenBy(n => n.SortOrder)
            .ThenBy(n => n.OldPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Key)
            .ToList();

        return [.. sorted.Select(ToDocument)];
    }

    /// <summary>
    /// The tool-owned settings folder: a top-level folder below the home page, published, hidden from navigation and sitemap.
    /// Added before the placements so they can name it as a parent.
    /// </summary>
    private void AddSettingsFolder(List<Node> nodes, Dictionary<string, Node> byAliasPath, Node? home)
    {
        if (_profile.SettingsFolder is not { } rule)
        {
            return;
        }

        if (byAliasPath.ContainsKey(rule.Path))
        {
            Error(rule.Path, "The settings folder path is the path of a source page.");
            return;
        }

        var node = new Node
        {
            Key = Keys.Derived("settings", rule.Path.ToLowerInvariant()),
            OldPath = rule.Path,
            Name = rule.Name,
            TypeAlias = PlanAliases.Folder,
            Source = new ValueSource(rule.Name, null, new Dictionary<string, object?>(), [], null),
            Publish = true,
            // After every source page of the top level, which are numbered from the source order.
            SortOrder = int.MaxValue,
            IsSettings = true,
            UrlName = rule.Path.Trim('/'),
            Parent = home,
        };
        nodes.Add(node);
        byAliasPath[rule.Path] = node;
    }

    /// <summary>Moves the pages named by the profile's placements (and so everything below them) and sets their URL names.</summary>
    private void ApplyPlacements(List<Node> nodes, Dictionary<string, Node> byAliasPath, Node? home)
    {
        foreach (var rule in _profile.Placements)
        {
            if (!byAliasPath.TryGetValue(rule.Path, out var node) || node.IsSettings)
            {
                Error(rule.Path, "Placement path does not resolve to a planned page.");
                continue;
            }

            Node? parent;
            if (rule.NewParentPath.Trim('/').Length == 0)
            {
                parent = home is not null && home != node ? home : null;
            }
            else if (!byAliasPath.TryGetValue(rule.NewParentPath, out parent) || parent == node)
            {
                Error(rule.Path, $"Placement parent path '{rule.NewParentPath}' does not resolve to a planned page.");
                continue;
            }

            node.Parent = parent;
            if (!string.IsNullOrWhiteSpace(rule.UrlName))
            {
                node.UrlName = rule.UrlName;
            }
        }
    }

    private Dictionary<int, SourceDocument>? _sourceById;

    /// <summary>Source pages by node id; if an id repeats, the page that sorts first by path then GUID is the one used.</summary>
    private Dictionary<int, SourceDocument> SourceById() =>
        _sourceById ??= _site.Documents
            .OrderBy(d => d.AliasPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.NodeGuid)
            .GroupBy(d => d.NodeId)
            .ToDictionary(g => g.Key, g => g.First());

    /// <summary>A re-parent chain that leads back to the page itself is a plan error; the page goes to the top level instead.</summary>
    private void BreakParentCycles(List<Node> nodes, Node? home)
    {
        foreach (var node in nodes)
        {
            var steps = 0;
            for (var p = node.Parent; p is not null && steps <= nodes.Count; p = p.Parent, steps++)
            {
                if (p == node)
                {
                    Error(node.OldPath, "Re-parenting creates a cycle; the page is placed at the top level.");
                    node.Parent = home is not null && home != node ? home : null;
                    break;
                }
            }
        }
    }

    private void CheckProfileClasses()
    {
        var known = _site.PageTypes.Select(t => t.ClassName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referenced = _profile.Types.Select(t => t.KenticoClass)
            .Concat(_profile.Reparent.Select(r => r.KenticoClass))
            .Concat(_profile.UnlinkedRows.Select(r => r.KenticoClass))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var className in referenced.Where(c => !known.Contains(c)))
        {
            Error("profile", $"Profile refers to Kentico class '{className}' that is not a page type of the source.");
        }
    }

    /// <summary>Decides whether a source page is planned, and as what. Skipped pages are recorded with a reason.</summary>
    private Node? Classify(SourceDocument doc)
    {
        if (doc.LinkedNodeId is not null)
        {
            Skip(SkipCategories.LinkedNode, doc, "Linked node: its content is migrated from the original node.");
            return null;
        }

        var excluded = _profile.ExcludedPathPrefixes.FirstOrDefault(p => IsUnder(doc.AliasPath, p));
        if (excluded is not null)
        {
            Skip(SkipCategories.ApplicationPage, doc, $"Under excluded path '{excluded}'.");
            return null;
        }

        if (string.Equals(doc.ClassName, FileNodeClass, StringComparison.OrdinalIgnoreCase))
        {
            // A file page becomes a media item (see PlanMedia), not a document.
            _fileNodes.Add(doc);
            return null;
        }

        var isFolder = _profile.FolderPaths.Any(p => PathEquals(doc.AliasPath, p));
        var mapping = isFolder ? null : FindMapping(doc);
        if (!isFolder && mapping is null)
        {
            Skip(SkipCategories.UnmappedType, doc, $"No type mapping for Kentico class '{doc.ClassName}'.");
            return null;
        }

        return new Node
        {
            Key = Keys.ForDocument(doc),
            OldPath = doc.AliasPath,
            Name = doc.Name,
            TypeAlias = isFolder ? PlanAliases.Folder : mapping!.TargetAlias,
            Mapping = mapping,
            Document = doc,
            Source = new ValueSource(doc.Name, doc, doc.Fields, doc.Regions, doc.Sku),
            Publish = doc.IsPublished,
            SortOrder = doc.Order,
            ScheduleNote = ScheduleNoteFor(doc),
        };
    }

    private TypeMapping? FindMapping(SourceDocument doc)
    {
        var sameClass = _profile.Types.Where(t => string.Equals(t.KenticoClass, doc.ClassName, StringComparison.OrdinalIgnoreCase)).ToList();
        return sameClass.FirstOrDefault(t => t.PathOverride is not null && PathEquals(doc.AliasPath, t.PathOverride))
            ?? sameClass.FirstOrDefault(t => t.PathOverride is null);
    }

    private void Skip(string category, SourceDocument doc, string reason) =>
        _skipped.Add(new SkippedItem(category, doc.NodeGuid.ToString(), doc.AliasPath, reason));

    private static string? ScheduleNoteFor(SourceDocument doc)
    {
        if (doc.PublishFrom is null && doc.PublishTo is null)
        {
            return null;
        }

        static string Format(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "open";
        return $"Publish window not applied: from {Format(doc.PublishFrom)} to {Format(doc.PublishTo)}.";
    }

    private Node? ResolveParent(
        Node node,
        Dictionary<int, Node> byNodeId,
        Dictionary<int, SourceDocument> sourceById,
        Dictionary<string, Node> byAliasPath,
        Node? home)
    {
        var doc = node.Document!;

        var rule = _profile.Reparent.FirstOrDefault(r => string.Equals(r.KenticoClass, doc.ClassName, StringComparison.OrdinalIgnoreCase));
        if (rule is not null)
        {
            if (byAliasPath.TryGetValue(rule.NewParentPath, out var newParent) && newParent != node)
            {
                if (!string.Equals(newParent.Document!.ClassName, doc.ClassName, StringComparison.OrdinalIgnoreCase))
                {
                    return newParent;
                }

                Error(doc.AliasPath, $"Re-parent path '{rule.NewParentPath}' is a page of the same Kentico class '{doc.ClassName}'.");
            }
            else
            {
                Error(doc.AliasPath, $"Re-parent path '{rule.NewParentPath}' does not resolve to a planned page.");
            }
        }

        if (doc.ParentNodeId is { } parentId && sourceById.TryGetValue(parentId, out var directParent))
        {
            // Walk up to the nearest migrated ancestor; a skipped parent never leaves a page orphaned.
            var current = directParent;
            for (var hops = 0; hops <= sourceById.Count; hops++)
            {
                if (byNodeId.TryGetValue(current.NodeId, out var planned))
                {
                    if (hops > 0)
                    {
                        Warning(doc.AliasPath, $"Parent '{directParent.AliasPath}' is not migrated; the page is attached to '{planned.OldPath}'.");
                    }

                    return planned;
                }

                if (current.ParentNodeId is not { } next || !sourceById.TryGetValue(next, out var above))
                {
                    break;
                }

                current = above;
            }

            // The Kentico root node itself is never content, so a page below it is simply top-level.
            if (directParent.ParentNodeId is not null)
            {
                Warning(doc.AliasPath, $"Parent '{directParent.AliasPath}' is not migrated; the page is attached to {(home is null ? "the top level" : "the home page")}.");
            }
        }

        else if (doc.ParentNodeId is not null && doc.AliasPath.Trim('/').Contains('/', StringComparison.Ordinal))
        {
            // A nested page whose parent row is absent; a top-level page below the unread Kentico root is normal.
            Warning(doc.AliasPath, "The parent node of this page is not in the source; the page is attached to the home page.");
        }

        return home is not null && home != node ? home : null;
    }

    private string ResolveUrlName(Node node)
    {
        var doc = node.Document!;
        var rule = _profile.Reparent.FirstOrDefault(r => string.Equals(r.KenticoClass, doc.ClassName, StringComparison.OrdinalIgnoreCase));
        if (rule is null || string.Equals(rule.UrlNameFrom, "NodeAlias", StringComparison.OrdinalIgnoreCase))
        {
            return doc.NodeAlias;
        }

        var fromField = Convert.ToString(Lookup(doc.Fields, rule.UrlNameFrom), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(fromField))
        {
            Error(doc.AliasPath, $"Re-parent rule takes the URL name from '{rule.UrlNameFrom}', which is empty.");
            return doc.NodeAlias;
        }

        return fromField;
    }

    private void AddUnlinkedRows(List<Node> nodes, Dictionary<string, Node> byAliasPath)
    {
        foreach (var rule in _profile.UnlinkedRows)
        {
            if (!_site.UnlinkedRows.TryGetValue(rule.KenticoClass, out var rows))
            {
                continue;
            }

            var mapping = _profile.Types.FirstOrDefault(t =>
                string.Equals(t.KenticoClass, rule.KenticoClass, StringComparison.OrdinalIgnoreCase) && t.PathOverride is null);
            if (mapping is null)
            {
                Error("profile", $"Unlinked rows of '{rule.KenticoClass}' have no type mapping.");
                continue;
            }

            if (!byAliasPath.TryGetValue(rule.ParentPath, out var parent))
            {
                Error("profile", $"Unlinked row parent path '{rule.ParentPath}' does not resolve to a planned page.");
                continue;
            }

            foreach (var row in rows.OrderBy(r => r.Id))
            {
                var oldPath = $"unlinked:{rule.KenticoClass}:{row.Id}";
                var name = Convert.ToString(Lookup(row.Values, rule.NameField), CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(name))
                {
                    Error(oldPath, $"Unlinked row has no value in '{rule.NameField}'.");
                    continue;
                }

                _ = int.TryParse(Convert.ToString(Lookup(row.Values, "Order"), CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var order);
                var urlName = Slug(name);
                if (urlName.Length == 0)
                {
                    Error(oldPath, "The unlinked row name has no letters or digits to build a URL name from.");
                    continue;
                }

                nodes.Add(new Node
                {
                    Key = Keys.Derived("unlinked", $"{rule.KenticoClass}:{row.Id}"),
                    OldPath = oldPath,
                    Name = name,
                    TypeAlias = mapping.TargetAlias,
                    Mapping = mapping,
                    RowClass = rule.KenticoClass,
                    Source = new ValueSource(name, null, row.Values, [], null),
                    Publish = rule.Publish,
                    SortOrder = order,
                    UrlName = urlName,
                    Parent = parent,
                });
            }
        }
    }

    private void CheckSiblingNames(List<Node> nodes)
    {
        var groups = nodes
            .Where(n => n.Parent is not null)
            .GroupBy(n => (Parent: n.Parent!.Key, Name: n.UrlName.ToLowerInvariant()));
        foreach (var group in groups.Where(g => g.Count() > 1))
        {
            var ordered = group.OrderBy(n => n.SortOrder).ThenBy(n => n.OldPath, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var duplicate in ordered.Skip(1))
            {
                Error(duplicate.OldPath, $"Sibling URL name collides with '{ordered[0].OldPath}'.");
            }
        }
    }

    private static void AssignDepths(List<Node> nodes)
    {
        foreach (var node in nodes)
        {
            var depth = 0;
            for (var p = node.Parent; p is not null && depth <= nodes.Count; p = p.Parent)
            {
                depth++;
            }

            node.Depth = depth;
        }
    }

    private TargetDocument ToDocument(Node node) => new(
        node.Key,
        node.Parent?.Key,
        node.TypeAlias,
        node.Name,
        node.UrlName,
        node.SortOrder,
        node.Publish,
        BuildValues(node),
        node.OldPath,
        OldUrlsFor(node),
        node.ScheduleNote);

    private static bool PathEquals(string path, string other) =>
        string.Equals(path.TrimEnd('/'), other.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string prefix)
    {
        var p = prefix.TrimEnd('/');
        return path.Equals(p, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Lowercase letters and digits separated by single dashes; used only for rows that have no node alias.</summary>
    private static string Slug(string name)
    {
        var sb = new StringBuilder(name.Length);
        var pendingDash = false;
        foreach (var ch in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingDash && sb.Length > 0)
                {
                    sb.Append('-');
                }

                sb.Append(ch);
                pendingDash = false;
            }
            else
            {
                pendingDash = true;
            }
        }

        return sb.ToString();
    }
}
