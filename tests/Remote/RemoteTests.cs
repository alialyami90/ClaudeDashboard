using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Threading;
using WidgetScreen;

// The phone link. Every check listens on loopback with free ports and isolated settings; nothing here opens the
// real port 43830 or touches the user's paired phones.
internal static class RemoteTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }

    // Requests are answered on the UI thread, so the test thread keeps the dispatcher running while it waits.
    private static T Pump<T>(Task<T> task)
    {
        var frame = new DispatcherFrame();
        var limit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        limit.Tick += (s, e) => { limit.Stop(); frame.Continue = false; };
        task.ContinueWith(t => frame.Continue = false, TaskScheduler.Default);
        limit.Start();
        Dispatcher.PushFrame(frame);
        limit.Stop();
        if (!task.IsCompleted) throw new TimeoutException("The phone link did not answer.");
        return task.Result;
    }

    private static Dictionary<string, object> Json(string text) { return (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(text); }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Phone link parses requests and rejects malformed ones", () => {
            var request = RemoteServer.ParseHead("GET /state?parts=focus%2Cdeck&x=1 HTTP/1.1\r\nHost: pc\r\nAuthorization: Bearer abc123");
            Assert(request.Method == "GET" && request.Path == "/state" && request.QueryValue("parts") == "focus,deck" && request.Token == "abc123", "Request head misread.");
            foreach (string bad in new[] { "GET /state", "GET state HTTP/1.1", "GET /a FTP/1.0", "GET /a HTTP/1.1\r\nNoColon" })
            {
                bool rejected = false; try { RemoteServer.ParseHead(bad); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "Malformed request accepted: " + bad);
            }
        });
        check("Web pages and foreign host names are refused", () => {
            Assert(RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: 192.168.1.20:43830")) == null, "The phone's own request was refused.");
            Assert(RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: [fe80::1]:43830")) == null && RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: localhost")) == null && RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1")) == null, "An address host or no host was refused.");
            Assert(RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: 192.168.1.20:43830\r\nOrigin: http://evil.example")) != null, "A browser Origin was accepted.");
            Assert(RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: 192.168.1.20\r\nSec-Fetch-Mode: no-cors")) != null, "A browser fetch was accepted.");
            Assert(RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: evil.example:43830")) != null, "A rebound host name was accepted.");
        });
        check("Only phones on the local network may connect", () => {
            foreach (string ok in new[] { "192.168.1.20", "10.0.0.5", "172.20.1.1", "169.254.3.4", "127.0.0.1", "::1", "fe80::1", "fd00::5" }) Assert(DashboardController.IsLocal(IPAddress.Parse(ok)), ok + " refused.");
            foreach (string no in new[] { "8.8.8.8", "172.32.0.1", "100.64.0.1", "2001:4860::8888" }) Assert(!DashboardController.IsLocal(IPAddress.Parse(no)), no + " allowed.");
        });
        check("Pairing codes expire, lock after five wrong tries, and tokens survive a restart", () => {
            DateTime now = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
            string path = Path.Combine(directory, "remote-devices-test.dat"); if (File.Exists(path)) File.Delete(path);
            var pairing = new RemotePairing(path, () => now);
            Assert(Failure(() => pairing.Pair("123456", "Phone")).Contains("isn't open"), "Pairing open without a code.");
            string code = pairing.StartPairing();
            Assert(code.Length == 6 && code.Trim('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Length == 0, "Code isn't six digits.");
            now = now.AddMinutes(6);
            Assert(Failure(() => pairing.Pair(code, "Phone")).Contains("isn't open"), "Expired code accepted.");
            code = pairing.StartPairing();
            string wrong = code == "000000" ? "111111" : "000000";
            for (int i = 0; i < 4; i++) Assert(Failure(() => pairing.Pair(wrong, "Phone")).Contains("doesn't match"), "Wrong code accepted.");
            Assert(Failure(() => pairing.Pair(wrong, "Phone")).Contains("Too many"), "Guessing wasn't stopped.");
            Assert(pairing.Code == null && Failure(() => pairing.Pair(code, "Phone")).Contains("isn't open"), "Code stayed valid after five wrong tries.");
            code = pairing.StartPairing();
            string token = pairing.Pair(code, "Pixel 9");
            string name;
            Assert(token.Length >= 40 && pairing.Code == null && pairing.Authorize(token, out name) && name == "Pixel 9" && !pairing.Authorize(token + "x", out name), "Token doesn't authorize.");
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(token) && !Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(RemotePairing.Hash(token)), "Pairing store isn't encrypted.");
            var restored = new RemotePairing(path, () => now);
            Assert(restored.Authorize(token, out name) && restored.Devices.Count == 1, "Pairing lost on restart.");
            // Last seen follows each request and is saved now and then; one phone can be forgotten on its own.
            Assert(restored.Devices[0].LastSeenUtc == now, "Last seen not recorded.");
            now = now.AddMinutes(10);
            string second = restored.Pair(restored.StartPairing(), "Tablet");
            Assert(restored.Authorize(second, out name) && name == "Tablet" && restored.Devices.Count == 2, "Second phone didn't pair.");
            Assert(new RemotePairing(path, () => now).Devices.First(d => d.Name == "Tablet").LastSeenUtc == now, "Last seen wasn't saved.");
            Assert(restored.Forget(restored.Devices.First(d => d.Name == "Pixel 9").TokenHash) && !restored.Forget("missing"), "Forget didn't report what it did.");
            var afterForget = new RemotePairing(path, () => now);
            Assert(!afterForget.Authorize(token, out name) && afterForget.Authorize(second, out name) && afterForget.Devices.Count == 1, "Forgetting one phone didn't stick, or took the other with it.");
            restored.ForgetAll();
            Assert(!new RemotePairing(path, () => now).Authorize(second, out name), "Forgotten phone still authorized.");
        });
    }

    private static string Failure(Action action) { try { action(); } catch (InvalidOperationException e) { return e.Message; } throw new Exception("Expected failure was silently accepted."); }
}
