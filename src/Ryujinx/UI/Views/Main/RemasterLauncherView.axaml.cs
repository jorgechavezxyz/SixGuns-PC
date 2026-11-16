using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Ryujinx.Ava.Remaster;
using Ryujinx.Ava.UI.Windows;
using Ryujinx.Common.Configuration;
using System;
using System.IO;
using System.Linq;
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
        Refresh();
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
        KeysPanel.IsVisible = _step == 0;
        FirmwarePanel.IsVisible = _step == 1;
        FilesPanel.IsVisible = _step == 2;
        BackButton.IsVisible = _step > 0;
        StepLabel.Text = $"STEP {_step + 1} OF 3";
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
        SetupPanel.IsEnabled = ReadyPanel.IsEnabled = RepairButton.IsEnabled = false;
        Progress.IsVisible = true;
        CancelButton.IsVisible = cancellable;
        Status.Text = "Preparing…";
        _cancellation = new CancellationTokenSource();
        try { await operation(); }
        catch (OperationCanceledException) { Status.Text = "Installation cancelled. Your existing game and saves are unchanged."; }
        catch (Exception ex) { Status.Text = "Setup couldn't finish. " + ex.Message; }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _busy = false;
            SetupPanel.IsEnabled = ReadyPanel.IsEnabled = RepairButton.IsEnabled = true;
            Progress.IsVisible = CancelButton.IsVisible = false;
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
            ApkLabel.Text = _apk == null ? "No APK selected" : Path.GetFileName(_apk);
            InstallButton.IsEnabled = _apk != null && _obb != null;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void ChooseObb(object sender, RoutedEventArgs e)
    {
        try
        {
            _obb = await PickFolder("Choose the folder containing both OBB files") ?? _obb;
            ObbLabel.Text = _obb == null ? "No folder selected" : Path.GetFileName(_obb);
            InstallButton.IsEnabled = _apk != null && _obb != null;
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async void InstallGame(object sender, RoutedEventArgs e)
    {
        await Run(async () =>
        {
            var token = _cancellation.Token;
            await Task.Run(() => GameInstaller.Install(_apk, _obb, RemasterProfile.BundledNro, RemasterProfile.GameRoot,
                message => Dispatcher.UIThread.Post(() => Status.Text = message), token));
            Status.Text = "You're all set. Your saves will be kept automatically.";
            Refresh();
        }, true);
    }

    private async void Play(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        PlayButton.IsEnabled = false;
        try
        {
            if (!GameInstaller.IsInstalled(RemasterProfile.GameRoot)) { Refresh(); return; }
            // Update only our executable; game data and saves remain in place.
            File.Copy(RemasterProfile.BundledNro, RemasterProfile.GamePath, true);
            File.WriteAllText(Path.Combine(RemasterProfile.GameRoot, "emulator_mode"), "");
            RemasterProfile.Apply();
            if (!_window.ApplicationLibrary.TryGetApplicationsFromFile(RemasterProfile.GamePath, out var apps) || apps.Count == 0)
                throw new InvalidDataException("The bundled port could not be loaded. Extract a fresh copy of the launcher.");
            _window.ViewModel.SelectedApplication = apps[0];
            _window.ViewModel.SelectedIcon = apps[0].Icon;
            await _window.ViewModel.LoadApplication(apps[0]);
        }
        catch (Exception ex) { Status.Text = "Couldn't start the game. " + ex.Message; }
        finally { PlayButton.IsEnabled = true; }
    }

    private async void Controls(object sender, RoutedEventArgs e)
    {
        try { await StyleableAppWindow.ShowAsync(new SettingsWindow(_window.VirtualFileSystem, _window.ContentManager)); }
        catch (Exception ex) { Status.Text = "Couldn't open controls. " + ex.Message; }
    }

    private void Cancel(object sender, RoutedEventArgs e) => _cancellation?.Cancel();
    private void Quit(object sender, RoutedEventArgs e) => _window.Close();
    private void RestartSetup(object sender, RoutedEventArgs e) { _step = 0; Status.Text = "Your saves and controller mappings will be kept."; ShowStep(); }
    private void GoBack(object sender, RoutedEventArgs e) { _step = Math.Max(0, _step - 1); Status.Text = ""; ShowStep(); }
}
