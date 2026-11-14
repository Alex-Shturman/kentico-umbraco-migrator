using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace KenticoMigrator.Kentico;

/// <summary>
/// Reads a Kentico 11 database through SQL. Read-only: only SELECT statements, and the connection
/// string carries <c>ApplicationIntent=ReadOnly</c>. See contracts/kentico-reader.md.
/// </summary>
public sealed partial class Kentico11SqlReader : IKenticoReader
{
    private const string SupportedVersionPrefix = "11.";
    private const string DocumentPageTypeFlag = "ClassIsDocumentType";

    private static readonly string[] SkuColumns = ["SKUNumber", "SKUPrice", "SKURetailPrice", "SKUAvailableItems", "SKUEnabled"];

    private readonly string _connectionString;
    private readonly string? _mediaRoot;
    private readonly TimeProvider _time;

    /// <param name="connectionString">Connection string of the Kentico database; the intent is forced to read-only.</param>
    /// <param name="mediaRoot">Folder that contains the media library folders; needed only to open media files.</param>
    public Kentico11SqlReader(string connectionString, string? mediaRoot = null, TimeProvider? time = null)
    {
        _connectionString = ReadOnlyConnectionString(connectionString);
        _mediaRoot = string.IsNullOrWhiteSpace(mediaRoot) ? null : mediaRoot;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Returns the connection string with <c>ApplicationIntent=ReadOnly</c>.</summary>
    public static string ReadOnlyConnectionString(string connectionString) =>
        new SqlConnectionStringBuilder(connectionString) { ApplicationIntent = ApplicationIntent.ReadOnly }.ConnectionString;

    /// <inheritdoc />
    public async Task<SourceSite> ReadAsync(IReadOnlyCollection<string> unlinkedRowClasses, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);

        var version = await ReadVersionAsync(connection, ct);
        var site = await ReadSingleSiteAsync(connection, ct);

        var classes = await ReadClassesAsync(connection, unlinkedRowClasses, ct);
        var templates = await ReadTemplateOrdersAsync(connection, ct);
        var skus = await ReadSkusAsync(connection, ct);
        var coupled = await ReadCoupledRowsAsync(connection, classes.Values.Where(c => c.IsUsedByNodes), ct);

        var nodes = (await connection.QueryAsync<NodeRow>(new CommandDefinition(
            """
            SELECT n.NodeID, n.NodeGUID, n.NodeParentID, n.NodeClassID, n.NodeAlias, n.NodeAliasPath, n.NodeOrder, n.NodeSKUID,
                   d.DocumentID, d.DocumentName, d.DocumentForeignKeyValue, d.DocumentPageTitle, d.DocumentPageDescription, d.DocumentPageKeyWords,
                   d.DocumentMenuItemHideInNavigation, d.DocumentShowInSiteMap, d.DocumentCanBePublished, d.DocumentPublishFrom,
                   d.DocumentPublishTo, d.DocumentCreatedWhen, d.DocumentModifiedWhen, d.DocumentUrlPath, d.DocumentExtensions,
                   d.DocumentContent, d.DocumentPageTemplateID
            FROM CMS_Tree n
            JOIN CMS_Document d ON d.DocumentNodeID = n.NodeID
            WHERE n.NodeSiteID = @SiteId AND n.NodeLinkedNodeID IS NULL
            ORDER BY n.NodeLevel, n.NodeOrder, n.NodeID
            """,
            new { site.SiteId }, cancellationToken: ct))).ToList();

        var now = _time.GetLocalNow().DateTime;
        var documents = nodes.Select(node => ToDocument(node, classes, templates, skus, coupled, now)).ToList();

        var unlinked = await ReadUnlinkedRowsAsync(connection, classes, unlinkedRowClasses, ct);
        var pageTypes = classes.Values
            .Select(c => new SourcePageType(c.ClassName, c.TableName, c.IsCoupled, c.Fields))
            .OrderBy(t => t.ClassName, StringComparer.Ordinal)
            .ToList();

        var attachments = await ReadAttachmentsAsync(connection, nodes, documents, classes, ct);
        var mediaFiles = await ReadMediaFilesAsync(connection, site.SiteId, ct);

        return new SourceSite(site.SiteName, version, pageTypes, documents, attachments, [], mediaFiles, unlinked);
    }

