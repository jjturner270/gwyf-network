using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace GwyfCommunity.Setup;

/// <summary>
/// The entire UX. Double-click, watch it find the game, press Install,
/// press Launch. Everything else in the app exists to serve this form.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly Label _title;
    private readonly Label _status;
    private readonly TextBox _gameFolder;
    private readonly Button _chooseFolder;
    private readonly Button _install;
    private readonly Button _uninstall;
    private readonly Button _launch;
    private readonly Button _close;
    private readonly ProgressBar _progress;
    private readonly TextBox _log;

    private string _gameRoot = "";
    private bool _busy;

    public MainForm()
    {
        Text = BuildInfo.DisplayName;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new System.Drawing.Size(560, 560);
        Font = new System.Drawing.Font("Segoe UI", 9f);

        _title = new Label
        {
            Text = BuildInfo.DisplayName,
            Font = new System.Drawing.Font("Segoe UI", 13f, System.Drawing.FontStyle.Bold),
            AutoSize = false,
            Height = 34,
            Padding = new Padding(0, 4, 0, 0),
        };
        _status = new Label
        {
            AutoSize = false,
            Height = 96,
            Font = new System.Drawing.Font("Segoe UI", 10f),
            ForeColor = System.Drawing.Color.DimGray,
        };
        _gameFolder = new TextBox
        {
            ReadOnly = true,
            Width = 430,
        };
        _chooseFolder = new Button { Text = "Choose game folder…", Width = 150 };
        _progress = new ProgressBar { Visible = false, Height = 18, MarqueeAnimationSpeed = 30 };
        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = System.Drawing.Color.FromArgb(24, 24, 24),
            ForeColor = System.Drawing.Color.FromArgb(200, 200, 200),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new System.Drawing.Font("Consolas", 8.5f),
        };
        _install = new Button { Text = "Install", Width = 110, Height = 34, Enabled = false };
        _uninstall = new Button { Text = "Uninstall", Width = 110, Height = 34, Enabled = false };
        _launch = new Button { Text = "Launch Game", Width = 120, Height = 34, Enabled = false };
        _close = new Button { Text = "Close", Width = 90, Height = 34 };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18),
        };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        buttons.Controls.AddRange(new Control[] { _install, _uninstall, _launch, _close });

        flow.Controls.Add(_title);
        flow.Controls.Add(_status);
        flow.Controls.Add(_gameFolder);
        flow.Controls.Add(_chooseFolder);
        flow.Controls.Add(_progress);
        flow.Controls.Add(_log);
        flow.Controls.Add(buttons);
        Controls.Add(flow);

        _gameFolder.Height = 26;
        _log.Height = 200;
        flow.SetFlowBreak(_gameFolder, true);
        flow.SetFlowBreak(_log, true);
        flow.SetFlowBreak(buttons, true);

        _install.Click += async (_, _) => await RunInstallerAsync(install: true);
        _uninstall.Click += async (_, _) => await RunInstallerAsync(install: false);
        _chooseFolder.Click += (_, _) => ChooseGameFolder();
        _launch.Click += (_, _) => LaunchGame();
        _close.Click += (_, _) => Close();

        Shown += async (_, _) => await RefreshStatusAsync();
    }

    // -- status ----------------------------------------------------------------

    private async Task RefreshStatusAsync()
    {
        SetBusy(true);
        try
        {
            await Task.Run(() =>
            {
                var installs = SteamLocator.FindGameInstalls();
                if (installs.Count > 0 && string.IsNullOrEmpty(_gameRoot))
                    _gameRoot = installs[0].GameRoot;
            });
            SetBusy(false);

            if (string.IsNullOrEmpty(_gameRoot))
            {
                _status.ForeColor = System.Drawing.Color.Crimson;
                _status.Text = "Golf With Your Friends was not found.\n" +
                               "Steam should be installed with the game present.\n" +
                               "If it is, point us at the game folder:";
                _gameFolder.Text = "";
                _install.Enabled = false;
                _uninstall.Enabled = false;
                _launch.Enabled = false;
                return;
            }

            var probe = BepInExManager.Probe(_gameRoot);
            var installed = ModDeployer.IsInstalled(_gameRoot);
            var modVersion = ModDeployer.ReadModVersion(_gameRoot);

            _gameFolder.Text = _gameRoot;
            _status.ForeColor = System.Drawing.Color.DimGray;
            _status.Text =
                $"✓ Golf With Your Friends found — {_gameRoot}\n" +
                $"• BepInEx: {probe.Detail}\n" +
                $"• Mod: {(installed ? $"installed ({modVersion})" : "not installed")}";
            _install.Text = installed ? "Repair" : "Install";
            _install.Enabled = true;
            _uninstall.Enabled = installed;
            _launch.Enabled = installed;
            Log("Game: " + _gameRoot);
            Log(probe.Detail);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ChooseGameFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Select the Golf With Your Friends folder" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var exe = System.IO.Path.Combine(dlg.SelectedPath, "Golf With Your Friends.exe");
        if (!System.IO.File.Exists(exe))
        {
            MessageBox.Show(this,
                "That folder does not contain Golf With Your Friends.exe.",
                BuildInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _gameRoot = dlg.SelectedPath;
        Log("Manual folder selected: " + _gameRoot);
        _ = RefreshStatusAsync();
    }

    // -- install / uninstall ---------------------------------------------------

    private async Task RunInstallerAsync(bool install)
    {
        if (_busy) return;
        if (string.IsNullOrEmpty(_gameRoot)) return;

        SetBusy(true);
        _progress.Visible = true;
        _progress.Style = ProgressBarStyle.Continuous;
        try
        {
            if (install)
                await InstallAsync();
            else
                await UninstallAsync();
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(this,
                "Something went wrong:\n\n" + ex.Message +
                "\n\nThe log above has details. No changes were left half-done.",
                BuildInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progress.Visible = false;
            _progress.Value = 0;
            SetBusy(false);
            await RefreshStatusAsync();
        }
    }

    private async Task InstallAsync()
    {
        Log("── Installing ─────────────────────────");
        var beInstalled = false;
        var probe = await Task.Run(() => BepInExManager.Probe(_gameRoot));
        if (probe.State == BepInExManager.InstallState.Absent) beInstalled = true;

        await BepInExManager.EnsureInstalledAsync(_gameRoot,
            status => { Ui(() => { Log(status); StatusLine(status); }); },
            p => Ui(() => _progress.Value = p),
            CancellationToken.None);

        var deployed = false;
        await Task.Run(() =>
        {
            ModDeployer.Deploy(_gameRoot,
                p => Ui(() => _progress.Value = p),
                CancellationToken.None);
            deployed = true;
        });

        await Task.Run(() =>
        {
            var wroteConfig = ModDeployer.EnsureConfig(_gameRoot);
            ModDeployer.WriteSetupMarker(_gameRoot, beInstalled);
            if (!wroteConfig) Log("Config exists — kept your existing settings.");
        });

        _status.ForeColor = System.Drawing.Color.FromArgb(0, 140, 60);
        _status.Text = "✓ Golf With Your Friends found\n" +
                       "✓ Mod installed\n" +
                       "✓ Ready to launch";
        Log(deployed
            ? "Mod deployed. Ready to launch."
            : "Mod deploy skipped?");
    }

    private async Task UninstallAsync()
    {
        var confirm = MessageBox.Show(this,
            "Remove the GWYF Community Network mod?\n\n" +
            "Removes the plugin, its config file, and the observer's local\n" +
            "identity/queue. The game itself is untouched.",
            BuildInfo.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        Log("── Uninstalling ───────────────────────");
        await Task.Run(() => ModDeployer.Uninstall(_gameRoot));

        var removeBep = false;
        if (ModDeployer.MarkerSaysWeInstalledBepInEx(_gameRoot))
        {
            await Task.Run(() =>
            {
                var nowUseless = ModDeployer.BepInExIsNowUseless(_gameRoot);
                if (nowUseless) removeBep = true;
            });
        }

        if (removeBep)
        {
            var yes = MessageBox.Show(this,
                "The GWYF Community Network setup installed BepInEx, and no other\n" +
                "mods remain. Remove BepInEx too?",
                BuildInfo.DisplayName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (yes == DialogResult.Yes)
            {
                await Task.Run(() => ModDeployer.RemoveBepInEx(_gameRoot));
                Log("BepInEx removed.");
            }
        }

        Log("Uninstall complete. The game is back to vanilla.");
    }

    // -- launch ----------------------------------------------------------------

    private void LaunchGame()
    {
        try
        {
            // Steam route: guarantees Steamworks init + overlay.
            Process.Start(new ProcessStartInfo($"steam://rungameid/{BuildInfo.GameSteamAppId}") { UseShellExecute = true });
        }
        catch (Exception)
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    System.IO.Path.Combine(_gameRoot, "Golf With Your Friends.exe"))
                { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not launch the game:\n" + ex.Message,
                    BuildInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    // -- plumbing --------------------------------------------------------------

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _install.Enabled = !busy && !string.IsNullOrEmpty(_gameRoot);
        _uninstall.Enabled = !busy && ModDeployer.IsInstalled(_gameRoot);
        _launch.Enabled = !busy && ModDeployer.IsInstalled(_gameRoot);
        _chooseFolder.Enabled = !busy;
        _close.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void StatusLine(string line) =>
        _status.Text = line;

    private void Log(string line)
    {
        _log.AppendText(DateTime.Now.ToString("HH:mm:ss ") + line + "\r\n");
    }

    private void Ui(Action action)
    {
        if (IsDisposed) return;
        try
        {
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
