using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

internal sealed partial class Launcher
{
    private static readonly Assembly Assembly = typeof(Launcher).Assembly;
    private static readonly string AppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ViceCityLauncher");
    private static readonly string SelectionFile = Path.Combine(AppData, "game-path.txt");
    private static readonly string MediaFolder = Path.Combine(
        AppData, "Media", Assembly.ManifestModule.ModuleVersionId.ToString("N"));

    private readonly Window window;
    private readonly Grid options;
    private readonly Border modsPage;
    private readonly MediaElement preview;
    private readonly Button motionButton;
    private Uri backgroundSource;
    private TimeSpan backgroundPosition;
    private string gamePath;
    private string buildFolder;
    private bool installing;
    private bool gameRunning;
    private bool motion = SystemParameters.ClientAreaAnimation;
    private IInputElement previousFocus;

    [STAThread]
    private static void Main()
    {
        var app = new Application();
        try
        {
            app.Run(new Launcher().window);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "Americas", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private Launcher()
    {
        using (var layout = Resource("Launcher.xaml"))
            window = (Window)XamlReader.Load(layout);

        FitWorkingArea();
        window.Icon = LoadBitmap("app-icon.png");
        Find<Border>("AppIcon").Background = new ImageBrush(window.Icon);
        options = Find<Grid>("Options");
        modsPage = Find<Border>("ModsPage");
        preview = Find<MediaElement>("Preview");
        Find<Image>("Poster").Source = LoadBitmap("background.jpg");
        motionButton = Find<Button>("ToggleMotion");
        var hero = Find<Grid>("Hero");
        hero.SizeChanged += delegate { hero.Clip = new RectangleGeometry(new Rect(hero.RenderSize), 24, 24); };

        RestoreGameSelection();
        Find<Grid>("TitleBar").MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                window.DragMove();
        };

        Click("Minimize", () => window.WindowState = WindowState.Minimized);
        Click("Close", window.Close);
        Click("Settings", ShowOptions);
        Click("Play", PlayGame);
        Click("Update", UpdateVictory);
        Click("CloseOptions", HideOptions);
        Click("Browse", BrowseFolder);
        Click("BrowseExe", Browse);
        Click("Install", Install);
        Click("Check", async () => { await CheckPrerequisites(); });
        Find<RadioButton>("GameTab").Checked += delegate { ShowMods(false); };
        Find<RadioButton>("ModsTab").Checked += delegate { ShowMods(true); };
        Find<Border>("OptionsBackdrop").MouseLeftButtonUp += delegate { HideOptions(); };
        motionButton.Click += delegate { motion = !motion; UpdatePlayback(); };

        window.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && options.IsVisible)
            {
                HideOptions();
                e.Handled = true;
            }
        };
        window.Activated += delegate { UpdatePlayback(); };
        window.Deactivated += delegate { ReleasePlayback(); };
        window.StateChanged += delegate { UpdatePlayback(); };
        preview.MediaOpened += delegate
        {
            if (preview.Source == null)
                return;

            if (backgroundPosition > TimeSpan.Zero)
                preview.Position = backgroundPosition;
            preview.Opacity = 1;
        };
        preview.MediaEnded += delegate
        {
            backgroundPosition = TimeSpan.Zero;
            if (preview.Source == null)
                return;

            preview.Position = TimeSpan.Zero;
            preview.Play();
        };
        preview.MediaFailed += delegate { PlaybackFailed(); };
        window.Loaded += delegate { UpdatePlayback(); };
        window.Closed += delegate { ReleasePlayback(); };
    }

    private void FitWorkingArea()
    {
        var area = SystemParameters.WorkArea;
        var scale = Math.Min((area.Width - 24) / window.Width, (area.Height - 24) / window.Height);
        if (scale >= 1)
            return;

        var content = (FrameworkElement)window.Content;
        content.Width = window.Width;
        content.Height = window.Height;
        window.Content = null;
        window.Content = new Viewbox { Child = content };
        window.Width *= scale;
        window.Height *= scale;
    }

    private T Find<T>(string name) where T : FrameworkElement
    {
        return (T)window.FindName(name);
    }

    private void Click(string name, Action action)
    {
        Find<Button>(name).Click += delegate { action(); };
    }

    private void UpdatePlayback()
    {
        UpdateMotionButton(motion ? "Pause animated background" : "Play animated background");
        if (!motion || gameRunning || !window.IsLoaded || !window.IsActive || !preview.IsVisible ||
            window.WindowState == WindowState.Minimized || options.IsVisible)
        {
            ReleasePlayback();
            return;
        }

        if (preview.Source != null)
            return;

        try
        {
            if (backgroundSource == null)
                backgroundSource = new Uri(Extract("background.mp4"));
            preview.Source = backgroundSource;
            preview.Play();
        }
        catch (IOException) { PlaybackFailed(); }
        catch (UnauthorizedAccessException) { PlaybackFailed(); }
    }

    private void ReleasePlayback()
    {
        if (preview.Source == null)
            return;

        if (preview.Opacity == 1)
        {
            var position = preview.Position;
            var duration = preview.NaturalDuration;
            backgroundPosition = duration.HasTimeSpan && position >= duration.TimeSpan
                ? TimeSpan.Zero : position;
        }

        preview.Opacity = 0;
        preview.Close();
        preview.Source = null;
    }

    private void PlaybackFailed()
    {
        ReleasePlayback();
        backgroundPosition = TimeSpan.Zero;
        motion = false;
        UpdateMotionButton("Retry animated background");
    }

    private void UpdateMotionButton(string label)
    {
        motionButton.Content = motion ? "\uE769" : "\uE768";
        motionButton.ToolTip = label;
        AutomationProperties.SetName(motionButton, label);
    }

    private void ShowMods(bool show)
    {
        Find<Grid>("Hero").Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        modsPage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        UpdatePlayback();
    }

    private void ShowOptions()
    {
        previousFocus = Keyboard.FocusedElement;
        options.Visibility = Visibility.Visible;
        Find<Grid>("Dashboard").IsEnabled = false;
        Find<Button>("Browse").Focus();
        UpdatePlayback();
    }

    private void HideOptions()
    {
        options.Visibility = Visibility.Collapsed;
        Find<Grid>("Dashboard").IsEnabled = true;
        Keyboard.Focus(previousFocus ?? Find<Button>("Play"));
        UpdatePlayback();
    }

    private async void PlayGame()
    {
        if (installing || gameRunning) return;
        if (!Directory.Exists(buildFolder))
        {
            ShowOptions();
            SetLocalStatus("Choose an existing build folder.");
            return;
        }
        if (gamePath == null || !File.Exists(gamePath))
        {
            ShowOptions();
            if (gamePath != null)
                SetLocalStatus("The selected file is missing. Choose the build executable again.");
            return;
        }

        if (!await CheckPrerequisites(false))
        {
            ShowOptions();
            return;
        }
        LaunchGame(gamePath);
    }

    private void BrowseFolder()
    {
        using (var picker = new System.Windows.Forms.FolderBrowserDialog())
        {
            picker.Description = "Choose the folder containing the build files";
            picker.ShowNewFolderButton = false;
            if (Directory.Exists(buildFolder)) picker.SelectedPath = buildFolder;
            var owner = new System.Windows.Forms.NativeWindow();
            owner.AssignHandle(new System.Windows.Interop.WindowInteropHelper(window).Handle);
            try
            {
                if (picker.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
                    SaveGameSelection(gamePath, picker.SelectedPath);
            }
            finally { owner.ReleaseHandle(); }
        }
    }

    private void Browse()
    {
        var picker = new OpenFileDialog
        {
            Title = "Choose the build executable",
            InitialDirectory = Directory.Exists(buildFolder) ? buildFolder : "",
            Filter = "Windows application (*.exe)|*.exe",
            DefaultExt = ".exe",
            CheckFileExists = true
        };
        if (picker.ShowDialog(window) != true)
            return;

        if (!string.Equals(Path.GetExtension(picker.FileName), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            SetLocalStatus("Choose a file with the .exe extension.");
            return;
        }

        SaveGameSelection(picker.FileName, buildFolder);
    }

    private void RestoreGameSelection()
    {
        string path = null;
        string folder = null;
        try
        {
            if (File.Exists(SelectionFile))
            {
                var lines = File.ReadAllLines(SelectionFile);
                if (lines.Length > 0 && Path.IsPathRooted(lines[0]) &&
                    string.Equals(Path.GetExtension(lines[0]), ".exe", StringComparison.OrdinalIgnoreCase))
                    path = lines[0];

                if (lines.Length > 1 && Path.IsPathRooted(lines[1]))
                    folder = lines[1];
                else if (path != null && (lines.Length == 1 ||
                    string.Equals(lines[1], "victory", StringComparison.OrdinalIgnoreCase)))
                    folder = Path.GetDirectoryName(path);

                if (lines.Length > 1 && string.Equals(lines[1], "windows", StringComparison.OrdinalIgnoreCase))
                    path = null;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (ArgumentException) { }
        SelectGame(path, folder);
    }

    private void SaveGameSelection(string path, string folder)
    {
        SelectGame(path, folder);
        string temporary = SelectionFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(AppData);
            File.WriteAllLines(temporary, new[] { path ?? "", folder ?? "" });
            try
            {
                File.Move(temporary, SelectionFile);
            }
            catch (IOException)
            {
                if (!File.Exists(SelectionFile)) throw;
                File.Replace(temporary, SelectionFile, null);
            }
        }
        catch (IOException error) { SetLocalStatus("Could not save the build paths. " + error.Message); }
        catch (UnauthorizedAccessException error) { SetLocalStatus("Could not save the build paths. " + error.Message); }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void SelectGame(string path, string folder)
    {
        gamePath = path;
        buildFolder = folder;
        Find<TextBlock>("FolderPath").Text = string.IsNullOrEmpty(buildFolder) ? "No folder selected" : buildFolder;
        Find<TextBlock>("ExePath").Text = path ?? "No executable selected";
        SetLocalStatus(null);
        if (string.IsNullOrEmpty(Find<TextBlock>("InstallStatus").Text))
            Find<TextBlock>("InstallStatus").Text = "Install VIctory, Visual C++ x64, Git, Rust, MSVC, the Windows SDK and DXC 1.9.";
        Find<Button>("Play").ToolTip = path == null ? "Open game settings" : "Open " + Path.GetFileNameWithoutExtension(path);
    }

    private void SetLocalStatus(string message)
    {
        var status = Find<TextBlock>("LocalStatus");
        status.Text = message;
        status.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool HasCppRuntime()
    {
        const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";
        Version version;
        string value = Registry.GetValue(key, "Version", "") as string;
        return Equals(Registry.GetValue(key, "Installed", 0), 1) &&
            Version.TryParse((value ?? "").TrimStart('v', 'V'), out version) && version >= new Version(14, 44, 35211);
    }

    private static string ProbeRuntime()
    {
        if (!HasCppRuntime())
            return "Visual C++ x64 is missing or outdated. Select Install prerequisites.";
        var active = ReadActive();
        string exe = RuntimePath(active);
        if (active == null && !File.Exists(exe))
            exe = Extract("victory.exe");
        if (!File.Exists(exe))
            return "VIctory is missing. Select Install prerequisites.";
        using (var disk = File.OpenRead(exe))
        using (var bundled = active == null ? Resource("victory.exe") : null)
        using (var sha = SHA256.Create())
        {
            if (!Convert.ToBase64String(sha.ComputeHash(disk)).Equals((active == null ? Convert.ToBase64String(sha.ComputeHash(bundled)) : active[2])))
                return "VIctory failed its integrity check. Use update version to repair the active version.";
        }
        return TestStartup(exe);
    }
    private static string TestStartup(string exe)
    {
            var start = new ProcessStartInfo(exe, "config")
            {
                WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false, CreateNoWindow = true
            };
            using (var process = Process.Start(start))
            {
                if (!process.WaitForExit(10000))
                {
                    process.Kill();
                    return "VIctory did not respond within 10 seconds. Check prerequisites again.";
                }
                if (process.ExitCode != 0)
                    return "VIctory startup check failed (exit code " + process.ExitCode + ").";
            }
        return null;
    }

    private async Task<bool> CheckPrerequisites(bool includeBuild = true)
    {
        if (installing) return false;
        installing = true;
        var status = Find<TextBlock>("InstallStatus");
        SetBusy(true);
        status.Text = includeBuild ? "Checking runtime and build prerequisites…" : "Checking VIctory and Visual C++ x64…";
        try
        {
            string problem = await Task.Run(() =>
            {
                string runtimeProblem = ProbeRuntime();
                if (runtimeProblem == null && includeBuild)
                    CheckBuildPrerequisites();
                return runtimeProblem;
            });
            status.Text = problem ?? (includeBuild ? "Runtime and build checks passed." : "VIctory is ready to play.");
            return problem == null;
        }
        catch (Exception error) { status.Text = "Check failed. " + error.Message; return false; }
        finally { installing = false; SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        foreach (string name in new[] { "Check", "Install", "Update", "Browse", "BrowseExe" })
            Find<Button>(name).IsEnabled = !busy;
        Find<Button>("Play").IsEnabled = !busy && !gameRunning;
    }

    private async void Install()
    {
        if (installing) return;
        installing = true;
        var status = Find<TextBlock>("InstallStatus");
        SetBusy(true);
        try
        {
            IProgress<string> progress = new Progress<string>(message => status.Text = message);
            status.Text = "Preparing VIctory and Visual C++ x64…";
            bool restart = await Task.Run(() =>
            {
                Extract("LICENSE");
                Extract("victory-source.zip");
                Extract("victory.exe");
                if (!HasCppRuntime())
                {
                    int result = RunInstaller(Extract("VC_redist.x64.exe"), "/install /passive /norestart");
                    if (result == 3010 || result == 1641) return true;
                    if ((result != 0 && result != 1638) || !HasCppRuntime())
                        throw new IOException("Visual C++ installation returned code " + result + ".");
                }
                return InstallBuildPrerequisites(progress);
            });
            status.Text = restart ? "Restart Windows, then select Install prerequisites again to finish setup." :
                await Task.Run(() => ProbeRuntime()) ?? "Runtime and build prerequisites are ready.";
        }
        catch (Exception error) { status.Text = "Installation interrupted. " + error.Message; }
        finally { installing = false; SetBusy(false); }
    }

    private void LaunchGame(string path)
    {
        bool started = false;
        try
        {
            SetGameRunning(true);
            var process = Process.Start(GameStartInfo(path));
            var watcher = new Thread(() =>
            {
                try { using (process) process.WaitForExit(); }
                finally
                {
                    if (!window.Dispatcher.HasShutdownStarted)
                        window.Dispatcher.BeginInvoke((Action)(() => SetGameRunning(false)));
                }
            }) { IsBackground = true };
            watcher.Start();
            started = true;
        }
        catch (IOException error) { ShowError(error); }
        catch (UnauthorizedAccessException error) { ShowError(error); }
        catch (System.ComponentModel.Win32Exception error) { ShowError(error); }
        catch (InvalidOperationException error) { ShowError(error); }
        finally { if (!started) SetGameRunning(false); }
    }

    private ProcessStartInfo GameStartInfo(string path)
    {
        string runtime = RuntimePath(ReadActive());
        var start = new ProcessStartInfo(runtime, "run \"" + path + "\" --execute")
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(runtime)
        };
        start.EnvironmentVariables["VICTORY_GAME_DIR"] = buildFolder;
        start.EnvironmentVariables.Remove("VICTORY_D3D12_WARP");
        SetDxcEnvironment(start, FindDxc(ReadWindowsSdkRoot(), DxcFolder));
        return start;
    }

    private void SetGameRunning(bool running)
    {
        gameRunning = running;
        Find<Button>("Play").IsEnabled = !installing && !running;
        UpdatePlayback();
    }

    private static void RemoveTemporaryDirectory(string directory)
    {
        try { Directory.Delete(directory, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void ShowError(Exception error)
    {
        MessageBox.Show(window, error.Message, "Americas", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static Stream Resource(string name)
    {
        var stream = Assembly.GetManifestResourceStream(name);
        if (stream == null)
            throw new FileNotFoundException("A launcher resource is missing.", name);
        return stream;
    }

    private static BitmapSource LoadBitmap(string file)
    {
        using (var stream = Resource(file))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
    }

    private static string Extract(string file)
    {
        Directory.CreateDirectory(MediaFolder);
        string destination = Path.Combine(MediaFolder, file);
        using (var stream = Resource(file))
        {
            var existing = new FileInfo(destination);
            if (existing.Exists && existing.Length == stream.Length)
            {
                using (var hash = SHA256.Create())
                using (var disk = File.OpenRead(destination))
                {
                    if (Convert.ToBase64String(hash.ComputeHash(disk)) == Convert.ToBase64String(hash.ComputeHash(stream)))
                        return destination;
                    stream.Position = 0;
                }
            }

            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = File.Create(temporary))
                    stream.CopyTo(output);

                try
                {
                    if (existing.Exists)
                        File.Replace(temporary, destination, null);
                    else
                        File.Move(temporary, destination);
                }
                catch (IOException)
                {
                    existing.Refresh();
                    if (!existing.Exists || existing.Length != stream.Length)
                        throw;
                }
            }
            finally { File.Delete(temporary); }
        }
        return destination;
    }

}
