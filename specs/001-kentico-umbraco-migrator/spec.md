# Feature Specification: Kentico to Umbraco Content Migrator

**Feature Branch**: `main`

**Created**: 2026-11-14

**Status**: Draft

**Input**: User description: "Build a reusable console tool that migrates content from a legacy
Kentico CMS site into a clean Umbraco 17 (LTS) site, published as a public demo of a migration
service. In scope: page types to document types, content pages with hierarchy, media library files
and attachments, rewriting media and internal links in rich text, redirects from old URLs, and a
verification report on every run. Out of scope: Web Forms templates, web parts and layouts;
listings, search, user accounts, dealers, e-commerce; forms, workflows, permissions." Discovery
findings: [docs/DISCOVERY.md](../../docs/DISCOVERY.md). Binding mapping decisions:
[docs/MAPPING.md](../../docs/MAPPING.md).

## Clarifications

### Session 2026-11-14

- Q: Which Kentico version is the reference site? → A: Kentico 11.0 hotfix 10 (not 7 or 8),
  Portal Engine. The tool supports Kentico 11 and fails fast on other versions.
- Q: Listing lookup page types (category, horsepower, listing plan, renewal plan)? → A: Migrate
  into a hidden settings folder. Labelled as a scope extension.
- Q: Products are pages and shop items. What moves? → A: Content plus SKU number, price, retail
  price and availability as plain fields. Labelled as a scope extension.
- Q: Which generic pages move? → A: Content pages move; application shell pages (account, listing
  functions, shop, login, registration) are skipped and reported. The exact rule is fixed below
  (excluded path prefixes in the site profile).
- Q: Pages with several editable regions? → A: First region becomes the main body, the rest are
  joined into an "additional content" field.
- Q: Brand rows that have no page in Kentico? → A: Create them as unpublished brand pages.
- Q: `ads.txt` and the XML product feed? → A: Not uploaded; listed in the report as files for the
  new host.
- Q: Links to the brand search route? → A: Rewritten to the brand page.
- Q: URLs? → A: Keep the old paths; redirects only where a path cannot be kept.
- Q: How does the tool write to Umbraco? → A: A separate console program using the official
  Umbraco Management API.
- Q: Target site and storage? → A: A fresh Umbraco 17 site in the repository on SQL Server; media
  files in cloud blob storage (an emulator locally).
- Q: Public sample dataset? → A: A fictional but working Kentico 11 database created by a script.
- Q: Kentico media libraries (none on the reference site)? → A: Supported anyway and exercised by
  the sample dataset.
- Q: What should a re-run do with a migrated item that an editor changed in Umbraco since the last
  run? → A: Leave it untouched and report it as "edited in Umbraco, not updated"; an explicit run
  option overwrites such items.
- Q: What should a re-run do with an item migrated earlier that no longer exists in Kentico? → A:
  Leave it in Umbraco unchanged and report it as "missing in source, review manually"; the tool
  never deletes or unpublishes.
- Q: By what rule are generic pages selected for migration? → A: The site profile lists excluded
  path prefixes; every other page migrates, including pages without text (for example the
  contact page, so its short alias can redirect to it).
- Q: How are pages that are not published in Kentico (drafts, archived, scheduled) migrated? → A:
  As unpublished pages; scheduled publish and unpublish dates are not carried over and are listed
  in the report.
- Q: Does the preview command read Umbraco to show what would change? → A: Yes, read-only: it
  shows per type how many items would be created, updated, left unchanged or skipped as edited in
  Umbraco; without a reachable Umbraco it falls back to a plan-only report.

## User Scenarios & Testing *(mandatory)*

The actors are the **migration engineer** who runs the tool, the **site owner** who checks the
result, and a **prospective client** who evaluates the public repository.

### User Story 1 - Migrate pages with their hierarchy (Priority: P1)

The migration engineer points the tool at a Kentico database and an empty Umbraco site and runs
one command. The tool creates the document types the content needs, then creates every in-scope
page in the same hierarchy, with names, field values, SEO fields, navigation flags, sort order and
publish state carried over.

