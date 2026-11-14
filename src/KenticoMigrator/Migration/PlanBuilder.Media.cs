using KenticoMigrator.Kentico;

namespace KenticoMigrator.Migration;

public sealed partial class PlanBuilder
{
    /// <summary>Kentico class of file pages; they become media items, never documents.</summary>
    internal const string FileNodeClass = "CMS.File";

    private const string MediaRootName = "Kentico import";
    private const string AttachmentsFolder = "Attachments";
    private const string LibrariesFolder = "Libraries";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "png", "gif", "bmp", "webp", "svg", "tif", "tiff",
    };

    private readonly List<SourceDocument> _fileNodes = [];
    private List<TargetMedia> _media = [];

    /// <summary>Planned media per (page, field column), in attachment order, for the field values.</summary>
    private readonly Dictionary<(int NodeId, string Column), List<Guid>> _fieldMedia = [];

    private void PlanMedia(IReadOnlyList<Node> candidates)
    {
        var owners = candidates.Where(n => n.Document is not null).Select(n => n.Document!)
            .Concat(_fileNodes)
            .ToDictionary(d => d.NodeId);
        var allDocuments = _site.Documents.GroupBy(d => d.NodeId).ToDictionary(g => g.Key, g => g.First());

        var folders = new Dictionary<Guid, TargetMedia>();
        var items = new List<TargetMedia>();

        Guid EnsureFolder(params string[] segments)
        {
            var natural = string.Empty;
            var display = MediaRootName;
            var parent = AddFolder("root", null, MediaRootName, display);
            foreach (var segment in segments)
            {
                natural = natural.Length == 0 ? segment : $"{natural}/{segment}";
                display = $"{display}/{segment}";
                parent = AddFolder(natural, parent.Key, segment, display);
            }

            return parent.Key;
        }

        TargetMedia AddFolder(string natural, Guid? parentKey, string name, string sourcePath)
        {
            var key = Keys.Derived("mediafolder", natural);
            if (!folders.TryGetValue(key, out var folder))
            {
                folder = new TargetMedia(key, parentKey, MediaType.Folder, name, null, sourcePath);
                folders.Add(key, folder);
            }

            return folder;
        }

        var attachments = _site.Attachments
            .OrderBy(a => a.NodeId)
            .ThenBy(a => a.Order)
            .ThenBy(a => a.FileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.AttachmentGuid);
        foreach (var attachment in attachments)
        {
            if (!owners.TryGetValue(attachment.NodeId, out var owner))
            {
                var path = allDocuments.TryGetValue(attachment.NodeId, out var skippedOwner) ? skippedOwner.AliasPath : string.Empty;
                SkipAttachment(SkipCategories.UnreferencedAttachment, attachment, path, "The owning page is not migrated.");
                continue;
            }

            if (_profile.NotUploadedPaths.Any(p => IsUnder(owner.AliasPath, p)))
            {
                SkipAttachment(SkipCategories.HostFile, attachment, owner.AliasPath, "Listed as not uploaded in the profile; belongs on the new host.");
                continue;
            }

            if (attachment.FieldColumn is null && !attachment.IsUnsorted)
            {
                SkipAttachment(SkipCategories.UnreferencedAttachment, attachment, owner.AliasPath, "Not used by a field and not an unsorted attachment.");
                continue;
            }

            var section = owner.AliasPath.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Root";
            var key = Keys.ForAttachment(attachment.AttachmentGuid);
            items.Add(new TargetMedia(
                key,
                EnsureFolder(AttachmentsFolder, section),
                MediaTypeFor(attachment.Extension),
                attachment.FileName,
                new MediaSource(attachment.AttachmentGuid, null, attachment.FileName, attachment.MimeType),
                $"{owner.AliasPath}/{attachment.FileName}"));

            if (attachment.FieldColumn is not null)
            {
                var slot = (attachment.NodeId, attachment.FieldColumn.ToLowerInvariant());
                if (!_fieldMedia.TryGetValue(slot, out var list))
                {
                    _fieldMedia[slot] = list = [];
                }

                list.Add(key);
            }
        }

        var files = _site.MediaFiles
            .OrderBy(f => f.LibraryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.FileGuid);
        foreach (var file in files)
        {
            var relative = file.FilePath.Replace('\\', '/').Trim('/');
            var sourcePath = $"{file.LibraryName}/{relative}";
            if (_mediaFileExists is not null && !_mediaFileExists(file))
            {
                _skipped.Add(new SkippedItem(SkipCategories.MissingFile, file.FileGuid.ToString(), sourcePath, "The file is not on disk under the media root."));
                continue;
            }

            string[] subfolders = [.. relative.Split('/').SkipLast(1)];
            items.Add(new TargetMedia(
                Keys.ForMediaFile(file.FileGuid),
                EnsureFolder([LibrariesFolder, file.LibraryName, .. subfolders]),
                MediaTypeFor(file.Extension),
                file.FileName,
                new MediaSource(null, file, file.FileName, file.MimeType),
                sourcePath));
        }

        _media =
        [
            .. folders.Values.OrderBy(f => f.SourcePath, StringComparer.OrdinalIgnoreCase),
            .. items.OrderBy(i => i.SourcePath, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Key),
        ];
    }

    private void SkipAttachment(string category, SourceAttachment attachment, string oldPath, string reason) =>
        _skipped.Add(new SkippedItem(category, attachment.AttachmentGuid.ToString(), oldPath, reason));

    private static MediaType MediaTypeFor(string extension) =>
        ImageExtensions.Contains(extension.TrimStart('.')) ? MediaType.Image : MediaType.File;

    private Guid[] MediaOf(Node node, string column) =>
        node.Document is not null && _fieldMedia.TryGetValue((node.Document.NodeId, column.ToLowerInvariant()), out var keys)
            ? [.. keys]
            : [];
}
