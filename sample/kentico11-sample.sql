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

-- ============================================================================
-- Part 3: media (file nodes, attachments, media library)
-- ============================================================================

CREATE TABLE CONTENT_File (
    FileID int IDENTITY(1,1) NOT NULL PRIMARY KEY,
    FileName nvarchar(450) NOT NULL,
    FileDescription nvarchar(max) NULL,
    FileAttachment uniqueidentifier NULL
);
GO

SET IDENTITY_INSERT CMS_Class ON;
INSERT INTO CMS_Class (ClassID, ClassDisplayName, ClassName, ClassIsDocumentType, ClassIsCoupledClass, ClassFormDefinition, ClassTableName, ClassLastModified, ClassGUID) VALUES
(5, N'File', N'CMS.File', 1, 1,
 N'<form version="2"><field column="FileID" columntype="integer" guid="b1a0c0de-0005-4000-8000-000000000001" isPK="true" isunique="true" publicfield="false" visibility="none" /><field column="FileName" columnsize="450" columntype="text" guid="b1a0c0de-0005-4000-8000-000000000002" publicfield="false" visible="true"><properties><fieldcaption>File name</fieldcaption></properties><settings><controlname>TextBoxControl</controlname></settings></field><field allowempty="true" column="FileDescription" columntype="longtext" guid="b1a0c0de-0005-4000-8000-000000000003" publicfield="false" visible="true"><properties><fieldcaption>Description</fieldcaption></properties><settings><controlname>TextAreaControl</controlname></settings></field><field allowempty="true" column="FileAttachment" columntype="file" guid="b1a0c0de-0005-4000-8000-000000000004" publicfield="false" visible="true"><properties><fieldcaption>File</fieldcaption></properties><settings><controlname>directuploadcontrol</controlname><Extensions>inherit</Extensions></settings></field></form>',
 N'CONTENT_File', '2018-03-01T09:00:00', 'c1a55000-0000-4000-8000-000000000005');
SET IDENTITY_INSERT CMS_Class OFF;

-- File nodes: the guide has its binary in the database; the map is a file on the web server
-- (listed as not uploaded in the profile), so it has no attachment.
SET IDENTITY_INSERT CONTENT_File ON;
INSERT INTO CONTENT_File (FileID, FileName, FileDescription, FileAttachment) VALUES
(1, N'Paddle size guide', N'How to pick the right paddle length.', 'a77ac000-0000-4000-8000-000000000007'),
(2, N'Paddling map', N'Map of paddling spots (served from the web server).', NULL);
SET IDENTITY_INSERT CONTENT_File OFF;

SET IDENTITY_INSERT CMS_Tree ON;
INSERT INTO CMS_Tree (NodeID, NodeAliasPath, NodeName, NodeAlias, NodeClassID, NodeParentID, NodeLevel, NodeSiteID, NodeGUID, NodeOrder, NodeSKUID, NodeLinkedNodeID) VALUES
(26, N'/Downloads',                    N'Downloads',         N'Downloads',         2, 1,  1, 1, '0de50000-0000-4000-8000-000000000026', 11, NULL, NULL),
(27, N'/Downloads/Paddle-Size-Guide',  N'Paddle size guide', N'Paddle-Size-Guide', 5, 26, 2, 1, '0de50000-0000-4000-8000-000000000027', 1,  NULL, NULL),
(28, N'/Downloads/Paddling-Map',       N'Paddling map',      N'Paddling-Map',      5, 26, 2, 1, '0de50000-0000-4000-8000-000000000028', 2,  NULL, NULL);
SET IDENTITY_INSERT CMS_Tree OFF;

SET IDENTITY_INSERT CMS_Document ON;
INSERT INTO CMS_Document (DocumentID, DocumentName, DocumentModifiedWhen, DocumentForeignKeyValue, DocumentCreatedWhen, DocumentCulture, DocumentNodeID,
    DocumentShowInSiteMap, DocumentMenuItemHideInNavigation, DocumentIsArchived, DocumentGUID, DocumentCanBePublished) VALUES
