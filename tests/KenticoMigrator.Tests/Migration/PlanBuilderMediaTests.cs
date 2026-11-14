using System.Text.Json;
using KenticoMigrator.Kentico;
using KenticoMigrator.Migration;
using KenticoMigrator.Tests.Support;
using static KenticoMigrator.Tests.Support.SourceFactory;

namespace KenticoMigrator.Tests.Migration;

public sealed class PlanBuilderMediaTests
{
    private static readonly Guid A1 = GuidFor(1001);
    private static readonly Guid A2 = GuidFor(1002);
    private static readonly Guid A3 = GuidFor(1003);

    private static SourceSite WithMedia(
        IReadOnlyList<SourceAttachment>? attachments = null,
        IReadOnlyList<SourceMediaFile>? files = null)
    {
        var site = SampleSite();
        return site with
        {
            PageTypes = [.. site.PageTypes, Type("CMS.File")],
            Documents =
            [
                .. site.Documents,
                Doc(20, null, "CMS.File", "/Files/banner", order: 6),
                Doc(21, null, "CMS.File", "/Files/feed", order: 7),
            ],
            Attachments = attachments ?? [],
            MediaFiles = files ?? [],
        };
    }

    private static MigrationPlan Build(SourceSite site, Func<SourceMediaFile, bool>? exists = null) =>
        PlanBuilder.Build(site, SampleProfile() with { NotUploadedPaths = ["/Files/feed"] }, exists);

    private static string FolderPath(MigrationPlan plan, Guid? key)
    {
        var parts = new List<string>();
        while (key is not null)
        {
            var folder = plan.Media.Single(m => m.Key == key);
            parts.Insert(0, folder.Name);
            key = folder.ParentKey;
        }

        return string.Join("/", parts);
    }

    [Fact]
    public void Attachment_key_is_attachment_guid()
    {
        var plan = Build(WithMedia([Attachment(A1, 9, "tandem.jpg", "Images", order: 1)]));

        var media = Assert.Single(plan.Media, m => m.MediaType != MediaType.Folder);
        Assert.Equal(A1, media.Key);
        Assert.Equal(MediaType.Image, media.MediaType);
        Assert.Equal("tandem.jpg", media.Name);
        Assert.Equal(A1, media.Source!.AttachmentGuid);
        Assert.Null(media.Source.MediaFile);
        Assert.Equal("Kentico import/Attachments/Catalog", FolderPath(plan, media.ParentKey));
        Assert.Equal(Keys.Derived("mediafolder", "attachments/catalog"), media.ParentKey);
    }

    [Fact]
    public void Library_folders_mirror_subfolders()
    {
        var file = MediaFile(GuidFor(2001), "Gallery", "boats/kayak.jpg");
        var plan = Build(WithMedia(files: [file, MediaFile(GuidFor(2002), "Gallery", "logo.png", ".png")]));

        var nested = plan.Media.Single(m => m.Key == GuidFor(2001));
        Assert.Equal("Kentico import/Libraries/Gallery/boats", FolderPath(plan, nested.ParentKey));
        Assert.Equal(file, nested.Source!.MediaFile);
        Assert.Null(nested.Source.AttachmentGuid);

        var top = plan.Media.Single(m => m.Key == GuidFor(2002));
        Assert.Equal("Kentico import/Libraries/Gallery", FolderPath(plan, top.ParentKey));
    }

    [Fact]
    public void File_node_becomes_media_not_document()
    {
        var plan = Build(WithMedia([Attachment(A1, 20, "banner.png", "FileAttachment", extension: ".png")]));

        Assert.DoesNotContain(plan.Documents, d => d.OldPath == "/Files/banner");
        Assert.DoesNotContain(plan.Skipped, s => s.OldPath == "/Files/banner");
        var media = plan.Media.Single(m => m.Key == A1);
        Assert.Equal("Kentico import/Attachments/Files", FolderPath(plan, media.ParentKey));
    }

    [Fact]
    public void Not_uploaded_file_is_reported_as_host_file()
    {
        var plan = Build(WithMedia([Attachment(A2, 21, "feed.xml", "FileAttachment", extension: ".xml")]));

        Assert.DoesNotContain(plan.Media, m => m.Key == A2);
        var skipped = Assert.Single(plan.Skipped, s => s.Category == SkipCategories.HostFile);
        Assert.Equal(A2.ToString(), skipped.Identifier);
        Assert.Equal("/Files/feed", skipped.OldPath);
    }

    [Fact]
    public void File_extension_decides_image_or_file()
    {
        var plan = Build(WithMedia([Attachment(A1, 20, "doc.pdf", "FileAttachment", extension: ".pdf"), Attachment(A2, 20, "pic.PNG", "FileAttachment", extension: "PNG")]));

        Assert.Equal(MediaType.File, plan.Media.Single(m => m.Key == A1).MediaType);
        Assert.Equal(MediaType.Image, plan.Media.Single(m => m.Key == A2).MediaType);
    }

