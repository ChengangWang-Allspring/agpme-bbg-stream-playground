namespace Agpme.Bbg.Playground.Simulator.Api.Streaming;

public interface IPositionsStreamService
{
    Task StreamAsync(
        HttpContext http,
        string entityType,
        string entityName,
        DateOnly asOfDate,
        bool chunk,
        CancellationToken ct);
}
