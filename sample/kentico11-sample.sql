-- Kentico 11 sample database for the migrator: an invented kayak shop.
-- Table and column names and types follow a real Kentico 11 database, limited to what the reader uses.
-- All content here is invented. Run with sqlcmd (scripts/restore-sample.sh); batches are separated by GO.
-- Never point this script at a real database: it drops and recreates Kentico11Sample.

USE master;
GO
IF DB_ID(N'Kentico11Sample') IS NOT NULL
BEGIN
    ALTER DATABASE Kentico11Sample SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE Kentico11Sample;
END
GO
CREATE DATABASE Kentico11Sample;
GO
USE Kentico11Sample;
GO

-- ============================================================================
-- Part 1: schema, settings, site, base classes, root
-- ============================================================================

CREATE TABLE CMS_SettingsKey (
    KeyID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    KeyName nvarchar(100) NOT NULL,
    KeyDisplayName nvarchar(200) NOT NULL,
    KeyValue nvarchar(max) NULL,
    KeyType nvarchar(50) NOT NULL,
    SiteID int NULL,
    KeyGUID uniqueidentifier NOT NULL,
    KeyLastModified datetime2(7) NOT NULL
);

CREATE TABLE CMS_Site (
    SiteID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SiteName nvarchar(100) NOT NULL,
    SiteDisplayName nvarchar(200) NOT NULL,
    SiteStatus nvarchar(20) NOT NULL,
    SiteDomainName nvarchar(400) NOT NULL,
    SiteGUID uniqueidentifier NOT NULL,
    SiteLastModified datetime2(7) NOT NULL
);

CREATE TABLE CMS_Class (
    ClassID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ClassDisplayName nvarchar(100) NOT NULL,
    ClassName nvarchar(100) NOT NULL,
    ClassIsDocumentType bit NOT NULL,
    ClassIsCoupledClass bit NOT NULL,
    ClassFormDefinition nvarchar(max) NOT NULL,
    ClassTableName nvarchar(100) NULL,
    ClassLastModified datetime2(7) NOT NULL,
    ClassGUID uniqueidentifier NOT NULL
);

CREATE TABLE CMS_PageTemplate (
    PageTemplateID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    PageTemplateDisplayName nvarchar(200) NOT NULL,
    PageTemplateCodeName nvarchar(100) NOT NULL,
    PageTemplateWebParts nvarchar(max) NULL
);

CREATE TABLE CMS_Tree (
    NodeID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    NodeAliasPath nvarchar(450) NOT NULL,
    NodeName nvarchar(100) NOT NULL,
    NodeAlias nvarchar(50) NOT NULL,
    NodeClassID int NOT NULL,
    NodeParentID int NULL,
    NodeLevel int NOT NULL,
    NodeSiteID int NOT NULL,
    NodeGUID uniqueidentifier NOT NULL,
    NodeOrder int NULL,
    NodeSKUID int NULL,
    NodeLinkedNodeID int NULL
);

CREATE TABLE CMS_Document (
    DocumentID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    DocumentName nvarchar(100) NOT NULL,
    DocumentModifiedWhen datetime2(7) NULL,
    DocumentForeignKeyValue int NULL,
    DocumentCreatedWhen datetime2(7) NULL,
    DocumentPublishFrom datetime2(7) NULL,
    DocumentPublishTo datetime2(7) NULL,
    DocumentUrlPath nvarchar(450) NULL,
    DocumentCulture nvarchar(10) NOT NULL,
    DocumentNodeID int NOT NULL,
    DocumentPageTitle nvarchar(max) NULL,
    DocumentPageKeyWords nvarchar(max) NULL,
    DocumentPageDescription nvarchar(max) NULL,
    DocumentShowInSiteMap bit NOT NULL,
    DocumentMenuItemHideInNavigation bit NOT NULL,
    DocumentPageTemplateID int NULL,
    DocumentContent nvarchar(max) NULL,
    DocumentExtensions nvarchar(100) NULL,
    DocumentIsArchived bit NULL,
    DocumentGUID uniqueidentifier NULL,
    DocumentCanBePublished bit NOT NULL
);

CREATE TABLE CMS_DocumentAlias (
    AliasID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    AliasNodeID int NOT NULL,
    AliasCulture nvarchar(20) NULL,
    AliasURLPath nvarchar(450) NULL,
    AliasExtensions nvarchar(100) NULL,
    AliasWildcardRule nvarchar(440) NULL,
    AliasPriority int NULL,
    AliasGUID uniqueidentifier NULL,
    AliasLastModified datetime2(7) NOT NULL,
    AliasSiteID int NOT NULL
);

CREATE TABLE CMS_Attachment (
    AttachmentID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    AttachmentName nvarchar(255) NOT NULL,
    AttachmentExtension nvarchar(50) NOT NULL,
    AttachmentSize int NOT NULL,
    AttachmentMimeType nvarchar(100) NOT NULL,
    AttachmentBinary varbinary(max) NULL,
    AttachmentImageWidth int NULL,
    AttachmentImageHeight int NULL,
    AttachmentDocumentID int NULL,
    AttachmentGUID uniqueidentifier NOT NULL,
    AttachmentIsUnsorted bit NULL,
    AttachmentOrder int NULL,
    AttachmentGroupGUID uniqueidentifier NULL,
    AttachmentFormGUID uniqueidentifier NULL,
    AttachmentTitle nvarchar(250) NULL,
    AttachmentDescription nvarchar(max) NULL,
    AttachmentLastModified datetime2(7) NOT NULL
);

