using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using BepInEx;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LogPose
{
    // Startup check against GitHub releases. When a newer version exists, a native-styled
    // button appears on the main menu; one click downloads the new DLL. The running module
    // can't be overwritten but CAN be renamed, so the old file is moved aside and the new
    // one written in its place — BepInEx loads it on the next launch, and the next startup
    // deletes the leftover ".old".
    internal static class UpdateCheck
    {
        private const string ApiLatest = "https://api.github.com/repos/HVallad/LogPose/releases/latest";

        private enum State { Idle, Available, Downloading, Ready, Failed }
        private static volatile State _state = State.Idle;
        private static volatile string _remoteTag = "";
        private static volatile string _downloadUrl = "";
        private static GameObject _button;
        private static TMP_Text _label;

        // Surface for the reskinned menu's top-bar pill.
        internal static bool Offering => _state != State.Idle;
        internal static string OfferText => LabelText();
        internal static void Trigger() => OnClick();

        public static void Init()
        {
            CleanupOldDll();
            // The check itself runs on main-menu entry (see Update) — the boot menu is
            // the first entry, and returning from a game re-checks, so a release cut
            // while the game is running still gets offered.
        }

        private static string DllPath()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
                    return loc;
            }
            catch { }
            return Path.Combine(Paths.PluginPath, "LogPose.dll");
        }

        private static void CleanupOldDll()
        {
            try
            {
                string old = DllPath() + ".old";
                if (File.Exists(old))
                    File.Delete(old);
            }
            catch { }
        }

        // Background thread — no Unity APIs here.
        private static void Check()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                JObject rel = JObject.Parse(FetchString(ApiLatest));
                string tag = (string)rel["tag_name"] ?? "";
                Version remote = ParseVersion(tag);
                Version local = ParseVersion(Plugin.VERSION);
                Plugin.Log.LogInfo("Update check: latest release is " + tag + " (running " + Plugin.VERSION + ").");
                if (remote == null || local == null || remote <= local)
                    return;
                foreach (JToken a in rel["assets"] ?? new JArray())
                    if ((string)a["name"] == "LogPose.dll")
                    {
                        _downloadUrl = (string)a["browser_download_url"];
                        break;
                    }
                if (string.IsNullOrEmpty(_downloadUrl))
                    return;
                _remoteTag = tag;
                _state = State.Available;
            }
            catch (Exception e)
            {
                // Offline, rate-limited, cert trouble — never bother the user about it.
                Plugin.Log.LogDebug("Update check skipped: " + e.Message);
            }
        }

        private static Version ParseVersion(string tag)
        {
            try { return new Version(tag.TrimStart('v', 'V')); }
            catch { return null; }
        }

        private static string FetchString(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "LogPose/" + Plugin.VERSION;
            req.Timeout = 10000;
            using (WebResponse resp = req.GetResponse())
            using (StreamReader r = new StreamReader(resp.GetResponseStream()))
                return r.ReadToEnd();
        }

        private static byte[] FetchBytes(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "LogPose/" + Plugin.VERSION;
            req.Timeout = 60000;
            req.AllowAutoRedirect = true;   // release assets redirect to a CDN host
            using (WebResponse resp = req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static bool _menuWasVisible;
        private static float _lastCheck = -9999f;

        public static void Update()
        {
            if (Time.frameCount % 30 != 0)
                return;
            HostJoinScript hjs = UnityEngine.Object.FindFirstObjectByType<HostJoinScript>();
            bool menuVisible = hjs != null && hjs.go_SoloVSelf != null && hjs.go_SoloVSelf.activeSelf;
            // Check on each ENTRY to the main menu, with a cooldown so hopping between
            // menu and deck editor doesn't hammer the API.
            if (Plugin.CfgCheckForUpdates.Value && menuVisible && !_menuWasVisible
                && _state == State.Idle && Time.realtimeSinceStartup - _lastCheck > 120f)
            {
                _lastCheck = Time.realtimeSinceStartup;
                ThreadPool.QueueUserWorkItem(_ => Check());
            }
            _menuWasVisible = menuVisible;

            if (_state == State.Idle || hjs == null || hjs.go_SoloVSelf == null)
                return;
            if (_button == null)
                CreateButton(hjs);
            if (_button == null)
                return;
            _button.SetActive(menuVisible);
            if (_label != null)
                _label.text = LabelText();
        }

        private static string LabelText()
        {
            switch (_state)
            {
                case State.Available: return "Update to " + _remoteTag;
                case State.Downloading: return "Updating...";
                case State.Ready: return "Restart to update";
                default: return "Update failed (see log)";
            }
        }

        private static void CreateButton(HostJoinScript hjs)
        {
            try
            {
                GameObject donor = hjs.go_SoloVSelf;
                _button = UnityEngine.Object.Instantiate(donor, donor.transform.parent);
                _button.name = "LogPoseUpdate";
                Button b = _button.GetComponent<Button>();
                if (b == null)
                    b = _button.AddComponent<Button>();
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(OnClick);
                _label = _button.GetComponentInChildren<TMP_Text>(true);
                if (_label != null)
                {
                    _label.text = LabelText();
                    _label.color = new Color(0.45f, 0.16f, 0.05f);
                }
                RectTransform rt = _button.GetComponent<RectTransform>();
                RectTransform drt = donor.GetComponent<RectTransform>();
                // Pinned to the top-left corner of the menu, mirroring OPBounty's top-right.
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(24f, -24f);
                rt.sizeDelta = new Vector2(drt.sizeDelta.x * 0.72f, drt.sizeDelta.y * 0.5f);
                Plugin.Log.LogInfo("Update button created (" + _remoteTag + " available).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Update button failed: " + e.Message);
            }
        }

        private static void OnClick()
        {
            if (_state == State.Ready)
            {
                Restart();
                return;
            }
            if (_state != State.Available)
                return;
            _state = State.Downloading;
            ThreadPool.QueueUserWorkItem(_ => DoUpdate());
        }

        // The new DLL is already in place on disk (the running one was renamed aside),
        // so a fresh process loads the update immediately. The child must NOT inherit
        // Doorstop's environment markers — with them present the injector thinks it
        // already ran and the new instance boots without BepInEx.
        private static void Restart()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(Paths.ExecutablePath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(Paths.ExecutablePath)
                };
                var env = psi.EnvironmentVariables;
                foreach (string key in new[] { "DOORSTOP_INITIALIZED", "DOORSTOP_DISABLE",
                    "DOORSTOP_INVOKE_DLL_PATH", "DOORSTOP_MANAGED_FOLDER_DIR", "DOORSTOP_PROCESS_PATH" })
                    if (env.ContainsKey(key))
                        env.Remove(key);
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Relaunch failed (" + e.Message + ") - restart manually.");
                return;
            }
            Application.Quit();
        }

        // Background thread — file I/O only, no Unity APIs.
        private static void DoUpdate()
        {
            string dll = DllPath();
            string old = dll + ".old";
            try
            {
                byte[] bytes = FetchBytes(_downloadUrl);
                if (bytes == null || bytes.Length < 8192 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
                    throw new Exception("downloaded file does not look like a plugin DLL");
                if (File.Exists(old))
                    File.Delete(old);
                File.Move(dll, old);
                File.WriteAllBytes(dll, bytes);
                _state = State.Ready;
                Plugin.Log.LogInfo("LogPose " + _remoteTag + " installed - restart the game to load it.");
            }
            catch (Exception e)
            {
                try
                {
                    // If the write failed after the rename, put the working DLL back.
                    if (!File.Exists(dll) && File.Exists(old))
                        File.Move(old, dll);
                }
                catch { }
                _state = State.Failed;
                Plugin.Log.LogWarning("Self-update failed: " + e.Message + " (re-run install.ps1 instead).");
            }
        }
    }
}
