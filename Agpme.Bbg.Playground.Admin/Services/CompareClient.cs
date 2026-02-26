using System.Net.Http.Json;
using Agpme.Bbg.Playground.Admin.Models;

namespace Agpme.Bbg.Playground.Admin.Services;

public sealed class CompareClient
{
    private readonly HttpClient _http;
    public CompareClient(IHttpClientFactory factory) => _http = factory.CreateClient("subsapi");

    public Task<CompareOptions?> GetOptionsAsync(CancellationToken ct = default)
        => _http.GetFromJsonAsync<CompareOptions>("/client/compare/options", ct);

    public async Task<CompareResponseDto?> RunAsync(CompareRequestDto req, CancellationToken ct = default)
    {
        var res = await _http.PostAsJsonAsync("/client/compare/run", req, ct);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<CompareResponseDto>(cancellationToken: ct);
    }
}