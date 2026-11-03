using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // One project's share of a day (package 4).
    public sealed class ClaudeDayGarage
    {
        public string Folder = "", Name = "";
        public long Tokens;
        public int Replies, Threads;
        public double Cost;
    }

    // One local day of Claude Code use (package 4, docs/design/claude-watcher.md "Cockpit"): tokens (fresh input, cache
    // writes and output, as the card counts them), cache reads, replies, threads, the cost at API prices of the priced
    // replies (Unpriced counts the others), how many times a session started needing you and the longest wait, the
    // tokens per hour (for "same time last week") and the share per garage. Numbers only: no prompt, title or command.
    public sealed class ClaudeDay
    {
        public DateTime Date;
        public long Tokens, CacheRead;
        public int Replies, Threads, Unpriced, Needs, LongestWait;
        public double Cost;
        public long[] Hours = new long[24];
        public List<ClaudeDayGarage> Garages = new List<ClaudeDayGarage>();

        public string Name { get { return Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }
        // Tokens from midnight up to a time of day, the last hour counted in part.
        public long TokensBy(TimeSpan timeOfDay)
        {
            double total = 0;
            for (int h = 0; h < 24; h++)
            {
                double share = Math.Max(0, Math.Min(1, timeOfDay.TotalHours - h));
                if (share <= 0) break;
                total += Hours[h] * share;
            }
            return (long)Math.Round(total);
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o["date"] = Name; o["tokens"] = new JsonNumber(Tokens); o["cacheRead"] = new JsonNumber(CacheRead);
            o["replies"] = new JsonNumber(Replies); o["threads"] = new JsonNumber(Threads); o["cost"] = new JsonNumber(Math.Round(Cost, 4));
            o["unpriced"] = new JsonNumber(Unpriced); o["needs"] = new JsonNumber(Needs); o["longestWait"] = new JsonNumber(LongestWait);
            o["hours"] = Hours.Select(h => (object)new JsonNumber(h)).ToList();
            o["garages"] = Garages.Select(g => {
                var x = new JsonObject(); x["folder"] = g.Folder; x["name"] = g.Name; x["tokens"] = new JsonNumber(g.Tokens);
                x["replies"] = new JsonNumber(g.Replies); x["threads"] = new JsonNumber(g.Threads); x["cost"] = new JsonNumber(Math.Round(g.Cost, 4));
                return (object)x;
            }).ToList();
            return o;
        }
        public static ClaudeDay FromJson(JsonObject o)
        {
            DateTime date;
            if (o == null || !DateTime.TryParseExact(ClaudeJson.Str(o, "date") ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return null;
            Func<JsonObject, string, long> num = (x, k) => { double v = ClaudeJson.Num(x, k); return Double.IsNaN(v) || v < 0 ? 0 : (long)v; };
            var day = new ClaudeDay { Date = date.Date, Tokens = num(o, "tokens"), CacheRead = num(o, "cacheRead"), Replies = (int)num(o, "replies"), Threads = (int)num(o, "threads"),
                Unpriced = (int)num(o, "unpriced"), Needs = (int)num(o, "needs"), LongestWait = (int)num(o, "longestWait") };
            double cost = ClaudeJson.Num(o, "cost"); day.Cost = Double.IsNaN(cost) ? 0 : cost;
            var hours = ClaudeJson.List(o, "hours");
            if (hours != null) for (int h = 0; h < 24 && h < hours.Count; h++) { var n = hours[h] as JsonNumber; day.Hours[h] = n == null || Double.IsNaN(n.Value) ? 0 : (long)n.Value; }
            foreach (var g in (ClaudeJson.List(o, "garages") ?? new List<object>()).OfType<JsonObject>())
            {
                double gc = ClaudeJson.Num(g, "cost");
                day.Garages.Add(new ClaudeDayGarage { Folder = ClaudeJson.Str(g, "folder") ?? "", Name = ClaudeJson.Str(g, "name") ?? "", Tokens = num(g, "tokens"),
                    Replies = (int)num(g, "replies"), Threads = (int)num(g, "threads"), Cost = Double.IsNaN(gc) ? 0 : gc });
            }
            return day;
        }
    }

    // The daily summary files (package 4): claude\history\YYYY-MM-DD.json beside the bridge's other files, one per local
    // day, kept 8 weeks. Days are worked out from the log and telemetry replies the card already reads (a week of them),
    // so a day is rewritten only while it can still grow; the needs and the longest wait come from the sessions the
    // dashboard saw. Settings → Claude → History turns it off, and Clear deletes the folder.
    public sealed class ClaudeHistory
    {
        public const int KeptDays = 56, SparkDays = 7;
        public static readonly TimeSpan WriteEvery = TimeSpan.FromMinutes(1);
        private readonly string folder;
        private readonly TimeZoneInfo zone;
        // Sessions needing you now, since when; and per local day the needs counted and the longest wait seen.
        private readonly Dictionary<string, DateTime> needing = new Dictionary<string, DateTime>();
        private readonly Dictionary<DateTime, int[]> waits = new Dictionary<DateTime, int[]>();
        private DateTime lastWrite = DateTime.MinValue;

        public ClaudeHistory(string bridge, TimeZoneInfo zone = null) { folder = Folder(bridge); this.zone = zone ?? TimeZoneInfo.Local; }

        public static string Folder(string bridge) { return Path.Combine(bridge, "history"); }
        public DateTime LocalDate(DateTime utc) { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone).Date; }
        private DateTime Local(DateTime utc) { return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone); }
        private string DayPath(DateTime date) { return Path.Combine(folder, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json"); }

        // Follows the running sessions: a session that starts needing you counts once for its day, and how long it
        // waited counts when it stops (or now, while it still waits).
        public void Note(IEnumerable<ClaudeSession> running, DateTime now)
        {
            var seen = new HashSet<string>();
            foreach (var s in running ?? Enumerable.Empty<ClaudeSession>())
            {
                if (s == null || !s.Running || s.State != ClaudeState.NeedsYou || !seen.Add(s.Id)) continue;
                if (!needing.ContainsKey(s.Id)) { needing[s.Id] = now; Day(LocalDate(now))[0]++; }
                Wait(needing[s.Id], now);
            }
            foreach (var id in needing.Keys.Where(k => !seen.Contains(k)).ToList()) { Wait(needing[id], now); needing.Remove(id); }
        }
        private void Wait(DateTime from, DateTime now)
        {
            var counts = Day(LocalDate(from));
            counts[1] = Math.Max(counts[1], (int)Math.Min(Int32.MaxValue, (now - from).TotalSeconds));
        }
        private int[] Day(DateTime date)
        {
            int[] counts;
            if (!waits.TryGetValue(date, out counts)) { counts = new int[2]; waits[date] = counts; }
            return counts;
        }

        // Works out the days the replies cover. Replies are the scan's (a week); names gives a folder's garage name.
        public List<ClaudeDay> Roll(IEnumerable<ClaudeUsageEntry> replies, Func<string, string> names)
        {
            var days = new Dictionary<DateTime, ClaudeDay>();
            var threads = new Dictionary<DateTime, HashSet<string>>();
            var garageThreads = new Dictionary<string, HashSet<string>>();
            foreach (var e in replies ?? Enumerable.Empty<ClaudeUsageEntry>())
            {
                if (e == null) continue;
                var local = Local(e.At); var date = local.Date;
                ClaudeDay day;
                if (!days.TryGetValue(date, out day)) { day = new ClaudeDay { Date = date }; days[date] = day; threads[date] = new HashSet<string>(); }
                day.Tokens += e.Tokens; day.CacheRead += e.CacheRead; day.Replies++;
                day.Hours[Math.Max(0, Math.Min(23, local.Hour))] += e.Tokens;
                if (e.Priced) day.Cost += e.Cost; else day.Unpriced++;
                if (e.Session.Length > 0) threads[date].Add(e.Session);
                string key = ClaudeUsage.Key(e.Folder);
                var garage = day.Garages.FirstOrDefault(g => String.Equals(g.Folder, key, StringComparison.OrdinalIgnoreCase));
                if (garage == null)
                {
                    string name = names == null ? null : names(key);
                    garage = new ClaudeDayGarage { Folder = key, Name = String.IsNullOrEmpty(name) ? ClaudeBridge.ProjectName(e.Folder) : name };
                    day.Garages.Add(garage);
                }
                garage.Tokens += e.Tokens; garage.Replies++; if (e.Priced) garage.Cost += e.Cost;
                string gk = day.Name + "|" + key.ToLowerInvariant();
                HashSet<string> set; if (!garageThreads.TryGetValue(gk, out set)) { set = new HashSet<string>(); garageThreads[gk] = set; }
                if (e.Session.Length > 0) set.Add(e.Session);
            }
            foreach (var day in days.Values)
            {
                day.Threads = threads[day.Date].Count;
                foreach (var g in day.Garages) { HashSet<string> set; g.Threads = garageThreads.TryGetValue(day.Name + "|" + g.Folder.ToLowerInvariant(), out set) ? set.Count : 0; }
                day.Garages = day.Garages.OrderByDescending(g => g.Tokens).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
                int[] counts; if (waits.TryGetValue(day.Date, out counts)) { day.Needs = counts[0]; day.LongestWait = counts[1]; }
            }
            foreach (var pair in waits) if (!days.ContainsKey(pair.Key) && (pair.Value[0] > 0 || pair.Value[1] > 0)) days[pair.Key] = new ClaudeDay { Date = pair.Key, Needs = pair.Value[0], LongestWait = pair.Value[1] };
            return days.Values.OrderBy(d => d.Date).ToList();
        }

        // Whether Save would write now (once a minute), so the caller copies the replies only then.
        public bool Due(DateTime now) { return now - lastWrite >= WriteEvery; }

        // Writes the days the replies cover, at most once a minute unless forced, and drops files past 8 weeks. A day
        // is replaced only when the new count isn't smaller (the scan keeps a week, so an old day's early hours fall
        // away); needs and waits keep the larger number. Returns whether anything was written.
        public bool Save(IEnumerable<ClaudeUsageEntry> replies, Func<string, string> names, DateTime now, bool force = false)
        {
            if (!force && now - lastWrite < WriteEvery) return false;
            lastWrite = now;
            bool wrote = false;
            foreach (var day in Roll(replies, names))
            {
                var stored = Read(day.Date);
                if (stored != null)
                {
                    day.Needs = Math.Max(day.Needs, stored.Needs); day.LongestWait = Math.Max(day.LongestWait, stored.LongestWait);
                    if (day.Tokens < stored.Tokens || day.Replies < stored.Replies)
                    {
                        if (day.Needs == stored.Needs && day.LongestWait == stored.LongestWait) continue;
                        stored.Needs = day.Needs; stored.LongestWait = day.LongestWait;
                        try { ClaudeBridge.WriteAtomic(DayPath(stored.Date), ClaudeJson.Compact(stored.ToJson())); wrote = true; } catch (Exception) { }
                        continue;
                    }
                    if (ClaudeJson.Compact(stored.ToJson()) == ClaudeJson.Compact(day.ToJson())) continue;
                }
                try { ClaudeBridge.WriteAtomic(DayPath(day.Date), ClaudeJson.Compact(day.ToJson())); wrote = true; } catch (Exception) { }
            }
            Prune(now);
            foreach (var date in waits.Keys.Where(d => d < LocalDate(now).AddDays(-SparkDays)).ToList()) waits.Remove(date);
            return wrote;
        }

        // Deletes day files older than 8 weeks (and stray files that aren't days).
        public void Prune(DateTime now)
        {
            DateTime oldest = LocalDate(now).AddDays(-(KeptDays - 1));
            string[] files;
            try { if (!Directory.Exists(folder)) return; files = Directory.GetFiles(folder, "*.json"); } catch (Exception) { return; }
            foreach (var path in files)
            {
                DateTime date;
                bool day = DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
                if (!day || date < oldest) { try { File.Delete(path); } catch (Exception) { } }
            }
        }

        // Turning History off: the folder and everything kept in memory go.
        public void Clear()
        {
            needing.Clear(); waits.Clear(); lastWrite = DateTime.MinValue;
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (Exception) { }
        }

        public ClaudeDay Read(DateTime date)
        {
            try { string path = DayPath(date); return File.Exists(path) ? ClaudeDay.FromJson(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(path, Encoding.UTF8)))) : null; }
            catch (Exception) { return null; }
        }

        // The last count days up to today, oldest first; a day with no file is an empty day.
        public List<ClaudeDay> Days(DateTime now, int count)
        {
            DateTime today = LocalDate(now);
            var list = new List<ClaudeDay>();
            for (int i = count - 1; i >= 0; i--) { var date = today.AddDays(-i); list.Add(Read(date) ?? new ClaudeDay { Date = date }); }
            return list;
        }

        // The last count days for Copy summary, oldest first: a day the replies cover is counted from them, unless its
        // file holds more (an older day whose first replies fell out of the scan); needs and waits keep the larger number.
        public List<ClaudeDay> Recent(IEnumerable<ClaudeUsageEntry> replies, Func<string, string> names, DateTime now, int count)
        {
            var rolled = Roll(replies, names).ToDictionary(d => d.Date);
            var list = new List<ClaudeDay>();
            foreach (var stored in Days(now, count))
            {
                ClaudeDay day;
                if (!rolled.TryGetValue(stored.Date, out day)) { list.Add(stored); continue; }
                if (stored.Tokens > day.Tokens || stored.Replies > day.Replies) { stored.Needs = Math.Max(stored.Needs, day.Needs); stored.LongestWait = Math.Max(stored.LongestWait, day.LongestWait); list.Add(stored); continue; }
                day.Needs = Math.Max(day.Needs, stored.Needs); day.LongestWait = Math.Max(day.LongestWait, stored.LongestWait);
                list.Add(day);
            }
            return list;
        }

        // The plan row's sparkline: tokens per day for the last 7 days, oldest first, today counted from today's replies
        // (fresher than its file).
        public long[] Spark(DateTime now, ClaudeDay today)
        {
            var days = Days(now, SparkDays);
            if (today != null && today.Date == LocalDate(now)) days[days.Count - 1] = today;
            return days.Select(d => d.Tokens).ToArray();
        }

        // The tick: tokens a week ago by this time of day, from that day's hours (null when that day has no file).
        public long? SameTimeLastWeek(DateTime now)
        {
            var local = Local(now);
            var day = Read(local.Date.AddDays(-7));
            return day == null ? (long?)null : day.TokensBy(local.TimeOfDay);
        }
    }
}
