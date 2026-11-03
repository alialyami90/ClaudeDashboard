using System;
using System.Diagnostics;

namespace ClaudeDashboard
{
    // Opens a web page in the default browser: Sign in with Claude and the links in Settings → Claude.
    public static class Launcher
    {
        public static bool IsAllowed(string target)
        {
            if (String.IsNullOrWhiteSpace(target)) return false;
            Uri uri;
            return Uri.TryCreate(target.Trim(), UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http") && !String.IsNullOrEmpty(uri.Host);
        }

        public static void Open(string target)
        {
            if (!IsAllowed(target)) throw new InvalidOperationException("Only http(s) websites can be opened.");
            // Null when the shell handed it to a browser that was already running.
            var started = Process.Start(new ProcessStartInfo(target.Trim()) { UseShellExecute = true });
            if (started != null) started.Dispose();
        }
    }
}