**Why this priority**: Pages are the core of the migration; without them nothing else has a target.

**Independent Test**: Run the tool against the sample dataset and an empty Umbraco site, then open
the back office and the public site: every in-scope sample page exists under the expected parent,
with the expected fields and publish state.

**Acceptance Scenarios**:

1. **Given** an empty Umbraco site, **When** the engineer runs the migration for the sample
   dataset, **Then** a document type exists for every mapped page type and every in-scope page
   exists under the parent defined by the mapping.
2. **Given** a source page with several editable regions, **When** it is migrated, **Then** the
   first region is in the main body field and the others, in template order, are in the
   additional content field.
3. **Given** brand rows that have no page in Kentico, **When** the migration runs, **Then** each
   becomes an unpublished brand page.
4. **Given** a product linked to a shop item, **When** it is migrated, **Then** its SKU number,
   price, retail price and availability appear as fields on the product page.
5. **Given** a page under an excluded application path, **When** the migration runs, **Then** it
   is not created and appears in the report as skipped, with the reason.

---

### User Story 2 - Verify every run and re-run safely (Priority: P1)

After each run the engineer gets a report that compares source and target counts per type, lists
every skipped item with a reason, lists unresolved links and errors, and states the run time.
Running the tool a second time creates nothing new and changes nothing that did not change. If a
target count goes down during a run, the tool stops and says so.

**Why this priority**: The tool is a sales demo; its numbers must be checkable and repeatable.

**Independent Test**: Run the migration twice on the sample. The second report shows zero created
and zero updated items and the same target counts. Delete one migrated page by hand, then simulate
a run that would remove another: the tool reports the drop and exits with a failure code.

**Acceptance Scenarios**:

1. **Given** a completed run, **When** the report is opened, **Then** it shows source and target
   counts per type, skipped items by category with reasons, link statistics, redirects by
   reason, errors and total run time.
2. **Given** a target that already holds a previous run, **When** the migration runs again with
   the same source, **Then** no item is created or updated and all target counts are unchanged.
3. **Given** a target count that is lower after the run than before, **When** the run finishes,
   **Then** the tool exits with the count-drop failure code and the report names the type and
   both counts.
4. **Given** one item that the target rejects, **When** the run continues, **Then** the other
   items are still migrated, the report lists the failed item with its old path, and the exit
   code signals item errors.
5. **Given** the engineer only wants to preview, **When** they run the preview command against a
   target that holds a previous run, **Then** the report shows per type how many items would be
   created, updated, left unchanged or skipped as edited, and the target is not changed.
6. **Given** a migrated page that an editor changed in Umbraco after the last run and a source
   change to the same page, **When** the migration runs again, **Then** the page keeps the
   editor's version and the report lists it as "edited in Umbraco, not updated"; **When** the run
   uses the overwrite-edited option, **Then** the page gets the source values and the report lists
   it as "edited in Umbraco, overwritten".

---

### User Story 3 - Media in cloud storage (Priority: P2)

Page attachments stored in the Kentico database and files from Kentico media libraries become
media items in Umbraco, stored in cloud blob storage, organised in folders by source section or
library, and linked from the page fields that used them (single images, galleries, teaser images).

**Why this priority**: Pages without their images are not a usable site, but pages can be checked
before media exist.

**Independent Test**: After a sample run, every sample attachment and media library file exists as
a media item whose file is served from blob storage, and the sample product gallery shows its
images in the original order.

**Acceptance Scenarios**:

1. **Given** a page with a file field and an image gallery field, **When** it is migrated,
   **Then** the fields point to media items holding the same files in the same order.
2. **Given** a Kentico media library, **When** the migration runs, **Then** its files exist as
   media items in a folder named after the library, keeping the library's subfolders.
3. **Given** a file node marked as "not uploaded" by the site profile, **When** the migration
   runs, **Then** it is not uploaded and the report lists it as a file for the new host.
4. **Given** historic attachment versions, **When** the migration runs, **Then** they are ignored.

---

### User Story 4 - Links inside rich text keep working (Priority: P2)

