namespace Agpme.Bbg.Playground.Subscriptions.Api.Comparison;

public sealed class CompareRequest
{
    public string entityType { get; set; } = default!;   // "accounts" | "groups"
    public string entityName { get; set; } = default!;
    public string asOfDate { get; set; } = default!;     // yyyy-MM-dd
    public string expectedSource { get; set; } = "csv";  // csv|db-history|external-db
    public string? expectedCsvPath { get; set; }

    // Modes
    public bool oneStepMode { get; set; } = true;
    public bool twoStepsMode { get; set; } = false;
    public bool useAllFields { get; set; } = true;

    // Field sets
    public HashSet<string>? phase1Fields { get; set; }
    public HashSet<string>? phase2Fields { get; set; }
    public HashSet<string>? excludedFields { get; set; }

    // Compare options
    public bool stringCaseInsensitive { get; set; } = false;
    public decimal numericTolerance { get; set; } = 1e-5m;

    // Output
    public string savePolicy { get; set; } = "OnFailure"; // Always|OnFailure|Never
    public string? outputRoot { get; set; }               // override default
}

public sealed class CompareResponse
{
    public bool success { get; set; }
    public string? message { get; set; }
    public int expectedCount { get; set; }
    public int actualCount { get; set; }
    public int? phase1Diffs { get; set; }
    public int? phase2Diffs { get; set; }
    public string? outputDir { get; set; }
    public object? paths { get; set; }
}
