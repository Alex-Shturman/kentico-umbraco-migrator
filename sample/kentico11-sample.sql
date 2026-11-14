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