Links in rich text that point to other migrated pages or to migrated files are rewritten so they
follow the Umbraco item, even if it is later moved or renamed. External links stay as they are.
Links that cannot be resolved stay as they are and are listed in the report.

**Why this priority**: Broken internal links are the most visible defect after a migration.

**Independent Test**: The sample rich text contains one link of each kind. After a run, internal
page and file links are Umbraco item links, external links are unchanged, and the one unknown link
is listed in the report.

**Acceptance Scenarios**:

1. **Given** a site-relative or absolute own-domain link to a migrated page, including an old
   `.aspx` or alias form, **When** the page is migrated, **Then** the link targets that page's
   Umbraco item.
2. **Given** a Kentico attachment or media library link or image, **When** the page is migrated,
   **Then** it targets the corresponding media item.
3. **Given** a link to the brand search route with a brand name (any case, with spaces),
   **When** the page is migrated, **Then** it targets that brand's page.
4. **Given** a link to an application route, an external site, an e-mail address or an in-page
   anchor, **When** the page is migrated, **Then** the link is unchanged and counted in its
   category.
5. **Given** markup that was already rewritten by a previous run, **When** the migration runs
   again, **Then** it is unchanged.

---

### User Story 5 - Old URLs keep working (Priority: P2)

Visitors and search engines that use old URLs reach the right page. Migrated pages keep their old
path where possible. Where a path cannot be kept (old `.aspx` URLs, short aliases, the old home
path, brand names with spaces), the old URL permanently redirects to the new one, and editors can
see these redirects in the Umbraco back office.

**Why this priority**: Losing URLs costs search ranking; it matters at launch, not for content
review.

**Independent Test**: A list of old sample URLs is requested one by one: each either returns the
page directly or returns a permanent redirect to it.

**Acceptance Scenarios**:

1. **Given** a migrated page whose old path is a valid Umbraco path, **When** the old path is
   requested, **Then** the page is served without a redirect.
2. **Given** an old URL with the `.aspx` extension, a document alias, or the old home path,
   **When** it is requested, **Then** the response is a permanent redirect to the page.
3. **Given** a brand whose name contains a space, **When** its old brand URL is requested,
   **Then** the response is a permanent redirect to the brand page.
4. **Given** a wildcard alias of an application route, **When** the migration runs, **Then** no
   redirect is created and the report lists the alias as an application route.
5. **Given** a second run, **When** redirects are registered again, **Then** no duplicates exist.

---

### User Story 6 - Try the tool without client data (Priority: P3)

A prospective client clones the public repository, starts the local services, loads the fictional
sample dataset and runs the migration by following the README, without access to any client
material. The README explains the problem, what the tool does, a before/after table with
anonymised numbers from the reference run, the limitations, and what is done per project.

**Why this priority**: It is what makes the repository a demo, but it depends on the stories above.

**Independent Test**: On a clean machine with Docker and the .NET SDK, follow the README from clone
to a migrated sample site.

**Acceptance Scenarios**:

1. **Given** a fresh clone, **When** the README steps are followed, **Then** the sample site is
   migrated and viewable without editing any tracked file.
2. **Given** the repository contents, **When** they are checked automatically, **Then** they
   contain no client material and no em dashes.

### Edge Cases

- Source database is not Kentico 11: the tool stops before reading content and names the version.
- Target Umbraco is unreachable or the credentials are wrong: the tool stops before any change,
  with a configuration error code.
- Access to the target expires during a long run: the tool renews access and continues.
- Two sibling pages would get the same URL segment: reported as a plan error before any change.
- A rich text field contains HTML that is encoded inside the editable region XML: decoded exactly
  once.
- An attachment referenced by a field is missing: the field is left empty and the report lists it.
- A media library file is listed in the database but missing on disk: skipped and reported.
- Old URL differs only by letter case from the new path: no redirect is created.
- The same old URL belongs to two pages: one redirect, to the first page by sort order, and a
  warning.
- The source database changes during the run: the report warns that source counts differ.
- An editor changed a migrated item in Umbraco between runs: the item is left as edited and
  reported, unless the overwrite-edited option is used.
