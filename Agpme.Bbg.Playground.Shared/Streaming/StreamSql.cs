namespace Agpme.Bbg.Playground.Shared.Streaming;

/// <summary>
/// SQL used by the streaming server to pull Bloomberg JSON from app_data.bbg_positions_stream.
/// Columns (per table definition):
///   as_of_date (date), load_bb_entity_type (varchar), load_bb_entity_name (varchar),
///   load_bb_action (varchar), load_bb_uuid (int), load_process (varchar),
///   load_date (timestamp), stream_order (int, PK with as_of_date), json_stream (jsonb),
///   msg_request_id (varchar)
/// </summary>
public static class StreamSql
{
    /// <summary>
    /// Initial paint: all rows for the given as_of_date/entity where load_bb_action = 'initial'.
    /// Returns stream_order, msg_request_id, and json_stream as text in stream order.
    /// </summary>
    public const string InitialQuery = @"
SELECT
    stream_order,
    msg_request_id,
    json_stream::text
FROM app_data.bbg_positions_stream
WHERE as_of_date = @asOfDate
  AND load_bb_entity_name = @entityName
  AND load_bb_entity_type = @entityType
  AND load_bb_action = @action   -- expect 'initial'
ORDER BY stream_order;";

    /// <summary>
    /// Updates: same as_of_date/entity, same msg_request_id as initial, stream_order > @stream_order,
    /// and load_bb_action is null or empty (''), ordered by stream_order.
    /// </summary>
    public const string UpdateQuery = @"
SELECT
    stream_order,
    json_stream::text
FROM app_data.bbg_positions_stream
WHERE as_of_date = @asOfDate
  AND load_bb_entity_name = @entityName
  AND load_bb_entity_type = @entityType
  AND stream_order > @stream_order
  AND msg_request_id = @msg_request_id
  AND (load_bb_action = '' OR load_bb_action IS NULL)
ORDER BY stream_order;";
}