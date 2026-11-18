using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Ryujinx.Ava.Remaster;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Common.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Views.Main;

public partial class RemasterLauncherView : UserControl
{
    private MainWindow _window;
    private int _step;
    private bool _busy;
    private string _apk;
    private string _obb;
    private CancellationTokenSource _cancellation;
    private RemasterUpdater.Update _update;
    private Task _portRefresh = Task.CompletedTask;
    private static readonly IBrush NormalText = new SolidColorBrush(Color.Parse("#C6CFC7"));
    private static readonly IBrush ErrorText = new SolidColorBrush(Color.Parse("#F08D7A"));
    private static readonly IBrush ChosenText = new SolidColorBrush(Color.Parse("#DEB46C"));

    public RemasterLauncherView() => InitializeComponent();

    public void Initialize(MainWindow window)
    {
        _window = window;
        window.Closing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                Status.Text = "Please finish setup or cancel the installation before closing.";
            }
        };
        RemasterUpdater.Log = message => Ryujinx.Common.Logging.Logger.Warning?.Print(Ryujinx.Common.Logging.LogClass.Application, message);
        var (version, _) = RemasterUpdater.Current;
        VersionLabel.Text = $"Version {version.ToString(3)}";
        PortUpdater.Log = RemasterUpdater.Log;
        ShowPortVersion();
        _portRefresh = RefreshPort();
        Refresh();
        if (_step == 3 && GameInstaller.NeedsRebuild(RemasterProfile.GameRoot, RemasterProfile.SdBuilder))
            _ = RebuildGame();
        RemasterUpdater.CleanupAfterUpdate();
        _ = CheckForUpdates(manual: false);
    }

    /// <summary>Fetches the newest SixGuns-NX release's port (the one Switch players update to) in the background.
    /// Silent: offline or on errors the newest port already here is used.</summary>
    private async Task RefreshPort()
    {
        try
        {
            var release = await Task.Run(() => PortUpdater.RefreshAsync(RemasterProfile.PortCache, RemasterProfile.BundledNro,
                RemasterProfile.SdBuilder, _ => { }, CancellationToken.None));
            if (release != null) ShowPortVersion();
        }
        catch (Exception ex)
        {
            RemasterUpdater.Log("Port update check: " + ex.Message);
        }
    }

    private void ShowPortVersion()
    {
        var (version, _) = RemasterUpdater.Current;
        string tag = PortUpdater.Choose(RemasterProfile.PortCache, RemasterProfile.BundledNro, RemasterProfile.SdBuilder).Tag;
        VersionLabel.Text = tag == null ? $"Version {version.ToString(3)}" : $"Version {version.ToString(3)} · port {tag}";
    }

    /// <summary>After a launcher update: rebuild the game with the new port from the player's kept game files.</summary>
    private async Task RebuildGame()
    {
        await Run(async () =>
        {
            var token = _cancellation.Token;
            Status.Text = "Updating your game to the new version…";
            await Task.Run(() => GameInstaller.Rebuild(RemasterProfile.GameRoot, RemasterProfile.SdBuilder,
                message => Dispatcher.UIThread.Post(() => Status.Text = message), token));
            Status.Text = "Your game is up to date. Your saves were kept.";
            Refresh();
        }, true);
    }

    private async Task CheckForUpdates(bool manual)
    {
        if (manual) Status.Foreground = NormalText;
        string reason = RemasterUpdater.CannotUpdateReason();
        if (reason != null)
        {
            if (manual) Status.Text = reason;
            return;
        }
        try
        {
            if (manual) Status.Text = "Checking for updates…";
            _update = await RemasterUpdater.CheckAsync(CancellationToken.None);
            UpdatePanel.IsVisible = _update != null;
            if (_update != null)
                UpdateLabel.Text = $"Version {_update.Version.ToString(3)} is available. Your game files and saves are kept.";
            else if (manual)
                Status.Text = "You have the latest version.";
        }
        catch (Exception ex)
        {
            Ryujinx.Common.Logging.Logger.Warning?.Print(Ryujinx.Common.Logging.LogClass.Application, $"Update check failed: {ex.Message}");
            if (manual) Status.Text = "Couldn't check for updates. Check your internet connection.";
        }
    }

    private async void CheckForUpdatesClicked(object sender, RoutedEventArgs e) => await CheckForUpdates(manual: true);

    private async void InstallUpdate(object sender, RoutedEventArgs e)
    {
        if (_update == null) return;
        bool started = false;
        await Run(async () =>
        {
            var update = _update;
            await RemasterUpdater.DownloadAndInstallAsync(update, message => Dispatcher.UIThread.Post(() =>
            {
                Status.Text = message;
                var percent = Regex.Match(message, @"(\d+)%$");
                Progress.IsIndeterminate = !percent.Success;
                if (percent.Success) Progress.Value = int.Parse(percent.Groups[1].Value);
            }), _cancellation.Token);
            started = true;
            Status.Text = "Restarting with the new version…";
        }, true);
        // The new version is running; it rebuilds the game from the kept files on its first start.
        if (started) _window.Close();
    }

    private async void ShowReleaseNotes(object sender, RoutedEventArgs e)
    {
        if (_update?.Tag == null) return;
        try { await TopLevel.GetTopLevel(this)!.Launcher.LaunchUriAsync(new Uri($"https://github.com/jorgechavezxyz/SixGuns-PC/releases/tag/{Uri.EscapeDataString(_update.Tag)}")); }
        catch (Exception ex) { Status.Text = "Couldn't open the release page. " + ex.Message; }
    }

    private bool HaveKeys()
    {
        try { GameInstaller.ValidateKeys(Path.Combine(AppDataManager.KeysDirPath, "prod.keys")); return true; }
        catch { return false; }
    }

    private bool HaveFirmware()
    {
        try { return _window.ContentManager.GetCurrentFirmwareVersion() != null; }
        catch { return false; }
    }

    public void Refresh()
    {
        _step = !HaveKeys() ? 0 : !HaveFirmware() ? 1 : !GameInstaller.IsInstalled(RemasterProfile.GameRoot) ? 2 : 3;
        ShowStep();
    }

    private void ShowStep()
    {
        SetupPanel.IsVisible = _step < 3;
        ReadyPanel.IsVisible = RepairButton.IsVisible = _step == 3;
        UpdatePanel.IsVisible = _update != null && _step == 3;
        KeysPanel.IsVisible = _step == 0;
        FirmwarePanel.IsVisible = _step == 1;
        FilesPanel.IsVisible = _step == 2;
        bool installed = _step < 3 && GameInstaller.IsInstalled(RemasterProfile.GameRoot);
        BackButton.IsVisible = _step > 0 || installed;
        BackButton.Content = _step == 0 && installed ? "Back to Play" : "Back";
        StepLabel.Text = $"STEP {Math.Min(_step, 2) + 1} OF 3";
        for (int i = 0; i < StepBars.Children.Count; i++)
            ((Border)StepBars.Children[i]).Background = new SolidColorBrush(Color.Parse(i <= _step ? "#DEB46C" : "#3A4245"));
        Heading.Text = _step switch { 0 => "Make yourself at home.", 1 => "A little groundwork.", _ => "Bring your adventure." };
        Description.Text = _step switch
        {
            0 => "First, choose prod.keys from your own Switch. We keep a separate copy for this launcher. Your existing emulator stays untouched.",
            1 => "Choose the firmware dumped from your Switch, as a ZIP or folder. We'll install it automatically. Your keys must support this firmware.",
            _ => "Choose the official Six Guns 2.9.9a Android APK and the folder containing its main and patch OBB files. We'll check and install everything for you. Allow about 2 GB of free space.",
        };
        if (_step == 3) PlayButton.Focus();
    }

    private async Task<string> PickFile(string title, params string[] patterns)
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(title) { Patterns = patterns } },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string> PickFolder(string title)
    {
        var folders = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task Run(Func<Task> operation, bool cancellable = false)
    {
        if (_busy) return;
        _busy = true;
        SetupPanel.IsEnabled = ReadyPanel.IsEnabled = RepairButton.IsEnabled = CheckUpdatesButton.IsEnabled = false;
        Progress.IsVisible = true;
        CancelButton.IsVisible = cancellable;
        Status.Text = "Preparing…";
        Status.Foreground = NormalText;
        _cancellation = new CancellationTokenSource();
        try { await operation(); }
        catch (OperationCanceledException) { Status.Text = "Cancelled. Your existing game and saves are unchanged."; }
        catch (Exception ex)
        {
            Ryujinx.Common.Logging.Logger.Error?.Print(Ryujinx.Common.Logging.LogClass.Application, ex.ToString());
            Status.Text = "That couldn't finish. " + ex.Message;
            Status.Foreground = ErrorText;
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _busy = false;
            SetupPanel.IsEnabled = ReadyPanel.IsEnabled = RepairButton.IsEnabled = CheckUpdatesButton.IsEnabled = true;
            Progress.IsVisible = CancelButton.IsVisible = false;
            Progress.IsIndeterminate = true;
        }
    }

    private async void ChooseKeys(object sender, RoutedEventArgs e)
    {
        await Run(async () =>
        {
            string path = await PickFile("Choose prod.keys", "*.keys");
            if (path == null) { Status.Text = ""; return; }
            GameInstaller.ValidateKeys(path);
            string target = Path.Combine(AppDataManager.KeysDirPath, "prod.keys");
            Directory.CreateDirectory(AppDataManager.KeysDirPath);
            if (!Path.GetFullPath(path).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                string temporary = target + ".new";
                try
                {
                    File.Copy(path, temporary, true);
                    GameInstaller.ValidateKeys(temporary);
                    File.Move(temporary, target, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            _window.VirtualFileSystem.ReloadKeySet();
            MainWindow.ShowKeyErrorOnLoad = false;
            _step = 1;
            ShowStep();
            Status.Text = "Keys imported. Now choose matching firmware.";
        });
    }

    private async Task InstallFirmware(bool folder)
    {
        await Run(async () =>
        {
            string path = folder ? await PickFolder("Choose your Switch firmware folder") : await PickFile("Choose Switch firmware ZIP", "*.zip");
            if (path == null) { Status.Text = ""; return; }
            Status.Text = "Checking firmware and keys…";
            // Firmware installation is not interruptible. Closing is blocked until it finishes.
            await Task.Run(() =>
            {
                var version = _window.ContentManager.VerifyFirmwarePackage(path);
                if (version == null) throw new InvalidDataException("No supported firmware was found in that location.");
                Dispatcher.UIThread.Post(() => Status.Text = $"Installing firmware {version.VersionString}…");
                _window.ContentManager.InstallFirmware(path);
            });
            if (!HaveFirmware()) throw new InvalidDataException("Firmware could not be loaded. Check that your keys match the firmware.");
            _window.ViewModel.RefreshFirmwareStatus();
            _step = 2;
            ShowStep();
            Status.Text = "Firmware installed. Let's add your game.";
        });
    }

    private async void ChooseFirmwareZip(object sender, RoutedEventArgs e) => await InstallFirmware(false);
    private async void ChooseFirmwareFolder(object sender, RoutedEventArgs e) => await InstallFirmware(true);

    private async void ChooseApk(object sender, RoutedEventArgs e)
    {
        try
        {
            _apk = await PickFile("Choose the official Six Guns 2.9.9a APK", "*.apk") ?? _apk;
            ApkLabel.Text = _apk == null ? "No APK selected" : "✓ " + Path.GetFileName(_apk);
            if (_apk == null) ApkLabel.ClearValue(TextBlock.ForegroundProperty); else ApkLabel.Foreground = ChosenText;
            InstallButton.IsEnabled = _apk != null && _obb != null;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void ChooseObb(object sender, RoutedEventArgs e)
    {
        try
        {
            _obb = await PickFolder("Choose the folder containing both OBB files") ?? _obb;
            if (_obb == null) ObbLabel.Text = "No folder selected";
            else
            {
                // Early hint only; the SD Builder does the real checks.
                bool main = File.Exists(Path.Combine(_obb, GameInstaller.MainObb)), patch = File.Exists(Path.Combine(_obb, GameInstaller.PatchObb));
                ObbLabel.Text = main && patch ? "✓ " + Path.GetFileName(_obb) + ": both OBB files found"
                    : Path.GetFileName(_obb) + ": " + (!main && !patch ? "no Six Guns OBB files found here" : !main ? "the main OBB is missing" : "the patch OBB is missing");
            }
            if (_obb == null) ObbLabel.ClearValue(TextBlock.ForegroundProperty); else ObbLabel.Foreground = ObbLabel.Text.StartsWith("✓") ? ChosenText : ErrorText;
            InstallButton.IsEnabled = _apk != null && _obb != null;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void InstallGame(object sender, RoutedEventArgs e)
    {
        await Run(async () =>
        {
            var token = _cancellation.Token;
            await Task.Run(() => GameInstaller.Install(_apk, _obb, RemasterProfile.SdBuilder, RemasterProfile.GameRoot,
                message => Dispatcher.UIThread.Post(() => Status.Text = message), token));
            Status.Text = "You're all set. Your saves will be kept automatically.";
            Refresh();
        }, true);
    }

    private async void Play(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        PlayButton.IsEnabled = false;
        Status.Foreground = NormalText;
        try
        {
            if (!GameInstaller.IsInstalled(RemasterProfile.GameRoot)) { Refresh(); return; }
            // A cancelled or failed update rebuild leaves game data from the previous port: never run the new
            // port on it. Rebuild first (from the kept files), then start.
            if (GameInstaller.NeedsRebuild(RemasterProfile.GameRoot, RemasterProfile.SdBuilder))
            {
                await RebuildGame();
                if (GameInstaller.NeedsRebuild(RemasterProfile.GameRoot, RemasterProfile.SdBuilder)) return;
            }
            // The newest port: a just-published SixGuns-NX release (what Switch players update to) when it is
            // downloaded in time, else the bundled one. Only the executable changes; game data and saves stay.
            if (!_portRefresh.IsCompleted)
            {
                Status.Text = "Getting the latest Six Guns port…";
                await Task.WhenAny(_portRefresh, Task.Delay(TimeSpan.FromSeconds(20)));
                Status.Text = "";
            }
            string port = PortUpdater.Choose(RemasterProfile.PortCache, RemasterProfile.BundledNro, RemasterProfile.SdBuilder).Path;
            GameInstaller.ValidatePort(port);
            File.Copy(port, RemasterProfile.GamePath, true);
            File.WriteAllText(Path.Combine(RemasterProfile.GameRoot, "emulator_mode"), "");
            RemasterProfile.Apply();
            if (!_window.ApplicationLibrary.TryGetApplicationsFromFile(RemasterProfile.GamePath, out var apps) || apps.Count == 0)
                throw new InvalidDataException("The bundled port could not be loaded. Extract a fresh copy of the launcher.");
            _window.ViewModel.SelectedApplication = apps[0];
            _window.ViewModel.SelectedIcon = apps[0].Icon;
            await _window.ViewModel.LoadApplication(apps[0]);
        }
        catch (Exception ex)
        {
            Ryujinx.Common.Logging.Logger.Error?.Print(Ryujinx.Common.Logging.LogClass.Application, ex.ToString());
            Status.Text = "Couldn't start the game. " + ex.Message;
            Status.Foreground = ErrorText;
        }
        finally { PlayButton.IsEnabled = true; }
    }

    private async void Controls(object sender, RoutedEventArgs e)
    {
        try { await StyleableAppWindow.ShowAsync(new SettingsWindow(_window.VirtualFileSystem, _window.ContentManager)); }
        catch (Exception ex) { Status.Text = "Couldn't open controls. " + ex.Message; }
    }

    private void Cancel(object sender, RoutedEventArgs e) => _cancellation?.Cancel();
    private void Quit(object sender, RoutedEventArgs e) => _window.Close();
    private void RestartSetup(object sender, RoutedEventArgs e) { _step = 0; Status.Foreground = NormalText; Status.Text = "Your saves and controller mappings will be kept."; ShowStep(); }
    private void GoBack(object sender, RoutedEventArgs e)
    {
        Status.Text = "";
        Status.Foreground = NormalText;
        if (_step == 0) { Refresh(); return; } // left "Set up files again" without changes
        _step--;
        ShowStep();
    }
}
