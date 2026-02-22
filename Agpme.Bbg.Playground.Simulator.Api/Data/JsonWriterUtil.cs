namespace Agpme.Bbg.Playground.Simulator.Api.Data;

internal static class JsonWriterUtil
{
    /// <summary>
    /// Writes a JSON payload to the HTTP response stream.
    /// - Does NOT append any newline or delimiter.
    /// - When allowPartialChunks=true, occasionally splits the JSON into two writes
    ///   to simulate incomplete buffers (client must reassemble).
    /// </summary>
    public static async Task WriteJsonAsync(
        HttpContext http,
        string json,
        bool allowPartialChunks,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(json))
            return;

        if (!allowPartialChunks || json.Length < 8)
        {
            await http.Response.WriteAsync(json, ct);
            return;
        }

        // ~30% chance to split JSON into two chunks (first chunk missing closing brace)
        var rnd = Random.Shared.Next(0, 10);
        if (rnd < 3)
        {
            var cut = Math.Clamp(json.Length * 7 / 10, 1, json.Length - 1);

            // First partial chunk (no newline)
            await http.Response.WriteAsync(json.Substring(0, cut), ct);
            await http.Response.Body.FlushAsync(ct);

            // tiny pause to mimic network behavior
            await Task.Delay(25, ct);

            // Remainder (no newline)
            await http.Response.WriteAsync(json.Substring(cut), ct);
        }
        else
        {
            await http.Response.WriteAsync(json, ct);
        }
    }

    /// <summary>
    /// Helper if you ever need to ensure one-chunk writes (no partial splits).
    /// </summary>
    public static Task WriteJsonSingleChunkAsync(
        HttpContext http,
        string json,
        CancellationToken ct) =>
        http.Response.WriteAsync(json ?? string.Empty, ct);
}