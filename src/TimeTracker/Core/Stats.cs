using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TimeTracker.Core;

internal sealed record AppTotal(string Name, string Exe, double Seconds);

internal sealed record DayBucket(string Date, Dictionary<string, double> Apps);

/// <summary>One continuous stretch in a program, in seconds from the start of the day.</summary>
internal sealed record Segment(double S, double E, string App);

internal sealed record Overview(
    double Total,
    List<AppTotal> Apps,
    List<DayBucket> Days,
    List<Dictionary<string, double>> Hours,
    List<Segment>? Timeline,
    double? FirstUnix,
    double? LastUnix);

internal sealed record ProgramInfo(string Exe, string Name, double Seconds);

/// <summary>Read-side queries over activity.db for the dashboard.</summary>
internal static class Stats
{
    private readonly record struct Row(double S, double E, string App, string Exe);

    public static Overview GetOverview(DateOnly from, DateOnly to, IReadOnlySet<string> exclude)
    {
        double t0 = Unix.From(from), t1 = Unix.From(to.AddDays(1));
        var rows = LoadRows(t0, t1, exclude);

        var perApp = new Dictionary<string, double>();
        var appExe = new Dictionary<string, string>();
        var perDay = new Dictionary<DateOnly, Dictionary<string, double>>();
        var perHour = Enumerable.Range(0, 24).Select(_ => new Dictionary<string, double>()).ToList();

        foreach (var r in rows)
        {
            Add(perApp, r.App, r.E - r.S);
            appExe[r.App] = r.Exe;
            // Split at local hour boundaries for the per-day and per-hour charts.
            for (double s = r.S; s < r.E;)
            {
                var local = Unix.ToLocal(s);
                var hourStart = new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Local);
                double ce = Math.Min(r.E, Unix.From(hourStart.AddHours(1)));
                var day = DateOnly.FromDateTime(local);
                if (!perDay.TryGetValue(day, out var dayApps)) perDay[day] = dayApps = [];
                Add(dayApps, r.App, ce - s);
                Add(perHour[local.Hour], r.App, ce - s);
                s = ce;
            }
        }

        var days = new List<DayBucket>();
        for (var d = from; d <= to; d = d.AddDays(1))
            days.Add(new DayBucket(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), perDay.GetValueOrDefault(d) ?? []));

        List<Segment>? timeline = null;
        if (from == to)
        {
            // Neighbouring spans of one program (different window titles) merge into one stretch.
            timeline = [];
            foreach (var r in rows)
            {
                double s = r.S - t0, e = r.E - t0;
                if (timeline.Count > 0 && timeline[^1].App == r.App && s - timeline[^1].E < 15)
                    timeline[^1] = timeline[^1] with { E = e };
                else
                    timeline.Add(new Segment(s, e, r.App));
            }
        }

        var apps = perApp.OrderByDescending(kv => kv.Value)
            .Select(kv => new AppTotal(kv.Key, appExe[kv.Key], kv.Value)).ToList();
        return new Overview(perApp.Values.Sum(), apps, days, perHour, timeline,
            rows.Count > 0 ? rows.Min(r => r.S) : null,
            rows.Count > 0 ? rows.Max(r => r.E) : null);
    }

    /// <summary>Total counted time in a period (used for the "yesterday / previous week" reference).</summary>
    public static double GetTotal(DateOnly from, DateOnly to, IReadOnlySet<string> exclude)
    {
        double t0 = Unix.From(from), t1 = Unix.From(to.AddDays(1));
        using var db = Database.Open();
        using var cmd = Database.Command(db, """
            SELECT exe, SUM(MIN("end", $t1) - MAX(start, $t0)) FROM spans
            WHERE "end" > $t0 AND start < $t1 AND start >= $t0 - 86400 GROUP BY exe
            """, ("$t0", t0), ("$t1", t1));
        double total = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            if (!exclude.Contains(reader.GetString(0))) total += reader.GetDouble(1);
        return total;
    }

    /// <summary>Day of the very first record (start of "all time"), or null when the database is empty.</summary>
    public static DateOnly? GetFirstDay()
    {
        using var db = Database.Open();
        using var cmd = Database.Command(db, "SELECT MIN(start) FROM spans");
        return cmd.ExecuteScalar() is double first ? DateOnly.FromDateTime(Unix.ToLocal(first)) : null;
    }

    /// <summary>Every program ever recorded, most used first (for the exclusions picker).</summary>
    public static List<ProgramInfo> GetPrograms()
    {
        using var db = Database.Open();
        using var cmd = Database.Command(db, """
            SELECT exe, app, SUM("end" - start) AS t FROM spans GROUP BY exe ORDER BY t DESC
            """);
        var list = new List<ProgramInfo>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(new ProgramInfo(reader.GetString(0), reader.GetString(1), reader.GetDouble(2)));
        return list;
    }

    /// <summary>CSV for Excel: one row per day and program. Semicolons + BOM suit the uk-UA Excel.</summary>
    public static void ExportCsv(DateOnly from, DateOnly to, IReadOnlySet<string> exclude, string path)
    {
        var overview = GetOverview(from, to, exclude);
        var sb = new StringBuilder();
        sb.AppendLine("Дата;Програма;Секунди;Час");
        foreach (var day in overview.Days)
        {
            foreach (var (app, sec) in day.Apps.Where(kv => kv.Value >= 1).OrderByDescending(kv => kv.Value))
            {
                long rounded = (long)Math.Round(sec);
                sb.Append(day.Date).Append(';').Append(Csv(app)).Append(';')
                  .Append(rounded.ToString(CultureInfo.InvariantCulture)).Append(';')
                  .AppendLine($"{rounded / 3600}:{rounded % 3600 / 60:00}:{rounded % 60:00}");
            }
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Csv(string value) =>
        value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    private static List<Row> LoadRows(double t0, double t1, IReadOnlySet<string> exclude)
    {
        using var db = Database.Open();
        using var cmd = Database.Command(db, """
            SELECT start, "end", app, exe FROM spans
            WHERE "end" > $t0 AND start < $t1 AND start >= $t0 - 86400 ORDER BY start
            """, ("$t0", t0), ("$t1", t1));
        var rows = new List<Row>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string exe = reader.GetString(3);
            if (exclude.Contains(exe)) continue;
            double s = Math.Max(reader.GetDouble(0), t0), e = Math.Min(reader.GetDouble(1), t1);
            if (e > s) rows.Add(new Row(s, e, reader.GetString(2), exe));
        }
        return rows;
    }

    private static void Add(Dictionary<string, double> map, string key, double value) =>
        map[key] = map.GetValueOrDefault(key) + value;
}
