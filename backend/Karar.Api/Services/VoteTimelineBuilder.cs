namespace Karar.Api.Services;

/// <summary>
/// Gönderi sahibine gösterilen "Oy Zaman Çizelgesi" için saatlik kovaları üretir.
/// Veritabanından gelen (saat, oy sayısı) çiftlerini son N saate yerleştirir;
/// oy almayan saatler sıfırla doldurulur.
/// </summary>
public static class VoteTimelineBuilder
{
    public const int DefaultHours = 24;

    /// <summary>
    /// Eskiden yeniye sıralı, <paramref name="hours"/> uzunluğunda bir liste döndürür.
    /// Son eleman <paramref name="now"/> değerinin içinde bulunduğu saattir.
    /// Pencere dışındaki kovalar yok sayılır; aynı saate düşen kovalar toplanır.
    /// Kind değeri dikkate alınmaz: çağıran taraf saatleri UTC olarak vermelidir.
    /// </summary>
    public static List<int> ZeroFillHourly(
        IEnumerable<(DateTime Hour, int Count)> buckets,
        DateTime now,
        int hours = DefaultHours)
    {
        if (hours <= 0) return new List<int>();

        var currentHour = TruncateToHour(now);
        var firstHour = currentHour.AddHours(-(hours - 1));
        var timeline = new int[hours];

        foreach (var (hour, count) in buckets)
        {
            var bucket = TruncateToHour(hour);
            if (bucket < firstHour || bucket > currentHour) continue;

            var index = (int)((bucket.Ticks - firstHour.Ticks) / TimeSpan.TicksPerHour);
            timeline[index] += Math.Max(0, count);
        }

        return timeline.ToList();
    }

    private static DateTime TruncateToHour(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerHour, DateTimeKind.Unspecified);
}
