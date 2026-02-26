namespace Agpme.Bbg.Playground.Admin.Models;

/// <summary>
/// Mirrors Subscriptions API /client/metadata/inbound-cols-map (GET).
/// </summary>
public record InboundColsMapRow(
    long map_id,
    string domain,
    string source_column,
    string target_column,
    string data_type,
    string? comments,
    bool is_active,
    bool is_required,
    string? default_expr,
    string? transform_expr,
    string? update_set_expr,
    DateTime created_at,
    string created_by,
    DateTime updated_at,
    string updated_by,
    string source_kind);

/// <summary>
/// Mirrors Subscriptions API PUT body for /client/metadata/inbound-cols-map/{mapId}.
/// </summary>
public record MetadataUpdateDto(
    string? domain,
    string? source_column,
    string? target_column,
    string? data_type,
    string? comments,
    bool is_active,
    bool is_required,
    string? default_expr,
    string? transform_expr,
    string? update_set_expr,
    string? source_kind);

public record ValidateResult(bool ok, string? kind, string? error);