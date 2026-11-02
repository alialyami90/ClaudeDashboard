using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace WidgetScreen
{
    // The phone remote (decision 008): Settings → Phone, and the requests the Android app sends. Every request runs
    // on the UI thread against the same services the dashboard uses, and only reads what the phone asks for, so
    // nothing polls while no phone is connected.
    public sealed partial class DashboardController
    {
        private RemoteServer remoteServer;
        private RemotePairing pairing;
        private string remoteError;
        private readonly CancellationTokenSource remoteLifetime = new CancellationTokenSource();
        private TrayMode tray;
        private DateTime lastPhoneSeen = DateTime.MinValue;
        // The user brought the window back while the phone was connected; don't hide it again until the phone leaves.
        private bool keepWindowWithPhone;
        public TrayMode Tray { get { return tray; } }
        public static readonly TimeSpan PhoneActiveFor = TimeSpan.FromSeconds(15), PhoneGoneAfter = TimeSpan.FromSeconds(60);
        private bool PhoneActive { get { return remoteServer != null && DateTime.UtcNow - lastPhoneSeen < PhoneActiveFor; } }
        private bool Showing { get { return window.WindowState != WindowState.Minimized && (tray == null || !tray.Hidden); } }

        // Port and discovery port can be overridden for tests; 0 picks any free port.
        public int RemotePort = RemoteServer.DefaultPort, RemoteDiscoveryPort = RemoteServer.DefaultDiscoveryPort;
        public IPAddress RemoteAddress = IPAddress.Any;
        public RemoteServer Remote { get { return remoteServer; } }
        public RemotePairing Pairing { get { return pairing; } }

        private void InitializeRemote()
        {
            pairing = new RemotePairing(RemotePairing.StorePath(settingsPath));
            tray = new TrayMode(window, () => window.Close());
            tray.Changed += () => { if (!tray.Hidden && PhoneActive) keepWindowWithPhone = true; };
            Button("TrayButton", () => { if (Editor.Editing || Control<Grid>("SettingsOverlay").Visibility == Visibility.Visible) return; tray.Hide(false); });
            Control<CheckBox>("RemoteTrayChoice").Click += (sender, e) => {
                bool previous = settings.HideToTrayWithPhone;
                settings.HideToTrayWithPhone = Control<CheckBox>("RemoteTrayChoice").IsChecked == true;
                try { SaveSettingsFile(settings); }
                catch (Exception) { settings.HideToTrayWithPhone = previous; Control<CheckBox>("RemoteTrayChoice").IsChecked = previous; Text("RemoteStatus", "Couldn't save this choice. Check that your settings folder is writable."); }
            };
            Button("RemotePairButton", () => {
                if (!settings.RemoteEnabled) { SetRemoteEnabled(true); }
                if (remoteServer == null) return;
                pairing.StartPairing(); UpdateRemoteSettings();
            });
            Button("RemoteForgetButton", () => {
                try { pairing.ForgetAll(); Text("RemoteStatus", "Paired phones were removed. They need a new code to connect."); }
                catch (Exception) { Text("RemoteStatus", "Couldn't remove the paired phones. Try again."); }
                remoteDevicesShown = null; UpdateRemoteSettings();
            });
            Control<CheckBox>("RemoteEnabledChoice").Click += (sender, e) => SetRemoteEnabled(Control<CheckBox>("RemoteEnabledChoice").IsChecked == true);
            if (pairing.StartupWarning != null && pendingMessage == null) pendingMessage = pairing.StartupWarning;
        }

        // Phone access applies right away, like the Spotify and Discord account buttons.
        private void SetRemoteEnabled(bool enabled)
        {
            bool previous = settings.RemoteEnabled;
            settings.RemoteEnabled = enabled;
            try { SaveSettingsFile(settings); }
            catch (Exception) { settings.RemoteEnabled = previous; Text("RemoteStatus", "Couldn't save this choice. Check that your settings folder is writable."); UpdateRemoteSettings(); return; }
            ApplyRemote();
            UpdateRemoteSettings();
        }

        public void ApplyRemote()
        {
            if (settings.RemoteEnabled && remoteServer == null)
            {
                try { remoteServer = new RemoteServer(RemoteAddress, RemotePort, RemoteDiscoveryPort, Environment.MachineName, IsLocal, HandleRemote); remoteError = null; }
                catch (SocketException) { remoteError = "Port " + RemotePort + " is in use by another program, so phones can't connect. Close it, then turn this on again."; }
            }
            else if (!settings.RemoteEnabled && remoteServer != null)
            {
                remoteServer.Dispose(); remoteServer = null; pairing.StopPairing(); remoteError = null;
            }
        }

        // The addresses a phone can use. Adapters with a gateway (the home network) come first; virtual adapters and
        // self-assigned 169.254 addresses are only shown when there's nothing better.
        public static List<string> LocalAddresses()
        {
            var routed = new List<string>(); var other = new List<string>();
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up || adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback || adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var properties = adapter.GetIPProperties();
                    bool gateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    foreach (var address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork || !IsLocal(address.Address) || IPAddress.IsLoopback(address.Address)) continue;
                        bool selfAssigned = address.Address.GetAddressBytes()[0] == 169;
                        (gateway && !selfAssigned ? routed : other).Add(address.Address.ToString());
                    }
                }
            }
            catch (NetworkInformationException) { }
            return (routed.Count > 0 ? routed : other.Where(a => !a.StartsWith("169.254.")).DefaultIfEmpty(other.FirstOrDefault()).Where(a => a != null)).Distinct().ToList();
        }

        // Phones on the home network only: private, link-local and loopback addresses.
        public static bool IsLocal(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address)) return true;
            if (address.AddressFamily == AddressFamily.InterNetworkV6) return address.IsIPv6LinkLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
            byte[] b = address.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        // Automatic tray mode: hide while a paired phone is reading the dashboard, and come back a minute after it
        // stops, unless the user brought the window back in the meantime or hid it with the button.
        public void UpdateTray()
        {
            if (tray == null) return;
            if (!PhoneActive)
            {
                keepWindowWithPhone = false;
                if (tray.Hidden && tray.Automatic && DateTime.UtcNow - lastPhoneSeen > PhoneGoneAfter) { tray.Show(false); Notify("Your phone disconnected, so the dashboard is back."); }
                return;
            }
            if (settings.HideToTrayWithPhone && !tray.Hidden && !keepWindowWithPhone && !Editor.Editing && Control<Grid>("SettingsOverlay").Visibility != Visibility.Visible) tray.Hide(true);
        }

        // Called for every request from a paired phone that is showing the dashboard.
        public void PhoneSeen() { PhoneSeenAt(DateTime.UtcNow); }
        public void PhoneSeenAt(DateTime utc) { lastPhoneSeen = utc; }

        // Settings → Phone, refreshed when the tab opens and on the heartbeat while a code is shown.
        private void UpdateRemoteSettings()
        {
            Control<CheckBox>("RemoteEnabledChoice").IsChecked = settings.RemoteEnabled;
            Control<CheckBox>("RemoteTrayChoice").IsChecked = settings.HideToTrayWithPhone;
            var addresses = LocalAddresses();
            Control<TextBox>("RemoteAddress").Text = addresses.Count == 0 ? "No network connection" : String.Join("   ", addresses.Select(a => a + ":" + (remoteServer == null ? RemotePort : remoteServer.Port)));
            string code = pairing.Code;
            Text("RemoteCode", code == null ? "—" : code.Substring(0, 3) + " " + code.Substring(3));
            var remaining = pairing.CodeRemaining;
            Text("RemoteCodeHint", code == null ? (settings.RemoteEnabled ? "Tap Pair a phone for a code." : "Turn on phone control to pair a phone.") : "Enter this code on your phone. It expires in " + Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)) + " min.");
            Control<Button>("RemotePairButton").Content = code == null ? "Pair a phone" : "New code";
            var devices = pairing.Devices; int count = devices.Count;
            Text("RemoteDevicesText", count == 0 ? "No phones paired yet." : count == 1 ? "1 phone paired." : count + " phones paired.");
            Control<Button>("RemoteForgetButton").IsEnabled = count > 0;
            ShowRemoteDevices(devices);
            if (remoteError != null) Text("RemoteStatus", remoteError);
            else if (!settings.RemoteEnabled) Text("RemoteStatus", "Off. Phones can't connect to this PC.");
            else if (code == null && Control<TextBlock>("RemoteStatus").Text.StartsWith("Paired phones were removed")) { }
            else Text("RemoteStatus", "On. Paired phones on this Wi-Fi can see and control the dashboard.");
        }

        private string remoteDevicesShown;

        // The paired phones, one row each with a Forget button. Rebuilt only when a name or a last-seen reading changes,
        // because this runs on the heartbeat while a pairing code is shown.
        private void ShowRemoteDevices(List<RemoteDevice> devices)
        {
            string signature = String.Join("|", devices.Select(d => d.TokenHash + ":" + d.Name + ":" + Ago(d.LastSeenUtc)));
            if (signature == remoteDevicesShown) return;
            remoteDevicesShown = signature;
            var panel = Control<StackPanel>("RemoteDevices"); panel.Children.Clear();
            foreach (var device in devices)
            {
                var hash = device.TokenHash; string name = device.Name;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
                var detail = new TextBlock { Text = "Paired " + device.PairedUtc.ToLocalTime().ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture) + "  ·  " + (device.LastSeenUtc == DateTime.MinValue ? "not seen yet" : "last seen " + Ago(device.LastSeenUtc)), Margin = new Thickness(0, 2, 0, 0) };
                detail.SetResourceReference(FrameworkElement.StyleProperty, "Small");
                text.Children.Add(detail); row.Children.Add(text);
                var forget = new Button { Content = "Forget", MinHeight = 52, Margin = new Thickness(12, 0, 0, 0), ToolTip = "This phone needs a new code to connect again." };
                AutomationProperties.SetName(forget, "Forget " + name);
                forget.Click += (sender, e) => {
                    try { Text("RemoteStatus", pairing.Forget(hash) ? "Forgot " + name + ". It needs a new code to connect again." : name + " was already forgotten."); }
                    catch (Exception) { Text("RemoteStatus", "Couldn't forget " + name + ". Try again."); }
                    remoteDevicesShown = null; UpdateRemoteSettings();
                };
                Grid.SetColumn(forget, 1); row.Children.Add(forget);
                panel.Children.Add(row);
            }
        }

        // "just now", "3 min ago", "2 h ago", or the date.
        public static string Ago(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "";
            var span = DateTime.UtcNow - utc;
            if (span < TimeSpan.FromMinutes(1)) return "just now";
            if (span < TimeSpan.FromHours(1)) return (int)span.TotalMinutes + " min ago";
            if (span < TimeSpan.FromDays(1)) return (int)span.TotalHours + " h ago";
            return utc.ToLocalTime().ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Runs on a socket thread; the work happens on the UI thread.
        private Task<RemoteResponse> HandleRemote(RemoteRequest request)
        {
            if (disposed) return Task.FromResult(RemoteResponse.Error(503, "Widget Screen is closing."));
            return window.Dispatcher.InvokeAsync(() => HandleRemoteOnUi(request)).Task.Unwrap();
        }

        private static Dictionary<string, object> D(params object[] pairs)
        {
            var result = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) result[(string)pairs[i]] = pairs[i + 1];
            return result;
        }

        private static Dictionary<string, object> Body(RemoteRequest request)
        {
            if (String.IsNullOrWhiteSpace(request.Body)) return new Dictionary<string, object>();
            return new JavaScriptSerializer().DeserializeObject(request.Body) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }
        private static string Str(Dictionary<string, object> body, string key) { object value; return body.TryGetValue(key, out value) && value != null ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null; }
        private static double Num(Dictionary<string, object> body, string key)
        {
            object value;
            if (!body.TryGetValue(key, out value) || value == null) return Double.NaN;
            try { return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch (FormatException) { return Double.NaN; } catch (InvalidCastException) { return Double.NaN; } catch (OverflowException) { return Double.NaN; }
        }
        // A number the phone must send. A missing or malformed one is refused (400) before it reaches an action.
        private static double Required(Dictionary<string, object> body, string key)
        {
            double value = Num(body, key);
            if (Double.IsNaN(value) || Double.IsInfinity(value)) throw new ArgumentException("The request is missing " + key + ".");
            return value;
        }
        private static bool Flag(Dictionary<string, object> body, string key) { object value; return body.TryGetValue(key, out value) && value is bool && (bool)value; }

        public static string Version
        {
            get { var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(typeof(DashboardController).Assembly, typeof(AssemblyInformationalVersionAttribute)); return attribute == null ? "" : attribute.InformationalVersion; }
        }

        private async Task<RemoteResponse> HandleRemoteOnUi(RemoteRequest request)
        {
            if (request.Method == "GET" && request.Path == "/hello")
                return RemoteResponse.Json(200, D("app", "Widget Screen", "name", Environment.MachineName, "version", Version, "paired", pairing.Authorize(request.Token, out remoteDeviceScratch)));
            if (request.Method == "POST" && request.Path == "/pair")
            {
                try
                {
                    var body = Body(request);
                    string token = pairing.Pair(Str(body, "code"), Str(body, "device"));
                    if (Control<Grid>("SettingsOverlay").Visibility == Visibility.Visible) UpdateRemoteSettings();
                    Notify("Paired " + (Str(body, "device") ?? "a phone") + ". It can now control this PC.");
                    return RemoteResponse.Json(200, D("token", token, "name", Environment.MachineName));
                }
                catch (InvalidOperationException e) { return RemoteResponse.Error(403, e.Message); }
                catch (Exception) { return RemoteResponse.Error(500, "The PC couldn't save the pairing. Try again."); }
            }
            string device;
            if (!pairing.Authorize(request.Token, out device)) return RemoteResponse.Error(401, "This phone isn't paired with the PC anymore. Pair it again in Settings → PC.");
            try
            {
                if (request.Method == "GET" && request.Path == "/state") { PhoneSeen(); UpdateTray(); }
                if (request.Method == "GET" && request.Path == "/state") return RemoteResponse.Json(200, await RemoteState((request.QueryValue("parts") ?? "").Split(',')));
                if (request.Method == "POST" && request.Path == "/command") return await RemoteCommand(Body(request));
                return RemoteResponse.Error(404, "Unknown request. Update Widget Screen on the phone or the PC.");
            }
            catch (InvalidOperationException e) { return RemoteResponse.Error(409, e.Message); }
            catch (ArgumentException e) { return RemoteResponse.Error(400, e.Message); }
        }
        private string remoteDeviceScratch;

        private async Task<Dictionary<string, object>> RemoteState(string[] parts)
        {
            var state = D("name", Environment.MachineName, "version", Version, "quiet", settings.QuietMode, "use24", settings.Use24HourClock, "layoutVersion", RemoteLayoutVersion());
            RemoteClaudeAlert(state);
            if (parts.Contains("layout")) state["layout"] = RemoteLayout();
            if (parts.Contains("claude")) state["claude"] = RemoteClaude();
            return state;
        }

        private RemoteResponse Done(string message) { return RemoteResponse.Json(200, D("ok", true, "message", message ?? "")); }

        private async Task<RemoteResponse> RemoteCommand(Dictionary<string, object> body)
        {
            string action = Str(body, "action") ?? "";
            switch (action)
            {
                case "quiet.toggle":
                    settings.QuietMode = !settings.QuietMode; ApplySettings(); Persist();
                    Notify(settings.QuietMode ? "Quiet mode on." : "Quiet mode off.");
                    return Done(null);
                case "claude.focus": return RemoteClaudeFocus(body);
                case "claude.collapse": return RemoteClaudeCollapse(body);
                case "page.show": case "layout.save": case "layout.inks": case "page.add": case "page.rename": case "page.delete": case "page.move":
                    return RemotePages(Str(body, "action"), body);
                default: throw new ArgumentException("Unknown command. Update Widget Screen on the phone or the PC.");
            }
        }

        private void DisposeRemote()
        {
            remoteLifetime.Cancel();
            if (remoteServer != null) { remoteServer.Dispose(); remoteServer = null; }
        }
    }
}