CREATE TABLE CMS_AttachmentHistory (
    AttachmentHistoryID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    AttachmentName nvarchar(255) NOT NULL,
    AttachmentExtension nvarchar(50) NOT NULL,
    AttachmentSize int NOT NULL,
    AttachmentMimeType nvarchar(100) NOT NULL,
    AttachmentBinary varbinary(max) NULL,
    AttachmentImageWidth int NULL,
    AttachmentImageHeight int NULL,
    AttachmentDocumentID int NOT NULL,
    AttachmentGUID uniqueidentifier NOT NULL,
    AttachmentIsUnsorted bit NULL,
    AttachmentOrder int NULL,
    AttachmentGroupGUID uniqueidentifier NULL,
    AttachmentTitle nvarchar(250) NULL,
    AttachmentLastModified datetime2(7) NOT NULL
);

CREATE TABLE Media_Library (
    LibraryID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    LibraryName nvarchar(250) NOT NULL,
    LibraryDisplayName nvarchar(250) NOT NULL,
    LibraryFolder nvarchar(250) NOT NULL,
    LibrarySiteID int NOT NULL,
    LibraryGUID uniqueidentifier NULL,
    LibraryLastModified datetime2(7) NULL
);

CREATE TABLE Media_File (
    FileID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    FileName nvarchar(250) NOT NULL,
    FileTitle nvarchar(250) NOT NULL,
    FileDescription nvarchar(max) NOT NULL,
    FileExtension nvarchar(50) NOT NULL,
    FileMimeType nvarchar(100) NOT NULL,
    FilePath nvarchar(450) NOT NULL,
    FileSize bigint NOT NULL,
    FileImageWidth int NULL,
    FileImageHeight int NULL,
    FileGUID uniqueidentifier NOT NULL,
    FileLibraryID int NOT NULL,
    FileSiteID int NOT NULL,
    FileCreatedWhen datetime2(7) NOT NULL,
    FileModifiedWhen datetime2(7) NOT NULL
);

CREATE TABLE COM_SKU (
    SKUID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    SKUName nvarchar(440) NOT NULL,
    SKUNumber nvarchar(200) NULL,
    SKUPrice decimal(18,9) NOT NULL,
    SKUEnabled bit NOT NULL,
    SKUAvailableItems int NULL,
    SKURetailPrice decimal(18,9) NULL
);

-- Coupled table of CMS.MenuItem (stock Kentico class).
CREATE TABLE CONTENT_MenuItem (
    MenuItemID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    MenuItemName nvarchar(450) NOT NULL,
    MenuItemTeaserImage uniqueidentifier NULL,
    MenuItemGroup nvarchar(100) NULL
);
GO

INSERT INTO CMS_Site (SiteName, SiteDisplayName, SiteStatus, SiteDomainName, SiteGUID, SiteLastModified)
VALUES (N'KayakShop', N'Kayak Shop', N'RUNNING', N'kayaks.example', '8b5d1a40-3c2e-4f6a-9d71-0a1b2c3d4e01', '2018-03-01T09:00:00');

-- CMSDBVersion is global (SiteID null); the other two are per site.
INSERT INTO CMS_SettingsKey (KeyName, KeyDisplayName, KeyValue, KeyType, SiteID, KeyGUID, KeyLastModified) VALUES
(N'CMSDBVersion', N'Database version', N'11.0', N'string', NULL, '1f0c7e11-0000-4a00-8000-000000000001', '2018-03-01T09:00:00'),
(N'CMSDefaultCultureCode', N'Default content culture', N'en-US', N'string', 1, '1f0c7e11-0000-4a00-8000-000000000002', '2018-03-01T09:00:00'),
(N'CMSDefaultAliasPath', N'Default alias path', N'/Home', N'string', 1, '1f0c7e11-0000-4a00-8000-000000000003', '2018-03-01T09:00:00');
GO

