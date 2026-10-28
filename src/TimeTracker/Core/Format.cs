namespace TimeTracker.Core;

internal static class Format
{
    /// <summary>"2 год 05 хв", "45 хв", "30 с" — same wording as the page.</summary>
    public static string Duration(double seconds)
    {
        var total = (long)Math.Round(seconds);
        long h = total / 3600, m = total % 3600 / 60, s = total % 60;
        if (h > 0) return $"{h} год {m:00} хв";
        if (m > 0) return $"{m} хв";
        return $"{s} с";
    }
}
