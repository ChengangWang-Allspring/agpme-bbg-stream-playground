namespace Agpme.Bbg.Stream.Playground.Server.Data;

public interface IPositionsStreamRepository
{
    /// <summary>
    /// Returns lastStreamOrder, msgRequestId (if any), and JSON rows for initial paint
    /// </summary>
    Task<(int lastStreamOrder, string? msgRequestId, List<string> jsonRows)>
        GetInitialAsync(string entityType, string entityName, DateOnly asOfDate, CancellationToken ct);

    /// <summary>
    /// Returns updates (stream_order, json)
    /// </summary>
    Task<List<(int streamOrder, string json)>> GetUpdatesAsync(
        string entityType, string entityName, DateOnly asOfDate, string msgRequestId, int lastStreamOrder, CancellationToken ct);
}