SET IDENTITY_INSERT CMS_Class ON;
INSERT INTO CMS_Class (ClassID, ClassDisplayName, ClassName, ClassIsDocumentType, ClassIsCoupledClass, ClassFormDefinition, ClassTableName, ClassLastModified, ClassGUID) VALUES
(1, N'Root', N'CMS.Root', 1, 0, N'<form version="2" />', NULL, '2018-03-01T09:00:00', 'c1a55000-0000-4000-8000-000000000001'),
(2, N'Folder', N'CMS.Folder', 1, 0, N'<form version="2" />', NULL, '2018-03-01T09:00:00', 'c1a55000-0000-4000-8000-000000000002'),
(3, N'Menu item', N'CMS.MenuItem', 1, 1,
 N'<form version="2"><field column="MenuItemID" columntype="integer" guid="b1a0c0de-0001-4000-8000-000000000001" isPK="true" publicfield="false" visibility="none" /><field column="MenuItemName" columnsize="450" columntype="text" guid="b1a0c0de-0001-4000-8000-000000000002" publicfield="false" translatefield="true" visible="true"><properties><fieldcaption>Page name</fieldcaption></properties><settings><AutoCompleteEnableCaching>False</AutoCompleteEnableCaching><AutoCompleteFirstRowSelected>False</AutoCompleteFirstRowSelected><AutoCompleteShowOnlyCurrentWordInCompletionListItem>False</AutoCompleteShowOnlyCurrentWordInCompletionListItem><controlname>TextBoxControl</controlname><FilterMode>False</FilterMode><Trim>True</Trim></settings></field><field allowempty="true" column="MenuItemTeaserImage" columntype="file" guid="b1a0c0de-0001-4000-8000-000000000003" publicfield="false" translatefield="true" visibility="none" visible="true"><properties><fieldcaption>Teaser image</fieldcaption></properties><settings><Autoresize_Hashtable>True</Autoresize_Hashtable><controlname>directuploadcontrol</controlname><Extensions>inherit</Extensions></settings></field><field allowempty="true" column="MenuItemGroup" columnsize="100" columntype="text" guid="b1a0c0de-0001-4000-8000-000000000004" publicfield="false" spellcheck="false" translatefield="true" visibility="none" visible="true"><properties><fieldcaption>Menu group</fieldcaption></properties><settings><controlname>dropdownlistcontrol</controlname><Options>&lt;item value="" text="(none)" /&gt;&lt;item value="top" text="Top" /&gt;&lt;item value="footer" text="Footer" /&gt;</Options></settings></field></form>',
 N'CONTENT_MenuItem', '2018-03-01T09:00:00', 'c1a55000-0000-4000-8000-000000000003');
SET IDENTITY_INSERT CMS_Class OFF;

-- Root node and its document (a real site always has both).
SET IDENTITY_INSERT CMS_Tree ON;
INSERT INTO CMS_Tree (NodeID, NodeAliasPath, NodeName, NodeAlias, NodeClassID, NodeParentID, NodeLevel, NodeSiteID, NodeGUID, NodeOrder, NodeSKUID, NodeLinkedNodeID)
VALUES (1, N'/', N'/', N'/', 1, NULL, 0, 1, '0de50000-0000-4000-8000-000000000001', 1, NULL, NULL);
SET IDENTITY_INSERT CMS_Tree OFF;

SET IDENTITY_INSERT CMS_Document ON;
INSERT INTO CMS_Document (DocumentID, DocumentName, DocumentModifiedWhen, DocumentForeignKeyValue, DocumentCreatedWhen, DocumentCulture, DocumentNodeID,
    DocumentShowInSiteMap, DocumentMenuItemHideInNavigation, DocumentIsArchived, DocumentGUID, DocumentCanBePublished)
VALUES (1, N'Kayak Shop', '2018-03-01T09:00:00', NULL, '2018-03-01T09:00:00', N'en-US', 1, 0, 0, 0, 'd0c50000-0000-4000-8000-000000000001', 1);
SET IDENTITY_INSERT CMS_Document OFF;
GO

-- ============================================================================
-- Part 2: pages (page types, templates, tree, documents)
-- ============================================================================

-- Coupled tables of the other page types.
CREATE TABLE CONTENT_Article (
    ArticleID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ArticleName nvarchar(450) NOT NULL,
    ArticleTeaserText nvarchar(max) NULL,
    ArticleTeaserImage uniqueidentifier NULL,
    ArticleText nvarchar(max) NULL
);

CREATE TABLE DEMO_Brand (
    BrandID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name nvarchar(200) NOT NULL,
    HeaderText nvarchar(400) NULL,
    TopText nvarchar(max) NULL,
    BottomText nvarchar(max) NULL,
    Synonyms nvarchar(450) NULL,
    MetaTitle nvarchar(200) NULL,
    MetaDescription nvarchar(500) NULL,
    MetaKeywords nvarchar(400) NULL,
    [Order] int NULL
);

-- The gallery field "Images" is an attachment group: it has no column in the table.
CREATE TABLE DEMO_Product (
    ProductID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ProductName nvarchar(200) NOT NULL,
    Featured bit NULL,
    ShippingInformation nvarchar(max) NULL,
    ShippingImage nvarchar(50) NULL
);
GO

SET IDENTITY_INSERT CMS_Class ON;
INSERT INTO CMS_Class (ClassID, ClassDisplayName, ClassName, ClassIsDocumentType, ClassIsCoupledClass, ClassFormDefinition, ClassTableName, ClassLastModified, ClassGUID) VALUES
(4, N'Article', N'CMS.Article', 1, 1,
 N'<form version="2"><field column="ArticleID" columntype="integer" guid="b1a0c0de-0004-4000-8000-000000000001" isPK="true" publicfield="false" visibility="none" /><field column="ArticleName" columnsize="450" columntype="text" guid="b1a0c0de-0004-4000-8000-000000000002" publicfield="false" visible="true"><properties><fieldcaption>Article name</fieldcaption></properties><settings><controlname>TextBoxControl</controlname></settings></field><field allowempty="true" column="ArticleTeaserText" columntype="longtext" guid="b1a0c0de-0004-4000-8000-000000000003" publicfield="false" visible="true"><properties><fieldcaption>Teaser text</fieldcaption></properties><settings><controlname>TextAreaControl</controlname></settings></field><field allowempty="true" column="ArticleTeaserImage" columntype="file" guid="b1a0c0de-0004-4000-8000-000000000004" publicfield="false" visible="true"><properties><fieldcaption>Teaser image</fieldcaption></properties><settings><controlname>directuploadcontrol</controlname><Extensions>inherit</Extensions></settings></field><field allowempty="true" column="ArticleText" columntype="longtext" guid="b1a0c0de-0004-4000-8000-000000000005" publicfield="false" visible="true"><properties><fieldcaption>Text</fieldcaption></properties><settings><controlname>HtmlAreaControl</controlname></settings></field></form>',
 N'CONTENT_Article', '2018-03-01T09:00:00', 'c1a55000-0000-4000-8000-000000000004'),