(26, N'Downloads',         '2018-11-01T10:00:00', NULL, '2018-11-01T09:00:00', N'en-US', 26, 0, 1, 0, 'd0c50000-0000-4000-8000-000000000026', 1),
(27, N'Paddle size guide', '2018-11-02T10:00:00', 1,    '2018-11-02T09:00:00', N'en-US', 27, 0, 1, 0, 'd0c50000-0000-4000-8000-000000000027', 1),
(28, N'Paddling map',      '2018-11-03T10:00:00', 2,    '2018-11-03T09:00:00', N'en-US', 28, 0, 1, 0, 'd0c50000-0000-4000-8000-000000000028', 1);
SET IDENTITY_INSERT CMS_Document OFF;

-- The article teaser is a file field: the column holds the attachment guid.
UPDATE CONTENT_Article SET ArticleTeaserImage = 'a77ac000-0000-4000-8000-000000000006' WHERE ArticleID = 1;
GO

-- Current attachments. Product galleries use the attachment group of the Images field;
-- attachment 5 is an unsorted attachment of a page; attachment 6 is a teaser (file field).
INSERT INTO CMS_Attachment (AttachmentName, AttachmentExtension, AttachmentSize, AttachmentMimeType, AttachmentBinary, AttachmentImageWidth, AttachmentImageHeight,
    AttachmentDocumentID, AttachmentGUID, AttachmentIsUnsorted, AttachmentOrder, AttachmentGroupGUID, AttachmentTitle, AttachmentLastModified) VALUES
