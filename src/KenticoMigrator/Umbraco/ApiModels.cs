using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KenticoMigrator.Umbraco;

// Request bodies of the Management API calls the tool makes (verified against Umbraco 17.8.1, research R6 to R10).
// They are serialised by ManagementApiClient with camelCase names and nulls written; Id is left out when null so the
// same shape serves update bodies, which do not take an id.

internal sealed record IdRef(Guid Id);

internal sealed record DataTypeValue(string Alias, object? Value);

internal sealed record DataTypeRequest(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Id,
    string Name,
    string EditorAlias,
    string EditorUiAlias,
    IReadOnlyList<DataTypeValue> Values,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IdRef? Parent = null);

internal sealed record TemplateRequest(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Id,
    string Name,
    string Alias,
    string Content);

internal sealed record PropertyValidation(bool Mandatory);

internal sealed record PropertyAppearance(bool LabelOnTop);

internal sealed record PropertyTypeRequest(
    Guid Id,
    IdRef? Container,
    int SortOrder,
    string Alias,
    string Name,
    IdRef DataType,
    bool VariesByCulture,
    bool VariesBySegment,
    PropertyValidation Validation,
    PropertyAppearance Appearance);

internal sealed record ContainerRequest(Guid Id, IdRef? Parent, string Name, string Type, int SortOrder);

internal sealed record AllowedDocumentType(IdRef DocumentType, int SortOrder);

internal sealed record CompositionRequest(IdRef DocumentType, string CompositionType);

internal sealed record CleanupRequest(bool PreventCleanup);

internal sealed record DocumentTypeRequest(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Id,
    string Alias,
    string Name,
    string Icon,
    bool AllowedAsRoot,
    bool VariesByCulture,
    bool VariesBySegment,
    bool IsElement,
    IReadOnlyList<PropertyTypeRequest> Properties,
    IReadOnlyList<ContainerRequest> Containers,
    IReadOnlyList<IdRef> AllowedTemplates,
    IdRef? DefaultTemplate,
    CleanupRequest Cleanup,
    IReadOnlyList<AllowedDocumentType> AllowedDocumentTypes,
    IReadOnlyList<CompositionRequest> Compositions);

internal sealed record VariantRequest(string? Culture, string? Segment, string Name);

internal sealed record DocumentCreateRequest(
    Guid Id,
    IdRef? Parent,
    IdRef DocumentType,
    IdRef? Template,
    JsonArray Values,
    IReadOnlyList<VariantRequest> Variants);

internal sealed record DocumentUpdateRequest(IdRef? Template, JsonArray Values, IReadOnlyList<VariantRequest> Variants);

internal sealed record PublishScheduleRequest(string? Culture);

internal sealed record PublishRequest(IReadOnlyList<PublishScheduleRequest> PublishSchedules);

internal sealed record SortItemRequest(Guid Id, int SortOrder);

internal sealed record SortRequest(IdRef? Parent, IReadOnlyList<SortItemRequest> Sorting);

internal sealed record MoveRequest(IdRef? Target);

internal sealed record RestoreRequest(IdRef? Target);

internal sealed record MediaCreateRequest(
    Guid Id,
    IdRef? Parent,
    IdRef MediaType,
    JsonArray Values,
    IReadOnlyList<VariantRequest> Variants);

internal sealed record MediaUpdateRequest(JsonArray Values, IReadOnlyList<VariantRequest> Variants);