(6, N'Brand', N'DEMO.Brand', 1, 1,
 N'<form version="2"><field column="BrandID" columntype="integer" guid="b1a0c0de-0006-4000-8000-000000000001" isPK="true" publicfield="false" visibility="none" /><field column="Name" columnsize="200" columntype="text" guid="b1a0c0de-0006-4000-8000-000000000002" publicfield="false" visible="true"><properties><fieldcaption>Name</fieldcaption></properties><settings><controlname>TextBoxControl</controlname></settings></field><field allowempty="true" column="HeaderText" columnsize="400" columntype="text" guid="b1a0c0de-0006-4000-8000-000000000003" publicfield="false" visible="true"><properties><fieldcaption>Header text</fieldcaption></properties><settings><controlname>TextBoxControl</controlname></settings></field><field allowempty="true" column="TopText" columntype="longtext" guid="b1a0c0de-0006-4000-8000-000000000004" publicfield="false" visible="true"><properties><fieldcaption>Top text</fieldcaption></properties><settings><controlname>HtmlAreaControl</controlname></settings></field><field allowempty="true" column="BottomText" columntype="longtext" guid="b1a0c0de-0006-4000-8000-000000000005" publicfield="false" visible="true"><properties><fieldcaption>Bottom text</fieldcaption></properties><settings><controlname>HtmlAreaControl</controlname></settings></field><field allowempty="true" column="Synonyms" columnsize="450" columntype="text" guid="b1a0c0de-0006-4000-8000-000000000006" publicfield="false" visible="true"><properties><fieldcaption>Synonyms</fieldcaption></properties><settings><controlname>TextBoxControl</controlname></settings></field><field allowempty="true" column="MetaTitle" columnsize="200" columntype="text" guid="b1a0c0de-0006-4000-8000-000000000007" publicfield="false" visible="true"><properties><fieldcaption>Meta title</fieldcaption></properties><settings><controlname>TextBoxControl</controlname></settings></field><field allowempty="true" column="MetaDescription" columnsize="500" columntype="text" guid="b1a0c0de-0006-4000-8000-000000000008" publicfield="false" visible="true"><properties><fieldcaption>Meta description</fieldcaption></properties><settings><controlname>TextAreaControl</controlname></settings></field><field allowempty="true" column="MetaKeywords" columnsize="400" columntype="text" guid="b1a0c0de-0006-4000-8000-000000000009" publicfield="false" visible="true"><properties><fieldcaption>Meta keywords</fieldcaption></properties><settings><controlname>TextBoxControl</controlname></settings></field><field allowempty="true" column="Order" columntype="integer" guid="b1a0c0de-0006-4000-8000-000000000010" publicfield="false" visible="true"><properties><fieldcaption>Order</fieldcaption></properties><settings><controlname>TextBoxControl</controlname></settings></field></form>',
 N'DEMO_Brand', '2018-03-01T09:00:00', 'c1a55000-0000-4000-8000-000000000006'),
(7, N'Product', N'DEMO.Product', 1, 1,
 N'<form version="2"><field column="ProductID" columntype="integer" guid="b1a0c0de-0007-4000-8000-000000000001" isPK="true" publicfield="false" visibility="none" /><field column="ProductName" columnsize="200" columntype="text" guid="b1a0c0de-0007-4000-8000-000000000002" publicfield="false" visible="true"><properties><fieldcaption>Product name</fieldcaption></properties><settings><controlname>TextBoxControl</controlname></settings></field><field allowempty="true" column="Featured" columntype="boolean" guid="b1a0c0de-0007-4000-8000-000000000003" publicfield="false" visible="true"><properties><defaultvalue>False</defaultvalue><fieldcaption>Featured</fieldcaption></properties><settings><controlname>CheckBoxControl</controlname></settings></field><field allowempty="true" column="ShippingInformation" columntype="longtext" guid="b1a0c0de-0007-4000-8000-000000000004" publicfield="false" visible="true"><properties><fieldcaption>Shipping information</fieldcaption></properties><settings><controlname>HtmlAreaControl</controlname></settings></field><field allowempty="true" column="ShippingImage" columnsize="50" columntype="text" guid="b1a0c0de-0007-4000-8000-000000000005" publicfield="false" visible="true"><properties><fieldcaption>Shipping image</fieldcaption></properties><settings><controlname>RadioButtonsControl</controlname><Options>standard;Standard shipping
freight;Freight shipping</Options><RepeatDirection>vertical</RepeatDirection></settings></field><field allowempty="true" column="Images" columntype="docattachments" guid="b1a0c0de-0007-4000-8000-000000000006" publicfield="false" visible="true"><properties><fieldcaption>Images</fieldcaption></properties><settings><Allowed_Extensions>bmp;gif;jpg;jpeg;png</Allowed_Extensions><ChangeOrder>True</ChangeOrder><controlname>DocumentAttachmentsControl</controlname><Extensions>inherit</Extensions><Paging>True</Paging></settings></field></form>',
 N'DEMO_Product', '2018-03-01T09:00:00', 'c1a55000-0000-4000-8000-000000000007');
