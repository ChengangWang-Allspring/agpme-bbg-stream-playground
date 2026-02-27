using System.Globalization;
using Allspring.Agpme.Bbg.TestsShared.Comparison;
using Allspring.Agpme.Bbg.TestsShared.DataAccess.Db;
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
        _localCs = cfg["ConnectionString_Local"]
            ?? throw new InvalidOperationException("ConnectionString_Local not configured.");

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

        // 2) Load EXPECTED based on current/history only.
        var targetCs = await GetTargetConnectionStringAsync(ct);
        await using var conn = new NpgsqlConnection(targetCs);
        await conn.OpenAsync(ct);

        var q = new BbgPositionsDb.Query(asOf, req.entityName);

        List<BbgPosition> expected;

        if (string.Equals(req.expectedSource, "current", StringComparison.OrdinalIgnoreCase))
        {
            expected = await BbgPositionsDb.QueryCurrentAsync(conn, q);
        }
        else if (string.Equals(req.expectedSource, "history", StringComparison.OrdinalIgnoreCase))
        {
            expected = await BbgPositionsDb.QueryHistoryAsync(conn, q);
        }
        else
        {
            return Fail("expectedSource must be 'current' or 'history'.", outputRoot);
        }


        // 3) Load ACTUAL (local current)
        List<BbgPosition> actual;
        await using (var actualConn = new NpgsqlConnection(_localCs))
        {
            await actualConn.OpenAsync(ct);
            var actQuery = new BbgPositionsDb.Query(asOf, req.entityName);
            actual = await BbgPositionsDb.QueryCurrentAsync(actualConn, actQuery);
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
        bool wrote = false;
        if (saveAlways || (saveOnFailure && !res.IsSuccess))
        {
            // Ensure the output folder exists only when we’re going to write
            Directory.CreateDirectory(outputRoot);
            session.WriteCsvs(outputRoot, testName: "bbg_positions");
            wrote = true;
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
            outputDir = wrote ? outputRoot : null,
            paths = wrote ? new
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
            } : null
        };

        static CompareResponse Fail(string reason, string dir) =>
            new() { success = false, message = reason, outputDir = dir };
    }

    // Resolve the Target connection string via StreamAwsSecrets + shared AWS helper
    private async Task<string> GetTargetConnectionStringAsync(CancellationToken ct)
    {
        var env = _cfg["TargetEnvironment"] ?? "uat";
        var sec = _cfg.GetSection($"TargetAwsSecrets_{env}");
        var arn = sec["Arn"] ?? throw new InvalidOperationException("TargetAwsSecrets: Arn missing");
        var keyName = sec["KeyName"] ?? throw new InvalidOperationException("TargetAwsSecrets: KeyName missing");
        var region = sec["Region"] ?? throw new InvalidOperationException("TargetAwsSecrets: Region missing");
        var profile = sec["Profile"]; // optional

        var cs = await Allspring.Agpme.Bbg.TestsShared.Helpers.Aws.AwsSecretHelper
            .GetSecretValueAsync(profile, region, arn, keyName, ct);
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("Resolved target connection string is empty.");
        return cs;
    }

}