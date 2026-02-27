namespace Agpme.Bbg.Playground.Admin.Models;

public sealed class CompareOptions // from GET /client/compare/options
{
    public string[] keyOrder { get; set; } = Array.Empty<string>();
    public Defaults defaults { get; set; } = new();
    public sealed class Defaults
    {
        public string[] phase1 { get; set; } = Array.Empty<string>();
        public string[] phase2 { get; set; } = Array.Empty<string>();
        public string[] excluded { get; set; } = Array.Empty<string>();
        public string[] numericExact { get; set; } = Array.Empty<string>();
        public string[] numericTolerant { get; set; } = Array.Empty<string>();
        public decimal numericTolerance { get; set; } = 0.00001m;
    }
}

public sealed class CompareRequestDto
{
    public string entityType { get; set; } = "accounts";     // "accounts" | "groups"
    public string entityName { get; set; } = "";
    public string asOfDate { get; set; } = "";               // yyyy-MM-dd
    // expectedSource: "current" | "history"
    public string expectedSource { get; set; } = "current";

    public bool oneStepMode { get; set; } = true;
    public bool twoStepsMode { get; set; } = false;
    public bool useAllFields { get; set; } = true;

    public HashSet<string>? phase1Fields { get; set; }
    public HashSet<string>? phase2Fields { get; set; }
    public HashSet<string>? excludedFields { get; set; }

    public bool stringCaseInsensitive { get; set; } = false;
    public decimal numericTolerance { get; set; } = 0.00001m;

    public string savePolicy { get; set; } = "OnFailure";    // Always|OnFailure|Never
    public string? outputRoot { get; set; }
}

public sealed class CompareResponseDto
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