- A page migrated earlier was deleted in Kentico: it stays in Umbraco unchanged and is reported as
  "missing in source, review manually".
- A source page is a draft, archived, or scheduled: it is migrated unpublished and its schedule is
  listed in the report.

## Requirements *(mandatory)*

### Functional Requirements

**Source reading**

- **FR-001**: The tool MUST read the Kentico database without modifying it, and MUST read media
  library files from a configured folder without modifying them.
- **FR-002**: The tool MUST detect the Kentico version and refuse versions other than 11.x with a
  clear message.
- **FR-003**: The tool MUST read page types with their field definitions, the page tree,
  coupled field values, editable regions in template order, page metadata (title, description,
  keywords, navigation and sitemap flags, sort order, publish state), document aliases,
  attachments (current versions only), media library files and linked shop item fields.
- **FR-004**: The tool MUST read rows of configured custom page types that have no page node.

**Planning**

- **FR-005**: The tool MUST map Kentico page types and fields to Umbraco document types and
  property editors as defined in MAPPING.md, driven by a site profile file.
- **FR-006**: The tool MUST skip pages (and their descendants) under the excluded path prefixes
  listed in the site profile and report each with a category and reason. Every page not excluded
  MUST be migrated, whether or not it has text. There is no automatic content-based exclusion.
- **FR-007**: The tool MUST turn configured Kentico folders into template-less folder items so
  their children keep their paths.
- **FR-008**: The tool MUST support re-parenting rules from the profile (brand pages under the
  brand list served at the old brand route).
- **FR-009**: The tool MUST give every target item a stable key derived from the source, so the
  same source always yields the same keys.
- **FR-010**: The tool MUST report sibling URL segment collisions as plan errors and make no
  changes when plan errors exist.
- **FR-011**: The tool MUST offer a preview command that builds the plan and, when the target is
  reachable, reads it (no writes) to report per type how many items would be created, updated,
  left unchanged or skipped as edited in Umbraco. When the target is not configured or not
  reachable, the preview MUST still produce the plan-only report and say so.

**Writing to Umbraco**

- **FR-012**: The tool MUST create or update data types, document types and minimal page
  templates needed by the plan, through the official Management API only.
- **FR-013**: The tool MUST create or update media items from attachments and media library
  files, stored in blob storage, in folders by source section or library.
- **FR-014**: The tool MUST create or update pages, set their field values, publish those that
  were published, leave unpublished those that the plan marks unpublished, and apply sort order.
- **FR-014a**: Pages that are not published in Kentico (never published, archived, or with a
  publish-from date in the future or a publish-to date in the past) MUST be migrated as
  unpublished pages. Publish-from and publish-to dates MUST NOT be applied in Umbraco and MUST be
  listed in the report per page.
- **FR-015**: For each item the tool MUST create it if missing, update it only when its owned
  values differ from the source, and otherwise leave it untouched.
- **FR-015a**: The tool MUST record, on each item it writes, a fingerprint of the values it wrote.
  On a later run, if the item's current owned values no longer match that fingerprint (an editor
  changed it in Umbraco), the tool MUST NOT update it and MUST list it in the report as "edited
  in Umbraco, not updated", unless the run was started with the overwrite-edited option, in which
  case it updates the item and lists it as "edited in Umbraco, overwritten".
- **FR-015b**: The tool MUST NOT delete or unpublish anything in Umbraco. Items it migrated in an
  earlier run that are no longer produced by the source MUST be left unchanged and listed in the
  report as "missing in source, review manually".
- **FR-016**: The tool MUST continue after an item-level failure and record it.
- **FR-017**: The tool MUST renew its target access automatically when it expires.

**Links**

- **FR-018**: The tool MUST rewrite rich text links to migrated pages (by old path, alias, `.aspx`
  form or own-domain absolute URL, case-insensitive) into Umbraco item links.
- **FR-019**: The tool MUST rewrite Kentico attachment and media library links and images into
  Umbraco media references.