    [Fact]
    public void Gallery_field_keeps_attachment_order()
    {
        var plan = Build(WithMedia(
        [
            Attachment(A3, 9, "c.jpg", "Images", order: 3),
            Attachment(A1, 9, "a.jpg", "Images", order: 1),
            Attachment(A2, 9, "b.jpg", "Images", order: 2),
        ]));

        var images = Assert.IsAssignableFrom<IReadOnlyList<Guid>>(plan.Documents.Single(d => d.OldPath == "/Catalog/Products/Tandem").Values["images"].Raw);
        Assert.Equal([A1, A2, A3], images);
        Assert.Equal("MediaMultiple", plan.Documents.Single(d => d.OldPath == "/Catalog/Products/Tandem").Values["images"].Editor);
    }

    [Fact]
    public void Single_file_field_points_at_its_attachment()
    {
        var plan = Build(WithMedia([Attachment(A1, 11, "teaser.jpg", "ArticleTeaserImage")]));

        Assert.Equal(A1, plan.Documents.Single(d => d.OldPath == "/Articles/First-trip").Values["teaserImage"].Raw);
        Assert.Null(plan.Documents.Single(d => d.OldPath == "/About").Values.GetValueOrDefault("teaserImage")?.Raw);
    }

    [Fact]
    public void Unsorted_attachment_becomes_unlinked_media()
    {
        var plan = Build(WithMedia([Attachment(A1, 9, "linked.jpg", "Images", order: 1), Attachment(A2, 9, "loose.jpg", unsorted: true)]));

        Assert.Contains(plan.Media, m => m.Key == A2);
        var images = Assert.IsAssignableFrom<IReadOnlyList<Guid>>(plan.Documents.Single(d => d.OldPath == "/Catalog/Products/Tandem").Values["images"].Raw);
        Assert.Equal([A1], images);
    }

    [Fact]
    public void Attachment_without_field_or_unsorted_flag_is_reported_unreferenced()
    {
        var plan = Build(WithMedia([Attachment(A1, 9, "stray.jpg")]));

        Assert.DoesNotContain(plan.Media, m => m.Key == A1);
        var skipped = Assert.Single(plan.Skipped, s => s.Category == SkipCategories.UnreferencedAttachment);
        Assert.Equal(A1.ToString(), skipped.Identifier);
    }

    [Fact]
    public void Attachment_of_a_page_that_is_not_migrated_is_reported_unreferenced()
    {
        var plan = Build(WithMedia([Attachment(A1, 4, "profile.jpg", "Images")]));

        Assert.DoesNotContain(plan.Media, m => m.Key == A1);
        Assert.Single(plan.Skipped, s => s.Category == SkipCategories.UnreferencedAttachment && s.OldPath == "/Account/Profile");
    }

    [Fact]
    public void Missing_library_file_is_reported_when_the_caller_says_so()
    {
        var present = MediaFile(GuidFor(2001), "Gallery", "here.jpg");
        var gone = MediaFile(GuidFor(2002), "Gallery", "gone.jpg");

        var plan = Build(WithMedia(files: [present, gone]), exists: f => f.FileGuid == present.FileGuid);

        Assert.Contains(plan.Media, m => m.Key == present.FileGuid);
        Assert.DoesNotContain(plan.Media, m => m.Key == gone.FileGuid);
        var skipped = Assert.Single(plan.Skipped, s => s.Category == SkipCategories.MissingFile);
        Assert.Equal(gone.FileGuid.ToString(), skipped.Identifier);
    }

    [Fact]
    public void History_versions_are_not_planned()
    {
        // The reader returns current versions only; the plan holds exactly those and reports no history.
        var plan = Build(WithMedia([Attachment(A1, 9, "current.jpg", "Images", order: 1)]));

        Assert.Single(plan.Media, m => m.MediaType != MediaType.Folder);
        Assert.DoesNotContain(plan.Skipped, s => s.Category == SkipCategories.AttachmentHistory);
    }

    [Fact]
    public void Folders_come_before_their_content_and_are_deterministic()
    {
        var site = WithMedia(
            [Attachment(A1, 9, "a.jpg", "Images", order: 1), Attachment(A2, 11, "b.jpg", "ArticleTeaserImage")],
            [MediaFile(GuidFor(2001), "Gallery", "boats/kayak.jpg")]);

        var plan = Build(site);

        var seen = new HashSet<Guid>();
        foreach (var media in plan.Media)
        {
            Assert.True(media.ParentKey is null || seen.Contains(media.ParentKey.Value), media.Name);
            seen.Add(media.Key);
        }

        var reversed = site with { Attachments = [.. site.Attachments.Reverse()] };
        Assert.Equal(JsonSerializer.Serialize(plan), JsonSerializer.Serialize(Build(reversed)));
    }
}
