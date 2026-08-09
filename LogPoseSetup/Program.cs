using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LogPoseSetup
{
    // GUI installer/updater for LogPose. Downloads the LATEST BepInEx 5.x and LogPose
    // releases at runtime, so this exe never goes stale — the same binary installs every
    // future version. Re-running it on an existing install just refreshes the plugin.
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox _path = new TextBox();
        private readonly Button _browse = new Button();
        private readonly Button _install = new Button();
        private readonly Button _launch = new Button();
        private readonly ProgressBar _bar = new ProgressBar();
        private readonly TextBox _log = new TextBox();
        private string _gameDir;

        public SetupForm()
        {
            Text = "LogPose Setup";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(560, 400);
            Font = new Font("Segoe UI", 9.5f);

            var header = new Label
            {
                Text = "LogPose — OPTCGSim mod",
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                Bounds = new Rectangle(16, 12, 528, 34),
            };
            var sub = new Label
            {
                Text = "Replay viewer with match history, alt-art selection, clean combat logs.",
                Bounds = new Rectangle(16, 46, 528, 22),
                ForeColor = Color.FromArgb(90, 90, 90),
            };
            var pathLabel = new Label
            {
                Text = "Game folder (containing OPTCGSim.exe):",
                Bounds = new Rectangle(16, 80, 528, 20),
            };
            _path.Bounds = new Rectangle(16, 102, 438, 26);
            _browse.Text = "Browse…";
            _browse.Bounds = new Rectangle(460, 100, 84, 28);
            _browse.Click += OnBrowse;

            _install.Text = "Install / Update";
            _install.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            _install.Bounds = new Rectangle(16, 140, 160, 36);
            _install.Click += OnInstall;

            _launch.Text = "Launch game";
            _launch.Bounds = new Rectangle(184, 140, 130, 36);
            _launch.Enabled = false;
            _launch.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(_gameDir, "OPTCGSim.exe"),
                        WorkingDirectory = _gameDir,
                        UseShellExecute = true,
                    });
                    Close();
                }
                catch (Exception ex) { Log("Launch failed: " + ex.Message); }
            };

            _bar.Bounds = new Rectangle(16, 186, 528, 14);
            _log.Bounds = new Rectangle(16, 208, 528, 178);
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BackColor = Color.White;

            Controls.AddRange(new Control[] { header, sub, pathLabel, _path, _browse, _install, _launch, _bar, _log });

            string found = AutoDetect();
            if (found != null)
            {
                _path.Text = found;
                Log("Found game: " + found);
            }
            else
            {
                Log("Pick your OPTCGSim folder, then click Install.");
            }
        }

        private static string AutoDetect()
        {
            var candidates = new List<string> { @"D:\OPSIM", @"C:\OPSIM" };
            foreach (string root in new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            })
            {
                try
                {
                    if (!Directory.Exists(root))
                        continue;
                    candidates.Add(root);
                    foreach (string d1 in Directory.GetDirectories(root))
                    {
                        candidates.Add(d1);
                        try
                        {
                            foreach (string d2 in Directory.GetDirectories(d1))
                                candidates.Add(d2);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            foreach (string c in candidates)
            {
                try
                {
                    if (File.Exists(Path.Combine(c, "OPTCGSim.exe")))
                        return c;
                }
                catch { }
            }
            return null;
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Select OPTCGSim.exe";
                dlg.Filter = "OPTCGSim|OPTCGSim.exe|Programs|*.exe";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    _path.Text = Path.GetDirectoryName(dlg.FileName);
            }
        }

        private void Log(string msg)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => Log(msg)));
                return;
            }
            _log.AppendText(msg + Environment.NewLine);
        }

        private void SetProgress(int pct)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SetProgress(pct)));
                return;
            }
            _bar.Value = Math.Max(0, Math.Min(100, pct));
        }

        private async void OnInstall(object sender, EventArgs e)
        {
            string dir = _path.Text.Trim().Trim('"');
            if (dir.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                dir = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "OPTCGSim.exe")))
            {
                MessageBox.Show(this, "OPTCGSim.exe was not found in:\n" + dir, "LogPose Setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _gameDir = dir;

            Process[] running = Process.GetProcessesByName("OPTCGSim");
            if (running.Length > 0)
            {
                DialogResult r = MessageBox.Show(this,
                    "OPTCGSim is running and must be closed to install. Close it now?",
                    "LogPose Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes)
                    return;
                foreach (Process p in running)
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }

            _install.Enabled = false;
            _browse.Enabled = false;
            try
            {
                await Task.Run(() => DoInstall(dir));
                _launch.Enabled = true;
                SetProgress(100);
            }
            catch (Exception ex)
            {
                Log("FAILED: " + ex.Message);
                MessageBox.Show(this, "Install failed:\n" + ex.Message, "LogPose Setup",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _install.Enabled = true;
                _browse.Enabled = true;
            }
        }

        private void DoInstall(string dir)
        {
            if (!File.Exists(Path.Combine(dir, @"BepInEx\core\BepInEx.dll")))
            {
                Log("BepInEx not found - downloading the latest 5.x release...");
                string json = Fetch("https://api.github.com/repos/BepInEx/BepInEx/releases/latest");
                string tag = JsonValue(json, "tag_name");
                string url = AssetUrl(json, new Regex(@"BepInEx.*win.*x64.*\.zip$|BepInEx_x64_.*\.zip$", RegexOptions.IgnoreCase));
                if (url == null)
                    throw new Exception("No win-x64 zip in the latest BepInEx release.");
                string zip = Path.Combine(Path.GetTempPath(), "BepInEx_LogPoseSetup.zip");
                Download(url, zip, 5, 60);
                Log("Extracting BepInEx " + tag + "...");
                ExtractOverwrite(zip, dir);
                File.Delete(zip);
                if (!File.Exists(Path.Combine(dir, "winhttp.dll")))
                    throw new Exception("BepInEx extraction failed (winhttp.dll missing).");
                Log("BepInEx " + tag + " installed.");
            }
            else
            {
                Log("BepInEx already installed - skipping.");
            }
            SetProgress(70);

            Log("Downloading the latest LogPose release...");
            string lp = Fetch("https://api.github.com/repos/HVallad/LogPose/releases/latest");
            string lpTag = JsonValue(lp, "tag_name");
            string dllUrl = AssetUrl(lp, new Regex(@"^LogPose\.dll$"));
            if (dllUrl == null)
                throw new Exception("The latest LogPose release has no LogPose.dll asset.");
            string plugins = Path.Combine(dir, @"BepInEx\plugins");
            Directory.CreateDirectory(plugins);
            Download(dllUrl, Path.Combine(plugins, "LogPose.dll"), 75, 98);
            Log("LogPose " + lpTag + " installed. Click \"Launch game\" - Match History appears on the main menu.");
            Log("The mod updates itself from now on (a button appears on the menu when a new version exists).");
        }

        private static string Fetch(string url)
        {
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "LogPoseSetup");
                return wc.DownloadString(url);
            }
        }

        private void Download(string url, string dest, int pctFrom, int pctTo)
        {
            using (var wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "LogPoseSetup");
                var done = new TaskCompletionSource<bool>();
                wc.DownloadProgressChanged += (s, e) =>
                    SetProgress(pctFrom + (pctTo - pctFrom) * e.ProgressPercentage / 100);
                wc.DownloadFileCompleted += (s, e) =>
                {
                    if (e.Error != null) done.SetException(e.Error);
                    else done.SetResult(true);
                };
                wc.DownloadFileAsync(new Uri(url), dest);
                done.Task.GetAwaiter().GetResult();
            }
        }

        // net48 ships no JSON parser and the GitHub API shape is stable — a scoped regex
        // pull of two fields beats dragging in a dependency for an installer.
        private static string JsonValue(string json, string key)
        {
            Match m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string AssetUrl(string json, Regex namePattern)
        {
            // Match against the filename at the end of each download url — pairing the
            // "name" field with its url via regex breaks on the nested uploader object.
            foreach (Match m in Regex.Matches(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\""))
            {
                string url = m.Groups[1].Value;
                string file = url.Substring(url.LastIndexOf('/') + 1);
                if (namePattern.IsMatch(file))
                    return url;
            }
            return null;
        }

        private static void ExtractOverwrite(string zipPath, string destDir)
        {
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
                    if (!target.StartsWith(Path.GetFullPath(destDir), StringComparison.OrdinalIgnoreCase))
                        continue;   // zip-slip guard
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
        }
    }
}