- **FR-020**: The tool MUST rewrite brand search route links to the brand page.
- **FR-021**: The tool MUST leave external, e-mail, anchor and application route links unchanged
  and count them; unresolved internal links MUST be listed with the page's old path.

**URLs and redirects**

- **FR-022**: The tool MUST give each migrated page the URL name of its Kentico node alias so old
  paths stay valid.
- **FR-023**: After pages exist, the tool MUST read their actual URLs from Umbraco and register a
  permanent redirect for every old URL that differs from the new URL after normalisation (case,
  decoding, extension, trailing slash).
- **FR-024**: Redirects MUST be visible and removable in the Umbraco back office and MUST NOT be
  duplicated by later runs.
- **FR-025**: Wildcard application aliases MUST NOT become redirects and MUST be listed in the
  report.

**Verification**

- **FR-026**: Before and after each run the tool MUST snapshot source and target counts; if any
  target count drops it MUST stop with a distinct failure code and report it.
- **FR-027**: Every run MUST write a report (machine-readable and human-readable) with source vs
  target counts per type, skipped items with reasons, plan issues, link statistics, unresolved
  links, redirects by reason, errors, per-phase timings and total run time, plus a redirects list
  and a skipped list.
- **FR-028**: Reports MUST contain only paths, keys, counts and timings, never field content.
- **FR-029**: Exit codes MUST distinguish success, item errors, configuration or connection
  errors, and count drops.

**Configuration and distribution**

- **FR-030**: All settings MUST come from configuration files and command-line options; secrets
  and connection strings MUST come from user secrets or environment variables.
- **FR-031**: The repository MUST include a fictional sample Kentico 11 database script with
  media library files, a site profile for it, and a list of expected old URLs.
- **FR-032**: The repository MUST include an Umbraco 17 host site configured for SQL Server, blob
  media storage and the redirect registration endpoint, plus local service definitions.
- **FR-033**: The README MUST describe the problem, what the tool does, a before/after table with
  anonymised reference numbers, limitations, scope extensions, out-of-scope items as "done per
  project", and how to run the sample.

### Key Entities

- **Source site**: the Kentico site as read: page types, pages, aliases, attachments, media
  files, unlinked rows, version.
- **Page type mapping**: how a Kentico page type and its fields become an Umbraco document type
  and properties.
- **Site profile**: site-specific rules: excluded paths, folders, re-parenting, files not to
  upload, rows to create as unpublished pages.
- **Migration plan**: the full set of target document types, media and pages with stable keys,
  plus skipped items and plan issues.
- **Redirect**: old URL, target item, new URL, reason.
- **Run report**: counts, skipped items, link statistics, redirects, errors, timings.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the reference site, 100% of in-scope items are either present in the target or
  listed as skipped with a reason; the report's numbers match the discovery counts (88 published
  plus 10 unpublished brand pages, 6 articles, 3 products, 13 lookup items, 22 media items).
- **SC-002**: A second run on the same target creates and updates 0 items and leaves every count
  unchanged.
- **SC-003**: 100% of old URLs in the expected-URL lists (sample and reference) return the page or
  a permanent redirect to it.
- **SC-004**: All 90 brand search links in the reference rich text resolve to brand pages; 0
  internal links are left unresolved without being listed in the report.
- **SC-005**: A newcomer following the README migrates the sample site in under 15 minutes,
  excluding image and package downloads.
- **SC-006**: A full reference run finishes in under 5 minutes on the development machine.
- **SC-007**: An automated check finds 0 client-material files and 0 em dashes in the repository.

## Assumptions

- The source is Kentico 11 with a single site and a single culture; multilingual sites are out of
  scope for this version.
- Listing photos are not part of the provided material and are out of scope (listings are out of
  scope).
- The target Umbraco site is fresh and dedicated to the migration; the tool does not merge with
  hand-made content of the same keys.
- Umbraco serves migrated pages under the home node with the top-level node hidden from paths.
- Templates created by the tool are deliberately minimal; the real design is done per project.
- The developer machine has Docker and the .NET 10 SDK; SQL Server runs in an existing container
  or the one defined in the repository.
