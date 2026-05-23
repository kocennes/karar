namespace Karar.Api.Services;

public static class TrendScoreCalculator
{
    private const double Gravity = 1.5;

    public static double Compute(int hakliVotes, int haksizVotes, int comments, double ageHours, double averageDwellSeconds = 0, int exposures = 0)
    {
        var uniqueVotes = Math.Max(0, hakliVotes) + Math.Max(0, haksizVotes);

        // Viral factor: reward posts with high vote-to-exposure ratio (AHA moment potential)
        // If exposures are low/unknown, assume neutral conversion (0.1)
        var conversionRate = exposures > 10 ? (double)uniqueVotes / exposures : 0.1;
        var viralBonus = 1.0 + Math.Clamp(conversionRate * 2.0, 0, 1.0);

        var engagement = (uniqueVotes * viralBonus) + (Math.Max(0, comments) * 3.0);

        var age = Math.Max(0, ageHours);
        var baseScore = engagement / Math.Pow(age + 2, Gravity);

        // Dwell time: 20% influence based on target of 30 seconds
        var dwellMultiplier = Math.Clamp(Math.Max(0, averageDwellSeconds) / 30.0, 0.0, 1.5);

        return baseScore * (0.8 + dwellMultiplier * 0.2);
    }
}
