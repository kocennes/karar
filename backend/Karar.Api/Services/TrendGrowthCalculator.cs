namespace Karar.Api.Services;

/// <summary>
/// Trend konuları için büyüme yüzdesi: son 24 saatteki gönderi sayısının
/// önceki 24 saate göre işaretli yüzde değişimi.
/// </summary>
public static class TrendGrowthCalculator
{
    public const int MaxPercent = 999;

    /// <summary>
    /// Önceki pencere boşsa: mevcut pencere de boşsa 0, değilse <see cref="MaxPercent"/>.
    /// Aksi halde (mevcut - önceki) / önceki * 100, en yakın tam sayıya yuvarlanır
    /// ve <see cref="MaxPercent"/> ile sınırlanır. Negatif olabilir (en düşük -100).
    /// </summary>
    public static int Percent(int current, int previous)
    {
        current = Math.Max(0, current);
        previous = Math.Max(0, previous);

        if (previous == 0) return current == 0 ? 0 : MaxPercent;

        var raw = Math.Round((current - previous) * 100.0 / previous, MidpointRounding.AwayFromZero);
        return (int)Math.Min(raw, MaxPercent);
    }
}
