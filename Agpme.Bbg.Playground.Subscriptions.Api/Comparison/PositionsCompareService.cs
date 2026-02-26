using System.Globalization;
using Allspring.Agpme.Bbg.TestsShared.Comparison;
using Allspring.Agpme.Bbg.TestsShared.DataAccess.Db;
using Allspring.Agpme.Bbg.TestsShared.DataAccess.Files;
using Allspring.Agpme.Bbg.TestsShared.Models;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Agpme.Bbg.Playground.Subscriptions.Api.Comparison;

public sealed class PositionsCompareService : IPositionsCompareService
{
    private readonly string _localCs;
    private readonly ILogger<PositionsCompareService> _logger;
    private readonly IConfiguration _cfg;

    public PositionsCompareService(IConfiguration cfg, ILogger<PositionsCompareService> logger)
    {
        _cfg = cfg;
        _logger = logger;
        _localCs = cfg.GetSection("ClientDb:ConnectionString").Value
            ?? throw new InvalidOperationException("ClientDb:ConnectionString not configured.");
    }

    public async Task<CompareResponse> RunAsync(CompareRequest req, CancellationToken ct)
    {
        // 1) Resolve dates and output root
        var asOf = DateOnly.ParseExact(req.asOfDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var outputRoot = string.IsNullOrWhiteSpace(req.outputRoot)
            ? Path.Combine(AppContext.BaseDirectory, "artifacts", "compare",
                           req.entityType, req.entityName, req.asOfDate,
                           DateTime.UtcNow.ToString("yyyyMMdd-HHmmss'Z'"))
            : req.outputRoot!;
        Directory.CreateDirectory(outputRoot);

        // 2) Load EXPECTED
        List<BbgPosition> expected = new();
        if (string.Equals(req.expectedSource, "csv", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(req.expectedCsvPath) || !File.Exists(req.expectedCsvPath))
                return Fail("Expected CSV not found.", outputRoot);

            expected = BbgPositionsCsv.Load(req.expectedCsvPath, orderByDefaultKey: true);
        }
        else if (string.Equals(req.expectedSource, "db-history", StringComparison.OrdinalIgnoreCase))
        {
            await using var conn = new NpgsqlConnection(_localCs);
            await conn.OpenAsync(ct);
            var q = new BbgPositionsDb.Query(asOf, req.entityName);
            expected = await BbgPositionsDb.QueryHistoryAsync(conn, q);
        }
        else if (string.Equals(req.expectedSource, "external-db", StringComparison.OrdinalIgnoreCase))
        {
            // Optionally fetch a DEV connection string via AWS Secret (same pattern you use elsewhere)
            var extCs = _cfg["Compare:ExternalDb:ConnectionString"]
                ?? throw new InvalidOperationException("Compare:ExternalDb:ConnectionString not set.");
            await using var conn = new NpgsqlConnection(extCs);
            await conn.OpenAsync(ct);
            var q = new BbgPositionsDb.Query(asOf, req.entityName);
            expected = await BbgPositionsDb.QueryCurrentAsync(conn, q);
        }

        // 3) Load ACTUAL (local current)
        List<BbgPosition> actual;
        await using (var conn = new NpgsqlConnection(_localCs))
        {
            await conn.OpenAsync(ct);
            var q = new BbgPositionsDb.Query(asOf, req.entityName);
            actual = await BbgPositionsDb.QueryCurrentAsync(conn, q);
        }

        if (expected.Count == 0)
            return Fail("No EXPECTED rows. Did you pick the right as_of_date/source?", outputRoot);
        if (actual.Count == 0)
            return Fail("No ACTUAL rows. Make sure you subscribed/replayed for this as_of_date.", outputRoot);

        // 4) Build options from request (defaults applied inside BbgCompareSession)
        var opts = new BbgComparerOptions
        {
            OneStepMode = req.oneStepMode || req.useAllFields,       // library also turns it on when UseAllFields=true
            UseAllFields = req.useAllFields,
            StringCaseInsensitive = req.stringCaseInsensitive,
            NumericTolerance = req.numericTolerance,
            ExcludedFields = req.excludedFields ?? new HashSet<string>(StringComparer.Ordinal),
            Phase1Fields = req.phase1Fields ?? new HashSet<string>(StringComparer.Ordinal),
            Phase2Fields = req.twoStepsMode ? (req.phase2Fields ?? new HashSet<string>(StringComparer.Ordinal))
                                            : new HashSet<string>(StringComparer.Ordinal)
        };

        // 5) Run compare
        var session = BbgCompareSession.Run(expected, actual, opts); // applies default rules (UseAllFields/OneStep defaults etc.)
        var res = session.Result;

        // 6) Materialize CSVs per save policy
        var saveAlways = string.Equals(req.savePolicy, "Always", StringComparison.OrdinalIgnoreCase);
        var saveOnFailure = string.Equals(req.savePolicy, "OnFailure", StringComparison.OrdinalIgnoreCase);
        if (saveAlways || (saveOnFailure && !res.IsSuccess))
        {
            session.WriteCsvs(outputRoot, testName: "bbg_positions"); // writes phase1 (and phase2 if present) CSVs 
        }

        // 7) Build response (bounded message using rich formatter)
        var msg = session.GetFailureMessage(maxLines: 200, header: "Compare failed", useRichFormatter: true); // 
        return new CompareResponse
        {
            success = res.IsSuccess,
            message = msg,
            expectedCount = expected.Count,
            actualCount = actual.Count,
            phase1Diffs = res.Phase1?.PerKeyMismatches?.Sum(kv => kv.Value.Count),
            phase2Diffs = res.Phase2?.PerKeyMismatches?.Sum(kv => kv.Value.Count),
            outputDir = outputRoot,
            paths = new
            {
                phase1 = new
                {
                    expectedCsv = Path.Combine(outputRoot, "phase1", "expected.csv"),
                    actualCsv = Path.Combine(outputRoot, "phase1", "actual.csv")
                },
                phase2 = (res.Phase2 is null) ? null : new
                {
                    expectedCsv = Path.Combine(outputRoot, "phase2", "expected.csv"),
                    actualCsv = Path.Combine(outputRoot, "phase2", "actual.csv")
                }
            }
        };

        static CompareResponse Fail(string reason, string dir) =>
            new() { success = false, message = reason, outputDir = dir };
    }
}