SET IDENTITY_INSERT CMS_Class OFF;

-- Page templates with editable text web parts. Document content keeps regions in another order on purpose.
SET IDENTITY_INSERT CMS_PageTemplate ON;
INSERT INTO CMS_PageTemplate (PageTemplateID, PageTemplateDisplayName, PageTemplateCodeName, PageTemplateWebParts) VALUES
(1, N'Text page', N'TextPage',
 N'<page><webpartzone id="zoneMain" v="1"><webpart controlid="text" guid="e7e10001-0000-4000-8000-000000000001" type="editabletext" v="1"><property name="regiontitle">Main text</property><property name="regiontype">HtmlEditor</property></webpart></webpartzone></page>'),
(2, N'Story page', N'StoryPage',
 N'<page><webpartzone id="zoneTop" v="1"><webpart controlid="menu" guid="e7e10002-0000-4000-8000-000000000001" type="repeater" v="1"><property name="classnames">CMS.MenuItem</property></webpart><webpart controlid="storyintro" guid="e7e10002-0000-4000-8000-000000000002" type="editabletext" v="1"><property name="regiontitle">Intro</property></webpart></webpartzone><webpartzone id="zoneMain" v="1"><webpart controlid="storybody" guid="e7e10002-0000-4000-8000-000000000003" type="editabletext" v="1"><property name="regiontitle">Story</property></webpart></webpartzone></page>'),
(3, N'Listing page', N'ListingPage',
 N'<page><webpartzone id="zoneMain" v="1"><webpart controlid="list" guid="e7e10003-0000-4000-8000-000000000001" type="repeater" v="1"><property name="classnames">CMS.Article</property></webpart></webpartzone></page>');
SET IDENTITY_INSERT CMS_PageTemplate OFF;
GO

-- Coupled rows.
SET IDENTITY_INSERT CONTENT_MenuItem ON;
INSERT INTO CONTENT_MenuItem (MenuItemID, MenuItemName, MenuItemTeaserImage, MenuItemGroup) VALUES
(1, N'Home', NULL, N'top'),
(2, N'About', NULL, N'top'),
(3, N'Story', NULL, N'top'),
(4, N'Contact', NULL, N'top'),
(5, N'Articles', NULL, N'top'),
(6, N'Brands', NULL, N'top'),
(7, N'Products', NULL, N'top'),
(8, N'Profile', NULL, NULL),
(9, N'Draft guide', NULL, NULL),
(10, N'Old offer', NULL, NULL),
(11, N'Spring sale', NULL, NULL);
SET IDENTITY_INSERT CONTENT_MenuItem OFF;

SET IDENTITY_INSERT CONTENT_Article ON;
INSERT INTO CONTENT_Article (ArticleID, ArticleName, ArticleTeaserText, ArticleTeaserImage, ArticleText) VALUES
(1, N'Paddling basics', N'Your first strokes, explained.', NULL, N'<p>Hold the paddle with both hands and rotate your torso.</p>'),
(2, N'Choosing a kayak', N'Touring, river or sea: which one suits you.', NULL, N'<p>Length and width decide how fast and how stable a kayak feels.</p>'),
(3, N'Winter storage', N'Keep your boat in shape between seasons.', NULL, N'<p>Store the kayak off the ground, away from direct sun.</p>');
SET IDENTITY_INSERT CONTENT_Article OFF;

-- Brand 7 (Driftwood) has no page: a row without a document.
SET IDENTITY_INSERT DEMO_Brand ON;
INSERT INTO DEMO_Brand (BrandID, Name, HeaderText, TopText, BottomText, Synonyms, MetaTitle, MetaDescription, MetaKeywords, [Order]) VALUES
(1, N'Aquila', N'Aquila kayaks', N'<p>Lightweight touring boats.</p>', N'<p>Made in small batches.</p>', N'aquila;aquila kayak', N'Aquila kayaks for sale', N'Aquila touring kayaks.', N'aquila,kayak', 1),
(2, N'Kestrel', N'Kestrel kayaks', N'<p>Fast and narrow.</p>', NULL, NULL, N'Kestrel kayaks', N'Kestrel sea kayaks.', N'kestrel', 2),
(3, N'Tempest', N'Tempest kayaks', N'<p>Stable in rough water.</p>', N'<p>Rudder optional.</p>', N'tempest', NULL, NULL, NULL, 3),
(4, N'Nordic', N'Nordic kayaks', N'<p>Classic lines.</p>', NULL, NULL, N'Nordic kayaks', NULL, N'nordic,classic', 4),
(5, N'Sea Spirit', N'Sea Spirit kayaks', N'<p>Comfortable for long days.</p>', N'<p>Three sizes.</p>', N'sea spirit;seaspirit', N'Sea Spirit kayaks', N'Sea Spirit sea kayaks.', N'sea spirit', 5),
(6, N'Wave Rider', N'Wave Rider kayaks', N'<p>Playful surf boats.</p>', NULL, N'wave rider', N'Wave Rider kayaks', NULL, NULL, 6),
(7, N'Driftwood', N'Driftwood kayaks', N'<p>Brand without a page.</p>', NULL, NULL, NULL, NULL, NULL, 7);
SET IDENTITY_INSERT DEMO_Brand OFF;

