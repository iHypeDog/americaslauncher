using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

internal sealed partial class Launcher
{
    private const string Repository = "https://github.com/fxceless/victory-gta6.git";
    private const string BundledRevision = "90228a7dc66010281fdcfad10ac00c3a252be2f3";
    private static readonly string Updates = Path.Combine(AppData, "Updates", "victory-gta6");
    private static readonly string ActiveFile = Path.Combine(Updates, "active.txt");
    private static readonly string UpdateSource = Path.Combine(Updates, "source");

    private static string[] ReadActive()
    {
        if (!File.Exists(ActiveFile)) return null;
        var lines = File.ReadAllLines(ActiveFile);
        Guid folder;
        if (lines.Length != 3 || !Regex.IsMatch(lines[0], "^[0-9a-f]{40}$") ||
            !Guid.TryParseExact(lines[1], "N", out folder) || !Regex.IsMatch(lines[2], "^[A-Za-z0-9+/]{43}=$"))
            throw new IOException("The installed update record is damaged. Run update version again.");
        return lines;
    }

    private static string RuntimePath(string[] active)
    {
        return Path.Combine(active == null ? MediaFolder : Path.Combine(Updates, active[1]), "victory.exe");
    }

    private async void UpdateVictory()
    {
        if (installing) return;
        installing = true;
        SetBusy(true);
        var button = Find<Button>("Update");
        try
        {
            button.Content = "Checking GitHub…";
            IProgress<string> progress = new Progress<string>(value => button.Content = value);
            string message = await Task.Run(() => FetchUpdate(progress));
            MessageBox.Show(window, message, "Americas update", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(window, error.Message, "Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { button.Content = "update version"; installing = false; SetBusy(false); }
    }

    private static T WithUpdateLock<T>(Func<T> work)
    {
        Directory.CreateDirectory(Updates);
        using (var updateLock = new FileStream(Path.Combine(Updates, "update.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
            return work();
    }

    private static string CheckoutLatest(string git)
    {
        Directory.CreateDirectory(UpdateSource);
        RunTool(git, "init --quiet", UpdateSource);
        RunTool(git, "fetch --depth=1 --no-tags " + Repository + " HEAD", UpdateSource);
        string revision = RunTool(git, "rev-parse FETCH_HEAD", UpdateSource).Trim();
        if (!Regex.IsMatch(revision, "^[0-9a-f]{40}$")) throw new IOException("Git returned an invalid revision.");
        RunTool(git, "checkout --quiet --detach --force " + revision, UpdateSource);
        return revision;
    }

    private static string FetchUpdate(IProgress<string> progress)
    {
        return WithUpdateLock(() =>
        {
            string revision = CheckoutLatest(RequireTool("git"));
            return ActivateRevision(progress, revision);
        });
    }

    private static string ActivateRevision(IProgress<string> progress, string revision)
    {
        string shortId = revision.Substring(0, 8);
        try
        {
            var active = ReadActive();
            string current = active == null ? BundledRevision : active[0];
            if (current == revision && ProbeRuntime() == null)
                return "VIctory is already up to date (" + shortId + ").";
        }
        catch (IOException) { /* Rebuild a damaged installation. */ }

        string rustup = RequireTool("rustup");
        string toolchain = ReadToolchain();
        string dxc = RequireDxc();
        progress.Report("Building " + shortId + "…");
        RunTool(rustup, "run --install " + toolchain + " cargo build --locked --release -p victory --target " +
            BuildTarget + " --target-dir ../build", UpdateSource, dxc);
        string slot = Guid.NewGuid().ToString("N");
        var next = new[] { revision, slot, "" };
        string exe = RuntimePath(next);
        Directory.CreateDirectory(Path.GetDirectoryName(exe));
        File.Copy(Path.Combine(Updates, "build", BuildTarget, "release", "victory.exe"), exe);
        progress.Report("Testing update…");
        string problem = TestStartup(exe);
        if (problem != null) throw new IOException(problem);
        using (var sha = SHA256.Create())
        using (var disk = File.OpenRead(exe))
            next[2] = Convert.ToBase64String(sha.ComputeHash(disk));
        string pending = Path.Combine(Updates, slot + ".tmp");
        File.WriteAllLines(pending, next);
        if (File.Exists(ActiveFile)) File.Replace(pending, ActiveFile, null);
        else File.Move(pending, ActiveFile);
        return "VIctory updated to " + shortId + ". Press Play to use this version.";
    }

    private static string RunTool(string tool, string arguments, string directory, string dxc = null)
    {
        var start = new ProcessStartInfo(tool, arguments) {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        start.EnvironmentVariables["PATH"] = ToolSearchPath();
        SetDxcEnvironment(start, dxc);
        using (var process = Process.Start(start))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                string detail = errors.Result.Trim();
                if (detail.Length == 0) detail = output.Result.Trim();
                if (detail.Length > 1600) detail = detail.Substring(detail.Length - 1600);
                throw new IOException(Path.GetFileNameWithoutExtension(tool) + " failed (exit code " + process.ExitCode + "). " + detail);
            }
            return output.Result;
        }
    }
}
