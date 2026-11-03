using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ClaudeDashboard
{
    // v0.59.0, History from the logs: the day files for the days before the dashboard was watching, built once from
    // Claude Code's own session logs (projects\**\*.jsonl under the Claude folder, read-only). Ali has used Claude for more
    // than a month while History began writing only when it was turned on, and Claude Code keeps its logs 30 days by
    // default (cleanupPeriodDays), so the logs are the only record of those days on the PC. Pro and Max plans have no
    // history API.
    //
    // Each past local day in the logs that has no day file gets one, and a day file the live History wrote from a
    // partial scan (fewer replies than the logs hold) gets the logs' counts, keeping its own needs and longest wait:
    // tokens, cache reads, replies, threads, ≈ $ at API
    // prices (unpriced replies counted apart), tokens per hour and the share per project. Each reply counts once, by its
    // request id (message.id when there is none), across every log. Numbers and project folders only: no prompt, reply,
    // title or command is kept, and no thread name is taken from a first request. A day file with as many replies as the
    // logs is never replaced, and today is never written (the live History owns it). Needs and the longest wait can't be rebuilt from
    // the logs: such a day says FromLogs, and History says so instead of showing 0.
    //
    // It runs on the log scan's thread, after each scan, in budgeted chunks (StepBudget bytes), with no timer of its own,
    // and writes the days once every log has been read. Off (Settings → Claude → History) it does nothing; turned on
    // again, it starts over.
    public sealed class ClaudeBackfill
    {
        public const int ChunkBytes = 1 << 20;
        public long StepBudget = 32L << 20;
        private readonly string projects;
        private readonly ClaudeHistory history;
        private readonly object gate = new object();
        private bool enabled;
        // The pass in progress: the logs to read, the one being read and how far.
        private List<string> files;
        private int index;
        private long offset;
        // Replies already counted, by request id: the value is a shared placeholder, never the reply, so no reply is kept.
        private Dictionary<string, ClaudeUsageEntry> counted = new Dictionary<string, ClaudeUsageEntry>();
        private Dictionary<string, string> folders = new Dictionary<string, string>();
        private Dictionary<DateTime, Tally> days = new Dictionary<DateTime, Tally>();
        private static readonly ClaudeUsageEntry Counted = new ClaudeUsageEntry();
        private sealed class Tally
        {
            public ClaudeDay Day;
            public HashSet<string> Threads = new HashSet<string>();
            public Dictionary<string, HashSet<string>> GarageThreads = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        }

        public ClaudeBackfill(string configFolder, ClaudeHistory history)
        {
            projects = Path.Combine(configFolder, "projects");
            this.history = history;
        }

        // Settings → Claude → History. Turned on again after being off, the pass starts over (Clear deleted the files).
        public bool Enabled
        {
            get { lock (gate) return enabled; }
            set { lock (gate) { if (value && !enabled) Reset(); enabled = value; } }
        }
        // Progress, for the Sources view: logs read of all, whether the pass is finished, the days it wrote, and the
        // oldest local day any log still holds.
        public int FilesTotal { get; private set; }
        public int FilesRead { get; private set; }
        public bool Done { get; private set; }
        public int DaysWritten { get; private set; }
        // Logs that couldn't be opened or read (gone, locked, or a path Windows refused): skipped, and counted so
        // Sources can say so instead of leaving their days out silently.
        public int FilesSkipped { get; private set; }
        public DateTime? OldestLog { get; private set; }
        public bool Started { get { return files != null; } }

        private void Reset()
        {
            files = null; index = 0; offset = 0;
            counted = new Dictionary<string, ClaudeUsageEntry>(); folders = new Dictionary<string, string>(); days = new Dictionary<DateTime, Tally>();
            FilesTotal = 0; FilesRead = 0; Done = false; DaysWritten = 0; OldestLog = null; FilesSkipped = 0;
        }

        // One budgeted step: reads up to StepBudget bytes of the logs, and writes the days once all are read. Returns
        // whether it wrote any day file.
        public bool Step(DateTime now)
        {
            lock (gate)
            {
                if (!enabled || Done || history == null) return false;
                try
                {
                    DateTime today = history.LocalDate(now), oldestKept = today.AddDays(-(ClaudeHistory.KeptDays - 1));
                    if (files == null)
                    {
                        if (!Directory.Exists(projects)) { files = new List<string>(); FilesTotal = 0; Done = true; return false; }
                        DateTime cutoff = now - TimeSpan.FromDays(ClaudeHistory.KeptDays + 1);
                        // Through the long-path prefix: a Claude app scratch workspace's log sits in a folder whose
                        // encoded name alone can be 200 characters, past Windows' 260 for the whole path.
                        files = new DirectoryInfo(LongPath(projects)).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).Where(f => f.LastWriteTimeUtc >= cutoff)
                            .Select(f => f.FullName).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                        FilesTotal = files.Count; FilesRead = 0; index = 0; offset = 0;
                    }
                    long budget = StepBudget;
                    while (budget > 0 && index < files.Count)
                    {
                        long read;
                        // One log that can't be read is skipped (and counted); it never holds up the others.
                        try { read = ReadChunk(files[index], today, oldestKept); }
                        catch (Exception) { FilesSkipped++; read = -1; }
                        if (read < 0) { index++; offset = 0; FilesRead = index; continue; }
                        budget -= Math.Max(1, read);
                    }
                    if (index < files.Count) return false;
                    bool wrote = Write(today, oldestKept);
                    Done = true;
                    counted = new Dictionary<string, ClaudeUsageEntry>(); folders = new Dictionary<string, string>(); days = new Dictionary<DateTime, Tally>();
                    return wrote;
                }
                catch (Exception) { return false; }
            }
        }

        // Reads whole lines from the current log at offset. Returns the bytes read, or -1 when the log is finished (or
        // gone). The last line of a log counts even without its newline.
        private long ReadChunk(string path, DateTime today, DateTime oldestKept)
        {
            if (!File.Exists(path)) { if (offset == 0) FilesSkipped++; return -1; }
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long length = stream.Length;
                if (offset >= length) return -1;
                int want = (int)Math.Min(length - offset, ChunkBytes);
                byte[] buffer = Read(stream, offset, want);
                int end = Array.LastIndexOf(buffer, (byte)'\n');
                // A line longer than a chunk: read on to its end (or the log's).
                while (end < 0 && offset + buffer.Length < length)
                {
                    want = (int)Math.Min(length - offset, Math.Min((long)Int32.MaxValue / 2, (long)buffer.Length * 2));
                    buffer = Read(stream, offset, want);
                    end = Array.LastIndexOf(buffer, (byte)'\n');
                }
                int take = end < 0 ? buffer.Length : end + 1;
                if (take <= 0) return -1;
                Fold(Encoding.UTF8.GetString(buffer, 0, take), Path.GetFileNameWithoutExtension(path), Helper(path), today, oldestKept);
                offset += take;
                return take;
            }
        }
        private static byte[] Read(FileStream stream, long from, int count)
        {
            stream.Seek(from, SeekOrigin.Begin);
            var buffer = new byte[Math.Max(0, count)];
            int read = 0; while (read < buffer.Length) { int n = stream.Read(buffer, read, buffer.Length - read); if (n <= 0) break; read += n; }
            if (read < buffer.Length) Array.Resize(ref buffer, read);
            return buffer;
        }
        // A local folder as a \\?\ path, which Windows reads past 260 characters; a UNC share or a path that has the
        // prefix already is left as it is.
        public static string LongPath(string folder)
        {
            if (String.IsNullOrEmpty(folder) || Path.DirectorySeparatorChar != '\\' || folder.StartsWith(@"\\", StringComparison.Ordinal)) return folder;
            try { string full = Path.GetFullPath(folder); return full.Length >= 2 && full[1] == ':' ? @"\\?\" + full : folder; } catch (Exception) { return folder; }
        }
        private static bool Helper(string path)
        {
            return Path.GetFileName(path).StartsWith("agent-", StringComparison.OrdinalIgnoreCase) || String.Equals(Path.GetFileName(Path.GetDirectoryName(path) ?? ""), "subagents", StringComparison.OrdinalIgnoreCase);
        }

        // Adds a piece of a log to its days. The parser's titles and first requests go to throwaway dictionaries.
        private void Fold(string text, string session, bool helper, DateTime today, DateTime oldestKept)
        {
            // The oldest day the logs hold, from any line's timestamp (a prompt or a note too, not only replies).
            var first = Earliest(text);
            if (first.HasValue) { var on = history.LocalDate(first.Value); if (!OldestLog.HasValue || on < OldestLog.Value) OldestLog = on; }
            foreach (var e in ClaudeUsage.Parse(text, counted, session, folders, null, null, null, helper))
            {
                // The reply is counted; only its request id stays, against a placeholder.
                if (e.ReplyKey.Length > 0) counted[e.ReplyKey] = Counted;
                DateTime date = history.LocalDate(e.At);
                if (!OldestLog.HasValue || date < OldestLog.Value) OldestLog = date;
                if (date < oldestKept || date >= today) continue;
                Tally tally;
                if (!days.TryGetValue(date, out tally)) { tally = new Tally { Day = new ClaudeDay { Date = date, FromLogs = true } }; days[date] = tally; }
                var day = tally.Day;
                day.Tokens += e.Tokens; day.CacheRead += e.CacheRead; day.Replies++;
                int hour = TimeZoneLocalHour(e.At);
                day.Hours[hour] += e.Tokens;
                if (e.Priced) day.Cost += e.Cost; else day.Unpriced++;
                if (e.Session.Length > 0) tally.Threads.Add(e.Session);
                string key2 = ClaudeUsage.Key(e.Folder);
                var garage = day.Garages.FirstOrDefault(g => String.Equals(g.Folder, key2, StringComparison.OrdinalIgnoreCase));
                if (garage == null) { garage = new ClaudeDayGarage { Folder = key2, Name = ClaudeBridge.ProjectName(e.Folder) }; day.Garages.Add(garage); }
                garage.Tokens += e.Tokens; garage.Replies++; if (e.Priced) garage.Cost += e.Cost;
                HashSet<string> set; if (!tally.GarageThreads.TryGetValue(key2, out set)) { set = new HashSet<string>(); tally.GarageThreads[key2] = set; }
                if (e.Session.Length > 0) set.Add(e.Session);
            }
        }
        // The earliest "timestamp" in a piece of a log, read without parsing the lines (a prompt's or a note's too).
        private static DateTime? Earliest(string text)
        {
            const string Key = "\"timestamp\":\"";
            DateTime? earliest = null;
            int at = 0;
            while ((at = text.IndexOf(Key, at, StringComparison.Ordinal)) >= 0)
            {
                at += Key.Length;
                int end = text.IndexOf('"', at);
                if (end < 0) break;
                if (end - at <= 40)
                {
                    var when = ClaudeBridge.ParseTime(text.Substring(at, end - at));
                    if (when.HasValue && (!earliest.HasValue || when.Value < earliest.Value)) earliest = when;
                }
                at = end + 1;
            }
            return earliest;
        }
        private int TimeZoneLocalHour(DateTime utc) { return Math.Max(0, Math.Min(23, history.LocalTime(utc).Hour)); }

        // Writes each past day the logs cover that has no file yet, or whose file counted fewer replies; never today.
        private bool Write(DateTime today, DateTime oldestKept)
        {
            bool wrote = false;
            foreach (var tally in days.Values.OrderBy(t => t.Day.Date))
            {
                var day = tally.Day;
                if (day.Date < oldestKept || day.Date >= today || !enabled) continue;
                day.Threads = tally.Threads.Count;
                foreach (var g in day.Garages) { HashSet<string> set; g.Threads = tally.GarageThreads.TryGetValue(g.Folder, out set) ? set.Count : 0; }
                day.Garages = day.Garages.OrderByDescending(g => g.Tokens).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (history.Rebuild(day, today)) { DaysWritten++; wrote = true; }
            }
            return wrote;
        }
    }
}