SET IDENTITY_INSERT COM_SKU ON;
INSERT INTO COM_SKU (SKUID, SKUName, SKUNumber, SKUPrice, SKUEnabled, SKUAvailableItems, SKURetailPrice) VALUES
(1, N'Pacific Touring Kayak', N'KX-PT-100', 1299.000000000, 1, 12, 1499.000000000),
(2, N'River Runner Kayak', N'KX-RR-200', 899.500000000, 1, 4, NULL);
SET IDENTITY_INSERT COM_SKU OFF;

SET IDENTITY_INSERT DEMO_Product ON;
INSERT INTO DEMO_Product (ProductID, ProductName, Featured, ShippingInformation, ShippingImage) VALUES
(1, N'Pacific Touring Kayak', 1, N'<p>Ships in 5 days on a pallet.</p>', N'freight'),
(2, N'River Runner Kayak', 0, N'<p>Ships in 2 days.</p>', N'standard');
SET IDENTITY_INSERT DEMO_Product OFF;
GO

-- Tree. Class ids: 1 Root, 2 Folder, 3 MenuItem, 4 Article, 6 Brand, 7 Product.
SET IDENTITY_INSERT CMS_Tree ON;
INSERT INTO CMS_Tree (NodeID, NodeAliasPath, NodeName, NodeAlias, NodeClassID, NodeParentID, NodeLevel, NodeSiteID, NodeGUID, NodeOrder, NodeSKUID, NodeLinkedNodeID) VALUES
(2,  N'/Home',                                    N'Home',                  N'Home',                  3, 1,  1, 1, '0de50000-0000-4000-8000-000000000002', 1,  NULL, NULL),
(3,  N'/About',                                   N'About',                 N'About',                 3, 1,  1, 1, '0de50000-0000-4000-8000-000000000003', 2,  NULL, NULL),
(4,  N'/Story',                                   N'Story',                 N'Story',                 3, 1,  1, 1, '0de50000-0000-4000-8000-000000000004', 3,  NULL, NULL),
(5,  N'/Contact',                                 N'Contact',               N'Contact',               3, 1,  1, 1, '0de50000-0000-4000-8000-000000000005', 4,  NULL, NULL),
(6,  N'/Articles',                                N'Articles',              N'Articles',              3, 1,  1, 1, '0de50000-0000-4000-8000-000000000006', 5,  NULL, NULL),
(7,  N'/Articles/Paddling-Basics',                N'Paddling basics',       N'Paddling-Basics',       4, 6,  2, 1, '0de50000-0000-4000-8000-000000000007', 1,  NULL, NULL),
(8,  N'/Articles/Choosing-A-Kayak',               N'Choosing a kayak',      N'Choosing-A-Kayak',      4, 6,  2, 1, '0de50000-0000-4000-8000-000000000008', 2,  NULL, NULL),
(9,  N'/Articles/Winter-Storage',                 N'Winter storage',        N'Winter-Storage',        4, 6,  2, 1, '0de50000-0000-4000-8000-000000000009', 3,  NULL, NULL),
(10, N'/Catalog',                                 N'Catalog',               N'Catalog',               2, 1,  1, 1, '0de50000-0000-4000-8000-000000000010', 6,  NULL, NULL),
(11, N'/Catalog/Brands',                          N'Brands',                N'Brands',                3, 10, 2, 1, '0de50000-0000-4000-8000-000000000011', 1,  NULL, NULL),
(12, N'/Catalog/Brands/Aquila',                   N'Aquila',                N'Aquila',                6, 11, 3, 1, '0de50000-0000-4000-8000-000000000012', 1,  NULL, NULL),
(13, N'/Catalog/Brands/Kestrel',                  N'Kestrel',               N'Kestrel',               6, 11, 3, 1, '0de50000-0000-4000-8000-000000000013', 2,  NULL, NULL),
(14, N'/Catalog/Brands/Tempest',                  N'Tempest',               N'Tempest',               6, 11, 3, 1, '0de50000-0000-4000-8000-000000000014', 3,  NULL, NULL),
(15, N'/Catalog/Brands/Nordic',                   N'Nordic',                N'Nordic',                6, 11, 3, 1, '0de50000-0000-4000-8000-000000000015', 4,  NULL, NULL),
(16, N'/Catalog/Brands/Sea-Spirit',               N'Sea Spirit',            N'Sea-Spirit',            6, 11, 3, 1, '0de50000-0000-4000-8000-000000000016', 5,  NULL, NULL),
(17, N'/Catalog/Brands/Wave-Rider',               N'Wave Rider',            N'Wave-Rider',            6, 11, 3, 1, '0de50000-0000-4000-8000-000000000017', 6,  NULL, NULL),
(18, N'/Catalog/Products',                        N'Products',              N'Products',              3, 10, 2, 1, '0de50000-0000-4000-8000-000000000018', 2,  NULL, NULL),
(19, N'/Catalog/Products/Pacific-Touring-Kayak',  N'Pacific Touring Kayak', N'Pacific-Touring-Kayak', 7, 18, 3, 1, '0de50000-0000-4000-8000-000000000019', 1,  1,    NULL),
(20, N'/Catalog/Products/River-Runner-Kayak',     N'River Runner Kayak',    N'River-Runner-Kayak',    7, 18, 3, 1, '0de50000-0000-4000-8000-000000000020', 2,  2,    NULL),
(21, N'/Account',                                 N'Account',               N'Account',               2, 1,  1, 1, '0de50000-0000-4000-8000-000000000021', 7,  NULL, NULL),
(22, N'/Account/Profile',                         N'Profile',               N'Profile',               3, 21, 2, 1, '0de50000-0000-4000-8000-000000000022', 1,  NULL, NULL),
(23, N'/Draft-Guide',                             N'Draft guide',           N'Draft-Guide',           3, 1,  1, 1, '0de50000-0000-4000-8000-000000000023', 8,  NULL, NULL),
(24, N'/Old-Offer',                               N'Old offer',             N'Old-Offer',             3, 1,  1, 1, '0de50000-0000-4000-8000-000000000024', 9,  NULL, NULL),
(25, N'/Spring-Sale',                             N'Spring sale',           N'Spring-Sale',           3, 1,  1, 1, '0de50000-0000-4000-8000-000000000025', 10, NULL, NULL);
SET IDENTITY_INSERT CMS_Tree OFF;
GO

