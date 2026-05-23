namespace Karar.Api.Services;

public static class UcbScoreCalculator
{
    public static double Compute(int rewards, int exposures, int totalExposures)
    {
        var safeRewards = Math.Max(0, rewards);
        var safeExposures = Math.Max(1, exposures);
        var safeTotalExposures = Math.Max(1, totalExposures);

        var rewardRate = (double)safeRewards / safeExposures;
        var uncertainty = Math.Sqrt(2.0 * Math.Log(safeTotalExposures + 1.0) / safeExposures);
        return rewardRate + uncertainty;
    }
}