    /// <inheritdoc />
    /// <remarks>The binary is buffered in memory: one attachment at a time, never the whole table.</remarks>
    public async Task<Stream> OpenAttachmentAsync(Guid attachmentGuid, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new SqlCommand(
            "SELECT AttachmentBinary FROM CMS_Attachment WHERE AttachmentGUID = @guid", connection);
        command.Parameters.AddWithValue("@guid", attachmentGuid);

        await using var reader = await command.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, ct);
        if (!await reader.ReadAsync(ct) || await reader.IsDBNullAsync(0, ct))
        {
            throw new FileNotFoundException($"Attachment {attachmentGuid} has no binary in the database.");
        }

        var buffer = new MemoryStream();
        await using (var source = reader.GetStream(0))
        {
            await source.CopyToAsync(buffer, ct);
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <inheritdoc />
    public Task<Stream> OpenMediaFileAsync(SourceMediaFile file, CancellationToken ct)
    {
        if (_mediaRoot is null)
        {
            throw new InvalidOperationException("Kentico:MediaRoot is not configured; media library files cannot be opened.");
        }

        // The library folder and path come from the database: keep them inside the media root.
        var root = Path.GetFullPath(_mediaRoot);
        var path = Path.GetFullPath(Path.Combine(root, file.LibraryFolder, file.FilePath));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Media file path '{file.FilePath}' points outside the media root.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Media file '{file.LibraryFolder}/{file.FilePath}' not found under the media root.", path);
        }

        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true));
    }

    /// <inheritdoc />
    public async Task<SourceCounts> CountAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);

        var perClass = await connection.QueryAsync<(string ClassName, int Count)>(new CommandDefinition(
            """
            SELECT c.ClassName, COUNT(*)
            FROM CMS_Tree n
            JOIN CMS_Document d ON d.DocumentNodeID = n.NodeID
            JOIN CMS_Class c ON c.ClassID = n.NodeClassID
            WHERE n.NodeLinkedNodeID IS NULL
            GROUP BY c.ClassName
            """, cancellationToken: ct));

        var attachments = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM CMS_Attachment WHERE AttachmentDocumentID IS NOT NULL", cancellationToken: ct));
        var mediaFiles = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM Media_File", cancellationToken: ct));

        return new SourceCounts(perClass.ToDictionary(r => r.ClassName, r => r.Count), attachments, mediaFiles, 0);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<string> ReadVersionAsync(SqlConnection connection, CancellationToken ct)
    {
        var version = await connection.QueryFirstOrDefaultAsync<string>(new CommandDefinition(
            "SELECT TOP 1 KeyValue FROM CMS_SettingsKey WHERE KeyName = N'CMSDBVersion' ORDER BY CASE WHEN SiteID IS NULL THEN 0 ELSE 1 END",
            cancellationToken: ct)) ?? "unknown";

        if (!version.StartsWith(SupportedVersionPrefix, StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Kentico {version} is not supported; this reader supports 11.x");
        }

        return version;
    }

    private static async Task<(int SiteId, string SiteName)> ReadSingleSiteAsync(SqlConnection connection, CancellationToken ct)
    {
        var sites = (await connection.QueryAsync<(int SiteId, string SiteName)>(new CommandDefinition(
            "SELECT SiteID, SiteName FROM CMS_Site ORDER BY SiteID", cancellationToken: ct))).ToList();
        var cultures = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT DocumentCulture FROM CMS_Document", cancellationToken: ct))).ToList();

        if (sites.Count != 1 || cultures.Count > 1)
        {
            throw new NotSupportedException(
                $"Only one site and one culture are supported; found {sites.Count} site(s) and {cultures.Count} culture(s).");
        }

        return sites[0];
    }

    /// <summary>Page types used by at least one node, plus those named in the profile.</summary>
    private static async Task<Dictionary<int, ClassInfo>> ReadClassesAsync(
        SqlConnection connection, IReadOnlyCollection<string> unlinkedRowClasses, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<ClassRow>(new CommandDefinition(
            $"""
            SELECT c.ClassID, c.ClassName, c.ClassTableName, c.ClassIsCoupledClass, c.ClassFormDefinition,
                   CAST(CASE WHEN EXISTS (SELECT 1 FROM CMS_Tree n WHERE n.NodeClassID = c.ClassID) THEN 1 ELSE 0 END AS bit) AS IsUsedByNodes
            FROM CMS_Class c
            WHERE c.{DocumentPageTypeFlag} = 1
            """, cancellationToken: ct));

        var wanted = new HashSet<string>(unlinkedRowClasses, StringComparer.OrdinalIgnoreCase);
        return rows
            .Where(r => r.IsUsedByNodes || wanted.Contains(r.ClassName))
            .ToDictionary(
                r => r.ClassID,
                r => new ClassInfo(
                    r.ClassID,
                    r.ClassName,
                    r.ClassTableName,
                    r.ClassIsCoupledClass && !string.IsNullOrWhiteSpace(r.ClassTableName),
                    FormDefinition.Parse(r.ClassFormDefinition),
                    FormDefinition.PrimaryKeyColumn(r.ClassFormDefinition),
                    r.IsUsedByNodes));
    }

    private static async Task<Dictionary<int, IReadOnlyList<string>>> ReadTemplateOrdersAsync(SqlConnection connection, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<(int PageTemplateID, string? PageTemplateWebParts)>(new CommandDefinition(
            "SELECT PageTemplateID, PageTemplateWebParts FROM CMS_PageTemplate", cancellationToken: ct));
        return rows.ToDictionary(r => r.PageTemplateID, r => EditableRegions.TemplateOrder(r.PageTemplateWebParts));
    }

    private static async Task<Dictionary<int, IReadOnlyDictionary<string, object?>>> ReadSkusAsync(SqlConnection connection, CancellationToken ct)
    {
        var rows = await connection.QueryAsync(new CommandDefinition(
            $"SELECT SKUID, {string.Join(", ", SkuColumns)} FROM COM_SKU", cancellationToken: ct));

        return rows.Cast<IDictionary<string, object?>>().ToDictionary(
            row => (int)row["SKUID"]!,
            row => (IReadOnlyDictionary<string, object?>)SkuColumns.ToDictionary(c => c, c => row[c]));
    }

    /// <summary>Rows of every coupled table, keyed by class id and primary key value.</summary>
    private static async Task<Dictionary<int, Dictionary<int, IReadOnlyDictionary<string, object?>>>> ReadCoupledRowsAsync(
        SqlConnection connection, IEnumerable<ClassInfo> classes, CancellationToken ct)
    {
        var result = new Dictionary<int, Dictionary<int, IReadOnlyDictionary<string, object?>>>();
        foreach (var info in classes.Where(c => c.IsCoupled))
        {
            result[info.ClassId] = await ReadRowsAsync(connection, info, ct);
        }

        return result;
    }

    private static async Task<Dictionary<int, IReadOnlyDictionary<string, object?>>> ReadRowsAsync(
        SqlConnection connection, ClassInfo info, CancellationToken ct)
    {
        // Table and column names come from the database itself; allow plain identifiers only, then quote them.
        var table = QuoteIdentifier(info.TableName!);
        var key = QuoteIdentifier(info.PrimaryKey ?? throw new InvalidDataException($"Page type {info.ClassName} has no primary key field."));
        // Attachment group fields have no column in the table.
        var columns = info.Fields.Where(f => f.DataType != "docattachments").Select(f => f.Column).ToList();
        var select = string.Join(", ", columns.Select(QuoteIdentifier).Prepend(key));

        var rows = await connection.QueryAsync(new CommandDefinition($"SELECT {select} FROM {table}", cancellationToken: ct));

        var map = new Dictionary<int, IReadOnlyDictionary<string, object?>>();
        foreach (var row in rows.Cast<IDictionary<string, object?>>())
        {
            map[(int)row[info.PrimaryKey]!] = columns.ToDictionary(c => c, c => row[c]);
        }

        return map;
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<SourceRow>>> ReadUnlinkedRowsAsync(
        SqlConnection connection, Dictionary<int, ClassInfo> classes, IReadOnlyCollection<string> requested, CancellationToken ct)
    {
        var result = new Dictionary<string, IReadOnlyList<SourceRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in classes.Values.Where(c => c.IsCoupled && requested.Contains(c.ClassName, StringComparer.OrdinalIgnoreCase)))
        {
            var linked = (await connection.QueryAsync<int>(new CommandDefinition(
                """
                SELECT d.DocumentForeignKeyValue FROM CMS_Document d
                JOIN CMS_Tree n ON n.NodeID = d.DocumentNodeID
                WHERE n.NodeClassID = @ClassId AND d.DocumentForeignKeyValue IS NOT NULL
                """, new { info.ClassId }, cancellationToken: ct))).ToHashSet();

            var rows = await ReadRowsAsync(connection, info, ct);
            result[info.ClassName] = rows
                .Where(r => !linked.Contains(r.Key))
                .OrderBy(r => r.Key)
                .Select(r => new SourceRow(r.Key, r.Value))
                .ToList();
        }

        return result;
    }

    /// <summary>Current attachments of the site's documents, with the field each one belongs to.</summary>
    private static async Task<IReadOnlyList<SourceAttachment>> ReadAttachmentsAsync(
        SqlConnection connection,
        List<NodeRow> nodes,
        List<SourceDocument> documents,
        Dictionary<int, ClassInfo> classes,
        CancellationToken ct)
    {
        var rows = await connection.QueryAsync<AttachmentRow>(new CommandDefinition(
            """
            SELECT AttachmentGUID, AttachmentDocumentID, AttachmentName, AttachmentExtension, AttachmentMimeType, AttachmentSize,
                   AttachmentImageWidth, AttachmentImageHeight, AttachmentTitle, AttachmentIsUnsorted, AttachmentOrder, AttachmentGroupGUID
            FROM CMS_Attachment
            WHERE AttachmentDocumentID IS NOT NULL
            """, cancellationToken: ct));

        var ownerByDocument = nodes.Select((n, i) => (Node: n, Document: documents[i])).ToDictionary(x => x.Node.DocumentID);
        var result = new List<SourceAttachment>();
        foreach (var row in rows)
        {
            // Attachments of documents that are not read (linked nodes, other sites) are skipped.
            if (!ownerByDocument.TryGetValue(row.AttachmentDocumentID, out var owner))
            {
                continue;
            }

            // Group fields match AttachmentGroupGUID to the field guid; file fields hold the attachment guid.
            var fields = classes[owner.Node.NodeClassID].Fields;
            var column = row.AttachmentGroupGUID is { } group
                ? fields.FirstOrDefault(f => f.DataType == "docattachments" && f.FieldGuid == group)?.Column
                : fields.FirstOrDefault(f => f.DataType == "file"
                    && owner.Document.Fields.TryGetValue(f.Column, out var value) && value is Guid g && g == row.AttachmentGUID)?.Column;

            result.Add(new SourceAttachment(
                row.AttachmentGUID,
                owner.Node.NodeID,
                row.AttachmentName,
                NormalizeExtension(row.AttachmentExtension),
                row.AttachmentMimeType,
                row.AttachmentSize,
                row.AttachmentImageWidth,
                row.AttachmentImageHeight,
                string.IsNullOrWhiteSpace(row.AttachmentTitle) ? null : row.AttachmentTitle,
                column,
                row.AttachmentIsUnsorted ?? false,
                row.AttachmentOrder ?? 0));
        }

        return result.OrderBy(a => a.NodeId).ThenBy(a => a.Order).ThenBy(a => a.FileName, StringComparer.Ordinal).ToList();
    }

    private static async Task<IReadOnlyList<SourceMediaFile>> ReadMediaFilesAsync(SqlConnection connection, int siteId, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<MediaFileRow>(new CommandDefinition(
            """
            SELECT f.FileGUID, l.LibraryName, l.LibraryFolder, f.FilePath, f.FileName, f.FileExtension, f.FileMimeType, f.FileSize, f.FileTitle
            FROM Media_File f
            JOIN Media_Library l ON l.LibraryID = f.FileLibraryID
            WHERE l.LibrarySiteID = @siteId
            ORDER BY l.LibraryName, f.FilePath
            """, new { siteId }, cancellationToken: ct));

        // Kentico stores the file name without its extension; the reader returns the name as it is on disk.
        return rows.Select(r => new SourceMediaFile(
            r.FileGUID,
            r.LibraryName,
            r.LibraryFolder,
            r.FilePath.Replace('\\', '/'),
            r.FileName + r.FileExtension,
            NormalizeExtension(r.FileExtension),
            r.FileMimeType,
            r.FileSize,
            string.IsNullOrWhiteSpace(r.FileTitle) ? null : r.FileTitle)).ToList();
    }

    /// <summary>Extensions are returned lower case without the leading dot.</summary>
    private static string NormalizeExtension(string extension) => extension.TrimStart('.').ToLowerInvariant();

    private SourceDocument ToDocument(
        NodeRow node,
        Dictionary<int, ClassInfo> classes,
        Dictionary<int, IReadOnlyList<string>> templates,
        Dictionary<int, IReadOnlyDictionary<string, object?>> skus,
        Dictionary<int, Dictionary<int, IReadOnlyDictionary<string, object?>>> coupled,
        DateTime now)
    {
        var info = classes[node.NodeClassID];

        IReadOnlyDictionary<string, object?> fields = new Dictionary<string, object?>();
        if (info.IsCoupled && node.DocumentForeignKeyValue is { } foreignKey
            && coupled.TryGetValue(info.ClassId, out var rows) && rows.TryGetValue(foreignKey, out var row))
        {
            fields = row;
        }

        var order = node.DocumentPageTemplateID is { } templateId && templates.TryGetValue(templateId, out var o) ? o : [];
        var sku = node.NodeSKUID is { } skuId && skus.TryGetValue(skuId, out var s) ? s : null;

        // Research R5a: published only when allowed and inside the publish window.
        var published = node.DocumentCanBePublished
            && (node.DocumentPublishFrom is null || node.DocumentPublishFrom <= now)
            && (node.DocumentPublishTo is null || node.DocumentPublishTo > now);

        return new SourceDocument(
            node.NodeGUID,
            node.NodeID,
            node.NodeParentID,
            info.ClassName,
            node.DocumentName,
            node.NodeAlias,
            node.NodeAliasPath,
            node.NodeOrder ?? 0,
            published,
            node.DocumentPublishFrom,
            node.DocumentPublishTo,
            node.DocumentMenuItemHideInNavigation,
            node.DocumentShowInSiteMap,
            node.DocumentPageTitle,
            node.DocumentPageDescription,
            node.DocumentPageKeyWords,
            node.DocumentCreatedWhen ?? default,
            node.DocumentModifiedWhen ?? default,
            node.DocumentUrlPath,
            node.DocumentExtensions,
            fields,
            EditableRegions.Parse(node.DocumentContent, order),
            sku);
    }

    private static string QuoteIdentifier(string name)
    {
        if (!SafeIdentifier().IsMatch(name))
        {
            throw new InvalidDataException($"Unexpected character in database identifier '{name}'.");
        }

        return $"[{name}]";
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SafeIdentifier();

    private sealed record ClassInfo(
        int ClassId,
        string ClassName,
        string? TableName,
        bool IsCoupled,
        IReadOnlyList<SourceField> Fields,
        string? PrimaryKey,
        bool IsUsedByNodes);

    private sealed record ClassRow(
        int ClassID,
        string ClassName,
        string? ClassTableName,
        bool ClassIsCoupledClass,
        string ClassFormDefinition,
        bool IsUsedByNodes);

    private sealed record AttachmentRow(
        Guid AttachmentGUID,
        int AttachmentDocumentID,
        string AttachmentName,
        string AttachmentExtension,
        string AttachmentMimeType,
        int AttachmentSize,
        int? AttachmentImageWidth,
        int? AttachmentImageHeight,
        string? AttachmentTitle,
        bool? AttachmentIsUnsorted,
        int? AttachmentOrder,
        Guid? AttachmentGroupGUID);

    private sealed record MediaFileRow(
        Guid FileGUID,
        string LibraryName,
        string LibraryFolder,
        string FilePath,
        string FileName,
        string FileExtension,
        string FileMimeType,
        long FileSize,
        string? FileTitle);

    private sealed record NodeRow(
        int NodeID,
        Guid NodeGUID,
        int? NodeParentID,
        int NodeClassID,
        string NodeAlias,
        string NodeAliasPath,
        int? NodeOrder,
        int? NodeSKUID,
        int DocumentID,
        string DocumentName,
        int? DocumentForeignKeyValue,
        string? DocumentPageTitle,
        string? DocumentPageDescription,
        string? DocumentPageKeyWords,
        bool DocumentMenuItemHideInNavigation,
        bool DocumentShowInSiteMap,
        bool DocumentCanBePublished,
        DateTime? DocumentPublishFrom,
        DateTime? DocumentPublishTo,
        DateTime? DocumentCreatedWhen,
        DateTime? DocumentModifiedWhen,
        string? DocumentUrlPath,
        string? DocumentExtensions,
        string? DocumentContent,
        int? DocumentPageTemplateID);
}