-- Documents (DocumentID = NodeID). DocumentForeignKeyValue points at the coupled row.
SET IDENTITY_INSERT CMS_Document ON;
INSERT INTO CMS_Document (DocumentID, DocumentName, DocumentModifiedWhen, DocumentForeignKeyValue, DocumentCreatedWhen, DocumentPublishFrom, DocumentPublishTo, DocumentUrlPath,
    DocumentCulture, DocumentNodeID, DocumentPageTitle, DocumentPageKeyWords, DocumentPageDescription, DocumentShowInSiteMap, DocumentMenuItemHideInNavigation,
    DocumentPageTemplateID, DocumentContent, DocumentExtensions, DocumentIsArchived, DocumentGUID, DocumentCanBePublished) VALUES
(2, N'Home', '2018-06-01T10:00:00', 1, '2018-03-01T09:00:00', NULL, NULL, NULL, N'en-US', 2, N'Kayak Shop: touring, river and sea kayaks', N'kayak,kayaks', N'Kayaks and paddling gear.', 1, 0, 1,
 N'<content><webpart id="text;e7e10001-0000-4000-8000-000000000001"><![CDATA[<h1>Welcome to the Kayak Shop</h1><p>Boats and gear for every paddler.</p>]]></webpart></content>', NULL, 0, 'd0c50000-0000-4000-8000-000000000002', 1),
(3, N'About', '2018-06-02T10:00:00', 2, '2018-03-01T09:00:00', NULL, NULL, NULL, N'en-US', 3, N'About the Kayak Shop', NULL, N'Who we are.', 1, 0, 1,
 N'<content><webpart id="text;e7e10001-0000-4000-8000-000000000001"><![CDATA[<p>We have been selling kayaks since 2009 &amp; we love the water.</p>]]></webpart></content>', NULL, 0, 'd0c50000-0000-4000-8000-000000000003', 1),
(4, N'Story', '2018-06-03T10:00:00', 3, '2018-03-01T09:00:00', NULL, NULL, NULL, N'en-US', 4, N'Our story', N'story,history', N'How the shop started.', 1, 0, 2,
 N'<content><webpart id="storybody;e7e10002-0000-4000-8000-000000000003"><![CDATA[<p>The shop started in a garage with three boats.</p>]]></webpart><webpart id="storyintro;e7e10002-0000-4000-8000-000000000002"><![CDATA[<p>A short history of the Kayak Shop.</p>]]></webpart></content>', NULL, 0, 'd0c50000-0000-4000-8000-000000000004', 1),
