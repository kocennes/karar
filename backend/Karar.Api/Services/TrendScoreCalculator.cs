namespace Karar.Api.Services;

public static class TrendScoreCalculator
{
    private const double Gravity = 1.5;

    public static double Compute(
        int hakliVotes,
        int haksizVotes,
        int comments,
        double ageHours,
        double averageDwellSeconds = 0,
        int exposures = 0,
        int pendingReports = 0,
        double perspectiveToxicity = 0,
        int qualityComments = 0)
    {
        var totalUniqueVotes = Math.Max(0, hakliVotes) + Math.Max(0, haksizVotes);

        // 1. Viral factor: reward posts with high vote-to-exposure ratio (AHA moment potential)
        // If exposures are low/unknown, assume neutral conversion (0.1)
        var conversionRate = exposures > 10 ? (double)totalUniqueVotes / exposures : 0.1;
        var viralBonus = 1.0 + Math.Clamp(conversionRate * 2.0, 0, 1.0);

        // 2. Vote Balance (Controversy Bonus): reward balanced discussions
        // A perfectly balanced post (50/50) gets +20% boost, decreasing as it leans one way.
        var balanceBonus = 1.0;
        if (totalUniqueVotes >= 10)
        {
            var ratio = (double)Math.Abs(hakliVotes - haksizVotes) / totalUniqueVotes;
            balanceBonus = 1.0 + (1.0 - ratio) * 0.2;
        }

        // 3. Comment Quality: reward high-quality discussions
        var effectiveComments = (Math.Max(0, comments) * 3.0) + (Math.Max(0, qualityComments) * 5.0);

        var engagement = (totalUniqueVotes * viralBonus * balanceBonus) + effectiveComments;

        var age = Math.Max(0, ageHours);
        var baseScore = engagement / Math.Pow(age + 2, Gravity);

        // 4. Dwell time: 20% influence based on target of 30 seconds
        var dwellMultiplier = Math.Clamp(Math.Max(0, averageDwellSeconds) / 30.0, 0.0, 1.5);
        var score = baseScore * (0.8 + dwellMultiplier * 0.2);

        // 5. Safety Guardrails (Negative Signals)

        // Toxicity Penalty: severe reduction for toxic content
        // Above 0.4 toxicity, we start penalizing. At 0.7+, score drops to near zero.
        if (perspectiveToxicity > 0.4)
        {
            var toxicityPenalty = Math.Pow(1.0 - Math.Clamp((perspectiveToxicity - 0.4) / 0.4, 0, 1.0), 2);
            score *= toxicityPenalty;
        }

        // Report Penalty: decrease score based on report rate
        if (pendingReports > 0)
        {
            // Report rate relative to exposures. 1 report per 100 views is a yellow flag.
            var reportRate = exposures > 0 ? (double)pendingReports / exposures : pendingReports * 0.05;
            var reportPenalty = Math.Max(0.1, 1.0 - (reportRate * 10.0)); // 10% report rate = 90% penalty
            score *= reportPenalty;
        }

        return score;
    }
}
