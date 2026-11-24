using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

internal sealed partial class Launcher
{
    private const string BuildTarget = "x86_64-pc-windows-msvc";
    private const string CppComponent = "Microsoft.VisualStudio.Component.VC.Tools.x86.x64";
    private const string SdkComponent = "Microsoft.VisualStudio.Component.Windows11SDK.26100";
    private static readonly string VsInstaller = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer");

    private static bool InstallBuildPrerequisites(IProgress<string> progress)
    {
        if (FindTool("git") == null)
        {
            progress.Report("Installing Git for Windows…");
            if (InstallPackage("Git.Git")) return true;
        }

        if (!HasNativeBuildTools())
        {
            progress.Report("Installing MSVC and the Windows SDK…");
            string components = " --add " + CppComponent + " --add " + SdkComponent;
            string instance = FindVisualStudio("-products Microsoft.VisualStudio.Product.BuildTools -version \"[17.0,18.0)\"");
            if (string.IsNullOrEmpty(instance))
            {
                if (InstallPackage("Microsoft.VisualStudio.2022.BuildTools",
                    "--passive --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools" + components))
                    return true;
            }
            else if (InstallerNeedsRestart(RunInstaller(Path.Combine(VsInstaller, "setup.exe"),
                "modify --installPath \"" + instance + "\" --passive --norestart" + components, true), "Visual Studio Build Tools"))
                return true;

            if (!HasNativeBuildTools())
                throw new IOException("MSVC or the Windows SDK is still missing. Close Visual Studio and try again.");
        }

        if (FindTool("rustup") == null)
        {
            progress.Report("Installing Rustup…");
            if (InstallPackage("Rustlang.Rustup", "-y --default-toolchain none")) return true;
        }

        return WithUpdateLock(() =>
        {
            if (FindDxc(ReadWindowsSdkRoot(), DxcFolder) == null)
            {
                progress.Report("Installing Microsoft DXC 1.9 x64…");
                InstallDxc(DxcFolder);
            }
            CheckDxc();
            progress.Report("Downloading the VIctory build configuration…");
            CheckoutLatest(RequireTool("git"));
            string rustup = RequireTool("rustup");
            string toolchain = ReadToolchain();
            progress.Report("Installing Rust " + toolchain + " and downloading Cargo dependencies…");
            RunTool(rustup, "toolchain install " + toolchain + " --profile minimal --target " + BuildTarget, UpdateSource);
            RunTool(rustup, "run " + toolchain + " cargo fetch --locked --target " + BuildTarget, UpdateSource);
            progress.Report("Testing the Rust compiler and MSVC linker…");
            CheckCompiler(rustup, toolchain);
            return false;
        });
    }

    private static void CheckBuildPrerequisites()
    {
        RunTool(RequireTool("git"), "--version", Path.GetTempPath());
        string rustup = RequireTool("rustup");
        if (!HasNativeBuildTools())
            throw new IOException("MSVC or the Windows SDK is missing. Select Install prerequisites.");
        CheckDxc();
        WithUpdateLock(() =>
        {
            string toolchain = ReadToolchain();
            CheckCompiler(rustup, toolchain);
            RunTool(rustup, "run " + toolchain + " cargo fetch --offline --locked --target " + BuildTarget, UpdateSource);
            return true;
        });
    }

    private static void CheckDxc()
    {
        string directory = RequireDxc();
        RunTool(Path.Combine(directory, "dxc.exe"), "--version", directory);
    }

    private static string ReadToolchain()
    {
        string file = Path.Combine(UpdateSource, "rust-toolchain.toml");
        if (!File.Exists(file))
            throw new IOException("The VIctory toolchain has not been set up. Select Install prerequisites.");
        var channel = Regex.Match(File.ReadAllText(file),
            @"^\s*channel\s*=\s*""(\d+\.\d+\.\d+|stable|beta|nightly(?:-\d{4}-\d{2}-\d{2})?)""\s*(?:#[^\r\n]*)?$", RegexOptions.Multiline);
        if (!channel.Success)
            throw new IOException("VIctory's Rust toolchain channel is missing or unsupported.");
        return channel.Groups[1].Value + "-" + BuildTarget;
    }

