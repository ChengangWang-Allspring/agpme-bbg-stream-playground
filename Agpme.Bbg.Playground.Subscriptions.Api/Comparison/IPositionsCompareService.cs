using System.Threading;
using System.Threading.Tasks;

namespace Agpme.Bbg.Playground.Subscriptions.Api.Comparison;

public interface IPositionsCompareService
{
    Task<CompareResponse> RunAsync(CompareRequest req, CancellationToken ct);
}
