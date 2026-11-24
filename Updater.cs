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
    private const string Repository = "https://github.com/Marvisak/VIctory.git";
    private static readonly string Updates = Path.Combine(AppData, "Updates");
    private static readonly string ActiveFile = Path.Combine(Updates, "active.txt");

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

    private static string FetchUpdate(IProgress<string> progress)
    {
        Directory.CreateDirectory(Updates);
        using (var updateLock = new FileStream(Path.Combine(Updates, "update.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None))
        {
            string source = Path.Combine(Updates, "source");
            Directory.CreateDirectory(source);
            string git = FindTool("git");
            RunTool(git, "init --quiet", source);
            RunTool(git, "fetch --depth=1 --no-tags " + Repository + " HEAD", source);
            string revision = RunTool(git, "rev-parse FETCH_HEAD", source).Trim();
            if (!Regex.IsMatch(revision, "^[0-9a-f]{40}$")) throw new IOException("Git returned an invalid revision.");
            return ActivateRevision(progress, source, git, revision);
        }
    }

    private static string ActivateRevision(IProgress<string> progress, string source, string git, string revision)
    {
        string shortId = revision.Substring(0, 8);
        try
        {
            var active = ReadActive();
            string current = active == null ? "015d45bfff4d82e7afad89afe10dd24e5a402ef8" : active[0];
            if (current == revision && ProbeRuntime() == null)
                return "VIctory is already up to date (" + shortId + ").";
        }
        catch (IOException) { /* Rebuild a damaged installation. */ }

        string cargo = FindTool("cargo");
        RunTool(git, "checkout --quiet --detach --force " + revision, source);
        progress.Report("Building " + shortId + "…");
        RunTool(cargo, "build --locked --profile diag -p victory --features diagnostics --target x86_64-pc-windows-msvc --target-dir ../build", source);
        string slot = Guid.NewGuid().ToString("N");
        var next = new[] { revision, slot, "" };
        string exe = RuntimePath(next);
        Directory.CreateDirectory(Path.GetDirectoryName(exe));
        File.Copy(Path.Combine(Updates, "build", "x86_64-pc-windows-msvc", "diag", "victory.exe"), exe);
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

    private static string FindTool(string name)
    {
        foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            string directory = entry.Trim().Trim('"');
            if (!Path.IsPathRooted(directory)) continue;
            string path = Path.Combine(directory, name + ".exe");
            if (File.Exists(path)) return path;
        }
        throw new IOException(name == "git" ? "Install Git for Windows and reopen Americas to update VIctory." :
            "Building a new revision requires Rust (rustup) and Visual Studio C++ Build Tools with the Windows SDK. Install them and reopen Americas.");
    }

    private static string RunTool(string tool, string arguments, string directory)
    {
        var start = new ProcessStartInfo(tool, arguments) {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
        using (var process = Process.Start(start))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                string detail = errors.Result.Trim();
                if (detail.Length > 1600) detail = detail.Substring(detail.Length - 1600);
                throw new IOException(Path.GetFileNameWithoutExtension(tool) + " failed. " + detail);
            }
            return output.Result;
        }
    }
}