(5, N'Contact', '2018-06-04T10:00:00', 4, '2018-03-01T09:00:00', NULL, NULL, NULL, N'en-US', 5, N'Contact us', NULL, NULL, 1, 0, 3, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000005', 1),
(6, N'Articles', '2018-06-05T10:00:00', 5, '2018-03-01T09:00:00', NULL, NULL, NULL, N'en-US', 6, N'Articles', NULL, NULL, 1, 0, 3, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000006', 1),
(7, N'Paddling basics', '2018-07-01T10:00:00', 1, '2018-07-01T09:00:00', NULL, NULL, NULL, N'en-US', 7, N'Paddling basics', N'paddling', N'Your first strokes, explained.', 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000007', 1),
(8, N'Choosing a kayak', '2018-07-02T10:00:00', 2, '2018-07-02T09:00:00', NULL, NULL, NULL, N'en-US', 8, N'Choosing a kayak', N'kayak,buying guide', N'Touring, river or sea.', 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000008', 1),
(9, N'Winter storage', '2018-07-03T10:00:00', 3, '2018-07-03T09:00:00', NULL, NULL, NULL, N'en-US', 9, N'Winter storage', NULL, NULL, 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000009', 1),
(10, N'Catalog', '2018-03-01T09:00:00', NULL, '2018-03-01T09:00:00', NULL, NULL, NULL, N'en-US', 10, NULL, NULL, NULL, 0, 1, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000010', 1),
(11, N'Brands', '2018-03-01T09:00:00', 6, '2018-03-01T09:00:00', NULL, NULL, NULL, N'en-US', 11, N'Brands', NULL, NULL, 1, 0, 3, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000011', 1),
(12, N'Aquila', '2018-08-01T10:00:00', 1, '2018-08-01T09:00:00', NULL, NULL, NULL, N'en-US', 12, N'Aquila', NULL, NULL, 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000012', 1),
(13, N'Kestrel', '2018-08-02T10:00:00', 2, '2018-08-02T09:00:00', NULL, NULL, NULL, N'en-US', 13, N'Kestrel', NULL, NULL, 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000013', 1),
(14, N'Tempest', '2018-08-03T10:00:00', 3, '2018-08-03T09:00:00', NULL, NULL, NULL, N'en-US', 14, N'Tempest', NULL, NULL, 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000014', 1),
(15, N'Nordic', '2018-08-04T10:00:00', 4, '2018-08-04T09:00:00', NULL, NULL, NULL, N'en-US', 15, N'Nordic', NULL, NULL, 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000015', 1),
(16, N'Sea Spirit', '2018-08-05T10:00:00', 5, '2018-08-05T09:00:00', NULL, NULL, NULL, N'en-US', 16, N'Sea Spirit', NULL, NULL, 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000016', 1),
(17, N'Wave Rider', '2018-08-06T10:00:00', 6, '2018-08-06T09:00:00', NULL, NULL, NULL, N'en-US', 17, N'Wave Rider', NULL, NULL, 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000017', 1),
(18, N'Products', '2018-03-01T09:00:00', 7, '2018-03-01T09:00:00', NULL, NULL, NULL, N'en-US', 18, N'Products', NULL, NULL, 1, 0, 3, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000018', 1),
(19, N'Pacific Touring Kayak', '2018-09-01T10:00:00', 1, '2018-09-01T09:00:00', NULL, NULL, NULL, N'en-US', 19, N'Pacific Touring Kayak', N'touring kayak', N'A fast touring kayak.', 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000019', 1),
(20, N'River Runner Kayak', '2018-09-02T10:00:00', 2, '2018-09-02T09:00:00', NULL, NULL, NULL, N'en-US', 20, N'River Runner Kayak', NULL, NULL, 1, 0, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000020', 1),
(21, N'Account', '2018-03-01T09:00:00', NULL, '2018-03-01T09:00:00', NULL, NULL, NULL, N'en-US', 21, NULL, NULL, NULL, 0, 1, NULL, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000021', 1),
(22, N'Profile', '2018-03-01T09:00:00', 8, '2018-03-01T09:00:00', NULL, NULL, NULL, N'en-US', 22, N'Your profile', NULL, NULL, 0, 1, 3, NULL, NULL, 0, 'd0c50000-0000-4000-8000-000000000022', 1),
-- Draft: not allowed to be published. Archived: also not publishable. Scheduled: publish window opens in 2099.
(23, N'Draft guide', '2018-10-01T10:00:00', 9, '2018-10-01T09:00:00', NULL, NULL, NULL, N'en-US', 23, N'Draft guide', NULL, NULL, 1, 0, 1,
 N'<content><webpart id="text;e7e10001-0000-4000-8000-000000000001"><![CDATA[<p>Work in progress.</p>]]></webpart></content>', NULL, 0, 'd0c50000-0000-4000-8000-000000000023', 0),
(24, N'Old offer', '2018-10-02T10:00:00', 10, '2018-04-01T09:00:00', NULL, NULL, NULL, N'en-US', 24, N'Old offer', NULL, NULL, 1, 0, 1,
 N'<content><webpart id="text;e7e10001-0000-4000-8000-000000000001"><![CDATA[<p>This offer has ended.</p>]]></webpart></content>', NULL, 1, 'd0c50000-0000-4000-8000-000000000024', 0),
(25, N'Spring sale', '2018-10-03T10:00:00', 11, '2018-10-03T09:00:00', '2099-01-01T00:00:00', NULL, NULL, N'en-US', 25, N'Spring sale', NULL, NULL, 1, 0, 1,
 N'<content><webpart id="text;e7e10001-0000-4000-8000-000000000001"><![CDATA[<p>Coming soon.</p>]]></webpart></content>', NULL, 0, 'd0c50000-0000-4000-8000-000000000025', 1);
SET IDENTITY_INSERT CMS_Document OFF;
GO