    private static void CheckCompiler(string rustup, string toolchain)
    {
        string directory = Path.Combine(Path.GetTempPath(), "Americas-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "check.rs"), "fn main() {}");
            RunTool(rustup, "run " + toolchain + " rustc --target " + BuildTarget + " check.rs", directory);
        }
        finally { RemoveSessionData(directory); }
    }

    private static string FindVisualStudio(string arguments)
    {
        string vswhere = Path.Combine(VsInstaller, "vswhere.exe");
        return File.Exists(vswhere) ? RunTool(vswhere, "-latest " + arguments + " -property installationPath", Path.GetTempPath()).Trim() : "";
    }

    private static bool HasNativeBuildTools()
    {
        if (string.IsNullOrEmpty(FindVisualStudio("-products * -requires " + CppComponent)))
            return false;

        string sdkRoot = ReadWindowsSdkRoot();
        if (string.IsNullOrEmpty(sdkRoot)) return false;
        string libraries = Path.Combine(sdkRoot, "Lib");
        if (!Directory.Exists(libraries)) return false;
        foreach (string version in Directory.EnumerateDirectories(libraries))
        {
            if (File.Exists(Path.Combine(version, "um", "x64", "kernel32.lib")) &&
                File.Exists(Path.Combine(version, "ucrt", "x64", "ucrt.lib")) &&
                File.Exists(Path.Combine(sdkRoot, "Include", Path.GetFileName(version), "um", "Windows.h")))
                return true;
        }
        return false;
    }

    private static string ReadWindowsSdkRoot()
    {
        const string kitsKey = @"SOFTWARE\Microsoft\Windows Kits\Installed Roots";
        using (var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
        using (var kits = registry.OpenSubKey(kitsKey))
            return kits == null ? null : kits.GetValue("KitsRoot10") as string;
    }

    private static bool InstallPackage(string package, string installerArguments = null)
    {
        string winget = FindTool("winget");
        if (winget == null)
            throw new IOException("Windows Package Manager is missing. Install or update App Installer from Microsoft Store, then retry.");
        string arguments = "install --exact --id " + package +
            " --source winget --accept-source-agreements --accept-package-agreements --disable-interactivity";
        arguments += installerArguments == null ? " --silent" : " --override \"" + installerArguments + "\"";
        return InstallerNeedsRestart(RunInstaller(winget, arguments), package);
    }

    private static int RunInstaller(string file, string arguments, bool elevate = false)
    {
        var start = new ProcessStartInfo(file, arguments) { UseShellExecute = true, WorkingDirectory = Path.GetTempPath() };
        if (elevate) start.Verb = "runas";
        using (var process = Process.Start(start))
        {
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    private static bool InstallerNeedsRestart(int result, string package)
    {
        // WinGet can wrap an installer's reboot status in an HRESULT.
        if (result == 3010 || result == 1641 || result == unchecked((int)0x8A150109) ||
            result == unchecked((int)0x8A15010A) || result == unchecked((int)0x8A15010B))
            return true;
        if (result != 0)
            throw new IOException(package + " installation returned code 0x" + result.ToString("X8") + ".");
        return false;
    }

    private static string ToolSearchPath()
    {
        string cargoHome = Environment.GetEnvironmentVariable("CARGO_HOME");
        if (string.IsNullOrEmpty(cargoHome))
            cargoHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo");
        return string.Join(";", Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Path.Combine(cargoHome, "bin"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps"));
    }

    private static string FindTool(string name)
    {
        foreach (string entry in ToolSearchPath().Split(';'))
        {
            string directory = entry.Trim().Trim('"');
            if (!Path.IsPathRooted(directory)) continue;
            string path = Path.Combine(directory, name + ".exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static string RequireTool(string name)
    {
        string path = FindTool(name);
        if (path == null) throw new IOException(name + " is missing. Select Install prerequisites.");
        return path;
    }
}
