using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // Settings → Claude → Copy summary and Save CSV (package 4): the last 7 days as Markdown for pasting, and the ledger
    // as CSV, one row per day, project and thread. Only what the card already shows: names, counts and costs, never a
    // prompt or a reply. Costs are API list prices (what the tokens would cost on the API), blank where unknown.
    public static class ClaudeExport
    {
        public const int Days = 7;
        public static readonly string[] Columns = { "Date", "Project", "Thread", "Session", "Models", "Replies", "Tokens", "Cache reads", "API value (USD)" };

        // One CSV row per local day, project and thread over the last 7 days, newest day first. titleOf names a session's
        // thread ("" for none, then its id label is used); zone is the user's time zone.
        public static string Csv(IEnumerable<ClaudeUsageEntry> replies, Func<string, string> titleOf, Func<string, string> projectOf, DateTime now, TimeZoneInfo zone = null)
        {
            zone = zone ?? TimeZoneInfo.Local;
            DateTime today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), zone).Date, first = today.AddDays(-(Days - 1));
            var b = new StringBuilder();
            b.Append(String.Join(",", Columns.Select(Field))).Append("\r\n");
            var rows = (replies ?? Enumerable.Empty<ClaudeUsageEntry>()).Where(e => e != null)
                .Select(e => new { E = e, Date = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(e.At, DateTimeKind.Utc), zone).Date, Folder = ClaudeUsage.Key(e.Folder) })
                .Where(x => x.Date >= first && x.Date <= today)
                .GroupBy(x => new { x.Date, Folder = x.Folder.ToLowerInvariant(), x.E.Session })
                .OrderByDescending(g => g.Key.Date).ThenByDescending(g => g.Sum(x => x.E.Tokens));
            foreach (var g in rows)
            {
                var any = g.First();
                string project = projectOf == null ? null : projectOf(any.Folder);
                if (String.IsNullOrEmpty(project)) project = ClaudeBridge.ProjectName(any.E.Folder);
                string title = titleOf == null ? null : titleOf(g.Key.Session);
                if (String.IsNullOrWhiteSpace(title)) title = ClaudeTitles.IdLabel(g.Key.Session);
                string models = String.Join(" ", g.Select(x => ClaudeService.ModelName(x.E.Model)).Where(m => m.Length > 0).Distinct());
                bool priced = g.All(x => x.E.Priced);
                b.Append(String.Join(",", new[] {
                    Field(g.Key.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Field(project), Field(title), Field(g.Key.Session), Field(models),
                    Field(g.Count().ToString(CultureInfo.InvariantCulture)), Field(g.Sum(x => x.E.Tokens).ToString(CultureInfo.InvariantCulture)),
                    Field(g.Sum(x => x.E.CacheRead).ToString(CultureInfo.InvariantCulture)),
                    Field(priced ? g.Sum(x => x.E.Cost).ToString("0.0000", CultureInfo.InvariantCulture) : "") })).Append("\r\n");
            }
            return b.ToString();
        }

        // A CSV field: quoted when it holds a comma, quote or line break; a value a spreadsheet would run as a formula
        // (starting with =, +, -, @, a tab or a carriage return) gets a leading apostrophe. Plain numbers stay numbers.
        public static string Field(string value)
        {
            value = value ?? "";
            double number;
            bool numeric = Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            if (!numeric && value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0) value = "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        // The last 7 days as Markdown: a line of totals, a table per day and the busiest projects of the week.
        public static string Summary(List<ClaudeDay> days, DateTime now)
        {
            days = (days ?? new List<ClaudeDay>()).OrderBy(d => d.Date).ToList();
            var b = new StringBuilder();
            b.Append("## Claude Code, last ").Append(Days).Append(" days\n\n");
            long tokens = days.Sum(d => d.Tokens); int replies = days.Sum(d => d.Replies), needs = days.Sum(d => d.Needs);
            b.Append(ClaudeUsage.Format(tokens)).Append(" tokens · ").Append(replies.ToString("N0", CultureInfo.InvariantCulture)).Append(replies == 1 ? " reply" : " replies")
                .Append(" · needed you ").Append(needs).Append(needs == 1 ? " time" : " times").Append("\n\n");
            b.Append("| Day | Tokens | Replies | Threads | Needed you | Longest wait |\n| --- | ---: | ---: | ---: | ---: | ---: |\n");
            foreach (var d in days)
                b.Append("| ").Append(d.Date.ToString("ddd d MMM", CultureInfo.InvariantCulture)).Append(" | ").Append(ClaudeUsage.Format(d.Tokens)).Append(" | ").Append(d.Replies)
                    .Append(" | ").Append(d.Threads).Append(" | ").Append(d.Needs).Append(" | ").Append(d.LongestWait > 0 ? Wait(d.LongestWait) : "–").Append(" |\n");
            var garages = days.SelectMany(d => d.Garages).GroupBy(g => g.Folder.ToLowerInvariant())
                .Select(g => new { Name = g.Last().Name, Tokens = g.Sum(x => x.Tokens), Replies = g.Sum(x => x.Replies) })
                .OrderByDescending(g => g.Tokens).Take(5).ToList();
            if (garages.Count > 0)
            {
                b.Append("\n**Busiest projects**\n\n");
                foreach (var g in garages) b.Append("- ").Append(Markdown(g.Name)).Append(": ").Append(ClaudeUsage.Format(g.Tokens)).Append(" tokens, ").Append(g.Replies).Append(g.Replies == 1 ? " reply" : " replies").Append("\n");
            }
            b.Append("\n_From Claude Dashboard, ").Append(now.ToString("d MMM yyyy", CultureInfo.InvariantCulture)).Append(". Tokens are fresh input, cache writes and output._\n");
            return b.ToString();
        }

        private static string Wait(int seconds) { return seconds >= 3600 ? seconds / 3600 + " h " + (seconds % 3600 / 60).ToString("00") : seconds >= 60 ? seconds / 60 + " min" : seconds + " s"; }
        // A project name as Markdown text: the characters that would format are escaped.
        private static string Markdown(string text)
        {
            var b = new StringBuilder();
            foreach (char c in text ?? "") { if ("\\`*_[]<>|#".IndexOf(c) >= 0) b.Append('\\'); b.Append(c); }
            return b.ToString();
        }
    }
}