(N'pacific-front.jpg', N'.jpg', 1256, N'image/jpeg', 0xFFD8FFE000104A46494600010100000100010000FFDB0043000D090A0B0A080D0B0A0B0E0E0D0F13201513121213271C1E17202E2931302E292D2C333A4A3E333646372C2D405741464C4E525352323E5A615A50604A51524FFFDB0043010E0E0E131113261515264F352D354F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4FFFC00011080040006003012200021101031101FFC4001F0000010501010101010100000000000000000102030405060708090A0BFFC400B5100002010303020403050504040000017D01020300041105122131410613516107227114328191A1082342B1C11552D1F02433627282090A161718191A25262728292A3435363738393A434445464748494A535455565758595A636465666768696A737475767778797A838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE1E2E3E4E5E6E7E8E9EAF1F2F3F4F5F6F7F8F9FAFFC4001F0100030101010101010101010000000000000102030405060708090A0BFFC400B51100020102040403040705040400010277000102031104052131061241510761711322328108144291A1B1C109233352F0156272D10A162434E125F11718191A262728292A35363738393A434445464748494A535455565758595A636465666768696A737475767778797A82838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE2E3E4E5E6E7E8E9EAF2F3F4F5F6F7F8F9FAFFDA000C03010002110311003F00EE68A28AE839C28A28A0028A28A0086D6E61BBB75B8B77DF13E76B608CE0E3BFD2A6AC9F0BFF00C8BF6BFF0003FF00D0CD6B5392B36849DD5C28A28A430A28A2800A28ACDD5F59B6D2900972F33825635EBF53E8334D26DD909B4B734A992CB1C319926912341D598E00FC6B84BEF126A376CC2393ECF11E8B1F07AE7EF75CFD31590ECCEECEEC5998E4927249F5ADE341F5666EAAE87A3CBAC69B1465DAFADC81FDD70C7F21CD57FF00849347FF009FBFFC86FF00E15E7D455FD5E3DC8F6ACEC741D6F4DB4D1E0B7B8B9D92A6EDCBB18E32C4F61EF5B116B1A6CB1875BEB700FF0079C29FC8F35E6D4512A09BB82AAD687AA452C7346248644910F4653907F1A7D79523323ABA3156539041C107D6B5EC7C49A8DA328924FB4443AAC9C9EB9FBDD73F5CD44A83E8CB555753BEA2B3748D66DB554222CA4C80168DBAFD47A8CD69560D34ECCD134F60AF39D7A39E3D66EBED01C1790B29639CAE7E5C7B638FC315E8D55350D3AD7528447751EEDB9DAC0E1949F43FE455D39F23D499C7991E67457477DE12BA8D99ACA44993B2B1DAFD7A7A1E3BE47D2B1AE34EBDB60ED3DA4C8A870CC50ED1CE3AF4AEB8CE32D99CEE2D6E55A28A2AC90A28A2800A2AD5BE9D7B7211A0B499D5CE15821DA79C75E95B363E12BA9195AF644853BAA9DCFD7A7A0E3BE4FD2A2538C776528B7B19BA0C73C9ACDAFD9C392920662A7185CFCD9F6C71F8E2BD1AAA69FA75AE9B098ED63DBBB1B989CB311EA7FC8AB75C9527CEF43A211E54145145665851451400C9628E68CC73469221EAAC320FE155FF00B334EFF9F0B5FF00BF2BFE156E8A77682C54FECCD3BFE7C2D7FEFCAFF85588A28E18C470C691A0E8AA3007E14FA28BB6160A28A290051451401FFFD9, 96, 64, 19, 'a77ac000-0000-4000-8000-000000000001', 0, 1, 'b1a0c0de-0007-4000-8000-000000000006', N'Pacific front', '2018-11-04T10:00:00'),
(N'pacific-side.png', N'.png', 337, N'image/png', 0x89504E470D0A1A0A0000000D49484452000000600000004008020000006A56E5590000011849444154789CEDD9BB11C23010005159F55D1B0EA888803654046D50043915E015F20759DE4D1DDD1BDB333A4DCFD73BD9F7F2C2331388F30D820482043A1B50DCE7B8CFA99BBA03EA2D812081208120812081208120812081208120812081208120812081A0E9F85B8D58B1EE29B7471A06280EDC7BED07B7255074B309DCD06B03A0E8C6650FA976A0E8D86543A916A03815CD4AA6DF80E2B434CD4CB54031044D0353BEA64EAA1E2A5F53A77EB47C599DCA01F395756AC6F4B00A0904090409B402A81CBE7CF94BCB63C21B544637C201F9132BE31AD58C56F50F2A231A550EE5691E721F04B95184DC4943DE6A40DE8B41DEACF60774AE3CAC4202410241024102410241024102410241024102410241024102A5E53E53DD747E76E7F16B0000000049454E44AE426082, 96, 64, 19, 'a77ac000-0000-4000-8000-000000000002', 0, 2, 'b1a0c0de-0007-4000-8000-000000000006', N'Pacific side', '2018-11-04T10:00:00'),
(N'river-front.jpg', N'.jpg', 1255, N'image/jpeg', 0xFFD8FFE000104A46494600010100000100010000FFDB0043000D090A0B0A080D0B0A0B0E0E0D0F13201513121213271C1E17202E2931302E292D2C333A4A3E333646372C2D405741464C4E525352323E5A615A50604A51524FFFDB0043010E0E0E131113261515264F352D354F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4FFFC00011080040006003012200021101031101FFC4001F0000010501010101010100000000000000000102030405060708090A0BFFC400B5100002010303020403050504040000017D01020300041105122131410613516107227114328191A1082342B1C11552D1F02433627282090A161718191A25262728292A3435363738393A434445464748494A535455565758595A636465666768696A737475767778797A838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE1E2E3E4E5E6E7E8E9EAF1F2F3F4F5F6F7F8F9FAFFC4001F0100030101010101010101010000000000000102030405060708090A0BFFC400B51100020102040403040705040400010277000102031104052131061241510761711322328108144291A1B1C109233352F0156272D10A162434E125F11718191A262728292A35363738393A434445464748494A535455565758595A636465666768696A737475767778797A82838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE2E3E4E5E6E7E8E9EAF2F3F4F5F6F7F8F9FAFFDA000C03010002110311003F00EF28A28AE7340A28A2800A28A28022B5B986EEDD67B77DF1BE70D8233838EF52D657867FE4016DFF0003FF00D0CD6AD25AA2A6B964D20A28A299214514500145159DAB6B16DA5A012E5E5604AC6BD7EA7D06686EC38C5C9D91A34C9658E18CC9348B1A0EACC7007E35C35F788F50BA6611C9F678CF458F83D73F7BAE7E98ACA7667767762CCC72493924D66EA763AE38393F899E892EAFA6C51976BD8081FDD70C7F21CD57FF00848B48FF009FBFFC86FF00E15C0D14BDA3355838756CEBF42D6B4EB4D22082E2E36489BB2BB18E32C4F615AF16AFA6CB1875BD8003FDE70A7F23CD79CD1494DA2A7848C9B773D4229639A3124322C887A329C83F8D3EBCB91991D5D18AB29C820E0835AB63E23D42D5944927DA231D564E4F5CFDEEB9FAE6A954EE612C1C97C2CEF28ACED2758B6D51088B292A805A36EBF51EA335A35A277392517176615E79AEC73C7AC5CFDA0302F21652C7395CF18F6C7F2C57A1D54D434EB5D46211DD47BB6E76B0382A4FA1FF0022A651BA36A1555395D9E6F457457DE14BA8D99ACA45993B2B1DADD7A7A1E3BF1F4AC69F4FBDB60ED3DACC8A870CC50ED1CE3AF4AC9A68F4A35612D995A8A28A9340A28A2800A2ACC1A7DEDC84682D66757385608769E71D7A56CD8F852EA4656BD91614EEAA77375E9E838EFCFD2A926CCE55610DD99BA1473C9AC5B7D9C312920662A7185CF39F6C7F3C57A1D54D3F4EB5D3A231DAC7B7763731392C47A9FF0022ADD6B18D91E6D7AAAA4AE828A28AA300A28A280192C51CD198E68D6443D558641FC2ABFF006669FF00F3E36BFF007E57FC2ADD140D49AD8A9FD99A7FFCF8DAFF00DF95FF000AB11451C3188E18D6341D154600FC29F4500E4DEE145145020A28A2803FFFD9, 96, 64, 20, 'a77ac000-0000-4000-8000-000000000003', 0, 2, 'b1a0c0de-0007-4000-8000-000000000006', N'River front', '2018-11-04T10:00:00'),
(N'river-side.jpg', N'.jpg', 1165, N'image/jpeg', 0xFFD8FFE000104A46494600010100000100010000FFDB0043000D090A0B0A080D0B0A0B0E0E0D0F13201513121213271C1E17202E2931302E292D2C333A4A3E333646372C2D405741464C4E525352323E5A615A50604A51524FFFDB0043010E0E0E131113261515264F352D354F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4FFFC00011080040006003012200021101031101FFC4001F0000010501010101010100000000000000000102030405060708090A0BFFC400B5100002010303020403050504040000017D01020300041105122131410613516107227114328191A1082342B1C11552D1F02433627282090A161718191A25262728292A3435363738393A434445464748494A535455565758595A636465666768696A737475767778797A838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE1E2E3E4E5E6E7E8E9EAF1F2F3F4F5F6F7F8F9FAFFC4001F0100030101010101010101010000000000000102030405060708090A0BFFC400B51100020102040403040705040400010277000102031104052131061241510761711322328108144291A1B1C109233352F0156272D10A162434E125F11718191A262728292A35363738393A434445464748494A535455565758595A636465666768696A737475767778797A82838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE2E3E4E5E6E7E8E9EAF2F3F4F5F6F7F8F9FAFFDA000C03010002110311003F00EF28A28AE7340A28A2800A28A280238668E7896589B7237438C54954745FF90543FF0002FF00D08D5EA983E68A65495A4D051451544851451400514553BFD421B151BF2D230F9507F5F414A5251576351727645CA6BBA4685E465551D4B1C015CC5CEB17739211FCA43D93AF5F5EB54598B316624927249EF5CB2C5A5F0A3A63856F7675CF7F668859AE62207F75813F90A8BFB5EC3FE7BFFE38DFE15CAD159BC5CBA2345858F566FE99A8DA41A7C514B36D75CE46D27B9F6AD04BFB3740CB731007FBCC01FC8D7214528E265156B0E5868C9DEE76C8E922078D9594F42A720D3AB88562AC1949041C823B55EB6D62EE0203BF9A83B3F5EBEBD6B58E2D3F89194B0AD6CCEA68AA761A8437CA76656451F321FE9EA2AE575464A4AE8E6717176615C8EA692A6A1379A1B2CC4AE7BAF6FD2BAEA82EED20BB8C24E99C7DD23822B2AD49D48D91A51A9C92BB38EA2B5AE7429D096B675917B29E1BAFE559F2DA5CC218CB048A14E0B1538FCEBCF9539477477C6A465B321A28A2A0B0A28A2800A2A68AD2E660A6282460C701829C7E75A16DA14EE435CBAC6BDD472DD7F2AB8D394B6444AA463BB29E9892BEA10F941B2AC0B63B2F7FD2BAEA82D2D20B48CA4098CFDE27926A7AF428D274E3667056A9CF2BA0A28A2B6320A28A2801AE892214915594F50C320D45F62B4FF9F583FEFD8A9E8A4D27B8D36B620FB15A7FCFAC1FF7EC54A8891A048D5554740A30053A8A124B606DBDC28A28A620A28A2803FFD9, 96, 64, 20, 'a77ac000-0000-4000-8000-000000000004', 0, 1, 'b1a0c0de-0007-4000-8000-000000000006', N'River side', '2018-11-04T10:00:00'),
(N'about-team.png', N'.png', 325, N'image/png', 0x89504E470D0A1A0A0000000D49484452000000600000004008020000006A56E5590000010C49444154789CEDD9CB0DC32010005142CD5B0675BA8E48B9C7433076F8CC5C7DDA27DB12CBEB388E64DFCB27CF4C20CE37081208126836A0F894866938A0D1120812081208120812081208120812081208120812081208120812087A3D7FAB1117D63DA594B40C503CB8F7BA0FAE27500CB309ECE8D501288671B943AA1D280676E928D5021453D15C64FA0D28A6A56966AA058A25681A98F29E3AA97AA8BCA74EFD68795B9DCA01F3CE3A35637A58850482048204BA00541E5FBEFCA5F331E10D2AAB1BE180FC8995758D6A46ABFA0795158D2A87F2340FB90F82DC2842EEA4216F3520EFC5206F56C7039A2B0FAB9040904090409040904090409040904090409040904090409040E9BC37AD7275FA039611AA0000000049454E44AE426082, 96, 64, 3, 'a77ac000-0000-4000-8000-000000000005', 1, 1, NULL, N'Team photo', '2018-11-04T10:00:00'),
(N'paddling-teaser.jpg', N'.jpg', 1218, N'image/jpeg', 0xFFD8FFE000104A46494600010100000100010000FFDB0043000D090A0B0A080D0B0A0B0E0E0D0F13201513121213271C1E17202E2931302E292D2C333A4A3E333646372C2D405741464C4E525352323E5A615A50604A51524FFFDB0043010E0E0E131113261515264F352D354F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4FFFC00011080040006003012200021101031101FFC4001F0000010501010101010100000000000000000102030405060708090A0BFFC400B5100002010303020403050504040000017D01020300041105122131410613516107227114328191A1082342B1C11552D1F02433627282090A161718191A25262728292A3435363738393A434445464748494A535455565758595A636465666768696A737475767778797A838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE1E2E3E4E5E6E7E8E9EAF1F2F3F4F5F6F7F8F9FAFFC4001F0100030101010101010101010000000000000102030405060708090A0BFFC400B51100020102040403040705040400010277000102031104052131061241510761711322328108144291A1B1C109233352F0156272D10A162434E125F11718191A262728292A35363738393A434445464748494A535455565758595A636465666768696A737475767778797A82838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE2E3E4E5E6E7E8E9EAF2F3F4F5F6F7F8F9FAFFDA000C03010002110311003F00EFA8A28A8300A28A2800A28A28022B6B986EEDD67B77DF1BE70D8233838EF52D65F867FE4016DFF03FFD08D6A50898BBA4C28A28A0A0A28A2800A28ACFD5B58B6D2D00972F2B025635EBF53E833409B495D9A14D9658E18CC9348B1A0EACC7007E35C35EF88F50BA6611C9F678CF458F83D73F7BAE7E98ACA7667767762CCC72493924D4F318BAEBA1E872EADA7451976BD8081FDD70C7F21CD41FF091693FF3F7FF0090DFFC2B81A2973323DBC8EBB43D6B4EB4D22082E2E36489BB2BB18E32C4F615AF16ADA74B1875BD8003FDE70A7F23CD79D5149481566958F508A58E68C490C8B221E8CA720FE34EAF2F4664757462ACA72083820D6AD9788F50B5651249F688C7559393D73F7BAE7EB9AAE62D575D4EEE8ACFD2758B6D51088B292A805A36EBF51EA335A1546C9A6AE82BCF75C8E78F58B9FB40605A42CA58E72B9E31ED8FE58AF42AAB7FA7DAEA3108EEA3DDB73B581C1527D0FF009149AB91521CC8F37A2BA2BDF0A5D46CCD6522CC9D958ED6EBD3D0F1DF8FA5634FA7DEDB0769ED66454386628768E71D7A567668E57092DD15A8A28A44851451400515660D3EF6E423416B33AB9C2B043B4F38EBD2B66CBC2975232B5EC8B0A77553B9BAF4F41C77E7E94ECD94A127B2337438E79358B6FB38625640CC54E30B9E73ED8FE78AF42AAB61A7DAE9D118ED63DBBB1B989C9623D4FF009156AB44AC7553872A0A28A299A051451400D9628E68CC7346B221EAAC320FE155FF00B374FF00F9F1B5FF00BF2BFE156A8A056455FECDD3FF00E7C6D7FEFCAFF85588A28E18C470C6B1A0E8AA3007E14EA280B20A28A281851451401FFFD9, 96, 64, 7, 'a77ac000-0000-4000-8000-000000000006', 0, 0, NULL, N'Paddling teaser', '2018-11-04T10:00:00'),
(N'paddle-size-guide.pdf', N'.pdf', 193, N'application/pdf', 0x255044462D312E310A312030206F626A3C3C2F547970652F436174616C6F672F50616765732032203020523E3E656E646F626A0A322030206F626A3C3C2F547970652F50616765732F4B6964735B33203020525D2F436F756E7420313E3E656E646F626A0A332030206F626A3C3C2F547970652F506167652F506172656E742032203020522F4D65646961426F785B30203020323030203130305D3E3E656E646F626A0A747261696C65723C3C2F526F6F742031203020523E3E0A2525454F460A, NULL, NULL, 27, 'a77ac000-0000-4000-8000-000000000007', 0, 0, NULL, N'Paddle size guide', '2018-11-04T10:00:00');

