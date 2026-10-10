namespace MsAds.Auditor.Core.Rules;

/// <summary>
/// Runs the registered rules and orders the results. Entirely deterministic: no AI service is
/// involved in calculating a metric or deciding whether a threshold has been crossed.
/// </summary>
public sealed class RecommendationEngine(IEnumerable<IAuditRule> rules)
{
    private readonly IReadOnlyList<IAuditRule> _rules = [.. rules];

    public IReadOnlyList<IAuditRule> Rules => _rules;

    /// <summary>
    /// Evaluates every rule. A rule that throws is isolated: the remaining rules still run and
    /// the failure is reported as a finding rather than losing the whole report.
    /// </summary>
    public RecommendationResult Evaluate(AuditContext context)
    {
        var findings = new List<Finding>();
        var failures = new List<string>();

        foreach (var rule in _rules)
        {
            try
            {
                findings.AddRange(rule.Evaluate(context));
            }
            catch (Exception ex)
            {
                failures.Add($"{rule.Name} ({rule.RuleId}): {ex.Message}");
            }
        }

        return new RecommendationResult
        {
            Findings = [.. findings.OrderByDescending(Rank).ThenByDescending(f => f.SpendAtStake ?? 0m).ThenBy(f => f.Title, StringComparer.Ordinal)],
            RuleFailures = failures,
            RulesEvaluated = _rules.Count,
        };
    }

    /// <summary>
    /// Ranks by how much the user should care: problems before opportunities, and within those,
    /// higher confidence first.
    /// </summary>
    private static int Rank(Finding finding)
    {
        var severityWeight = finding.Severity switch
        {
            FindingSeverity.Critical => 400,
            FindingSeverity.Warning => 300,
            FindingSeverity.Positive => 200,
            _ => 100,
        };

        var confidenceWeight = finding.Confidence switch
        {
            FindingConfidence.High => 20,
            FindingConfidence.Medium => 10,
            _ => 0,
        };

        return severityWeight + confidenceWeight;
    }
}

public sealed record RecommendationResult
{
    public IReadOnlyList<Finding> Findings { get; init; } = [];

    /// <summary>Rules that failed to evaluate, so a silent gap is never mistaken for a clean bill of health.</summary>
    public IReadOnlyList<string> RuleFailures { get; init; } = [];

    public int RulesEvaluated { get; init; }

    public IEnumerable<Finding> OfSeverity(FindingSeverity severity)
        => Findings.Where(f => f.Severity == severity);

    public int CountOf(FindingSeverity severity) => Findings.Count(f => f.Severity == severity);
}
