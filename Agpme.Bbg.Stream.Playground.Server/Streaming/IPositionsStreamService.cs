namespace Agpme.Bbg.Stream.Playground.Server.Streaming;

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