-- An old version of a product image; the reader must ignore the history table.
INSERT INTO CMS_AttachmentHistory (AttachmentName, AttachmentExtension, AttachmentSize, AttachmentMimeType, AttachmentBinary, AttachmentImageWidth, AttachmentImageHeight,
    AttachmentDocumentID, AttachmentGUID, AttachmentIsUnsorted, AttachmentOrder, AttachmentGroupGUID, AttachmentLastModified) VALUES
(N'pacific-front-old.jpg', N'.jpg', 1080, N'image/jpeg', 0xFFD8FFE000104A46494600010100000100010000FFDB0043000D090A0B0A080D0B0A0B0E0E0D0F13201513121213271C1E17202E2931302E292D2C333A4A3E333646372C2D405741464C4E525352323E5A615A50604A51524FFFDB0043010E0E0E131113261515264F352D354F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4F4FFFC00011080040006003012200021101031101FFC4001F0000010501010101010100000000000000000102030405060708090A0BFFC400B5100002010303020403050504040000017D01020300041105122131410613516107227114328191A1082342B1C11552D1F02433627282090A161718191A25262728292A3435363738393A434445464748494A535455565758595A636465666768696A737475767778797A838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE1E2E3E4E5E6E7E8E9EAF1F2F3F4F5F6F7F8F9FAFFC4001F0100030101010101010101010000000000000102030405060708090A0BFFC400B51100020102040403040705040400010277000102031104052131061241510761711322328108144291A1B1C109233352F0156272D10A162434E125F11718191A262728292A35363738393A434445464748494A535455565758595A636465666768696A737475767778797A82838485868788898A92939495969798999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DAE2E3E4E5E6E7E8E9EAF2F3F4F5F6F7F8F9FAFFDA000C03010002110311003F00F4BA28A2800A28A2800A28A280238268EE2159616DC8DD0E31ED52550D0FFE41107FC0BFF42357E800A28A2800A28A2800A28AA7A86A505828DF9791865517AFD4FA0A00B94D91D2342F23AA28EACC700572B75AD5E5C1608FE4A1E8A9D7AFAF5ACF662CC59892C4E4927926803B29351B28D0B35D4440FEEB063F90A8BFB6B4EFF9F8FF00C71BFC2B91A2803A2D2B53B2B7D3A28A69B6BAE723693DC9F4AD18F51B291032DD4401FEF3053F91AE328A00EEE3749103C6EAEA7A329C834EAE0D58AB065243039041E41AD0B5D6AF2DCA877F3907557EBD7D7AD007594553D3F5282FD4ECCA48A32C8DD7EA3D455CA002B8DD55254D4A7F38302CE5949EEB9E3F4AECAABDE59C17B1849D338CED61C15FA500715456CDD787E742CD6CEB2AF656386EBF97F2ACC96CEEA10C65B791554E0B1538FCFA500434514500145145001454D159DD4C14C56F232B1C060A71F9F4AD3B5F0FCEE55AE5D625EEAA72DD7F2FE7401474A495F5283C90C4AB86623B2E79FD2BB2AAF6767059465204C671B98F25BEB562800A28A2800A28A2801B22248852445753D558641A87EC367FF3E907FDFB15628A00AFF61B3FF9F483FEFD8A9A3448D02468A8A3A2A8C014EA2800A28A2800A28A2803FFD9, 96, 64, 19, 'a77ac000-0000-4000-8000-000000000090', 0, 1, 'b1a0c0de-0007-4000-8000-000000000006', '2018-10-01T10:00:00');

-- Media library with files on disk under sample/media/<LibraryFolder>/<FilePath>.
-- The fourth row points at a file that is not on disk.
SET IDENTITY_INSERT Media_Library ON;
INSERT INTO Media_Library (LibraryID, LibraryName, LibraryDisplayName, LibraryFolder, LibrarySiteID, LibraryGUID, LibraryLastModified)
VALUES (1, N'Gallery', N'Gallery', N'Gallery', 1, 'b1b11000-0000-4000-8000-000000000001', '2018-11-05T10:00:00');
SET IDENTITY_INSERT Media_Library OFF;

INSERT INTO Media_File (FileName, FileTitle, FileDescription, FileExtension, FileMimeType, FilePath, FileSize, FileImageWidth, FileImageHeight, FileGUID, FileLibraryID, FileSiteID, FileCreatedWhen, FileModifiedWhen) VALUES
(N'dock', N'Dock', N'', N'.jpg', N'image/jpeg', N'dock.jpg', 1619, 160, 100, 'f11e0000-0000-4000-8000-000000000001', 1, 1, '2018-11-05T10:00:00', '2018-11-05T10:00:00'),
(N'kayak-blue', N'Kayak blue', N'', N'.jpg', N'image/jpeg', N'boats/kayak-blue.jpg', 1735, 160, 100, 'f11e0000-0000-4000-8000-000000000002', 1, 1, '2018-11-05T10:00:00', '2018-11-05T10:00:00'),
(N'kayak-red', N'Kayak red', N'', N'.png', N'image/png', N'boats/kayak-red.png', 524, 160, 100, 'f11e0000-0000-4000-8000-000000000003', 1, 1, '2018-11-05T10:00:00', '2018-11-05T10:00:00'),
(N'missing', N'Missing', N'', N'.jpg', N'image/jpeg', N'missing.jpg', 2048, 160, 100, 'f11e0000-0000-4000-8000-000000000004', 1, 1, '2018-11-05T10:00:00', '2018-11-05T10:00:00');
GO
