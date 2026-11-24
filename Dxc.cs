using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Authentication;
using System.Security.Cryptography;

internal sealed partial class Launcher
{
    private const string DxcRelease = "1.9.2602.24";
    private const string DxcArchive = "https://github.com/microsoft/DirectXShaderCompiler/releases/download/v" +
        DxcRelease + "/dxc_2026_05_27.zip";
    private const string DxcArchiveSha256 = "cf658aacf070d3045e31b8f1f8a696c2945f37c1095019481ef7c513368db3b4";
    private static readonly string DxcFolder = Path.Combine(AppData, "Tools", "DXC", DxcRelease);
    private static readonly string[] DxcFiles = { "dxc.exe", "dxcompiler.dll", "dxil.dll" };

    private static bool HasDxc(string directory)
    {
        foreach (string name in DxcFiles)
        {
            string file = Path.Combine(directory, name);
            if (!File.Exists(file)) return false;
            var version = FileVersionInfo.GetVersionInfo(file);
            if (new Version(version.FileMajorPart, version.FileMinorPart) < new Version(1, 9))
                return false;
        }
        return true;
    }

    private static string FindDxc(string sdkRoot, string installedFolder)
    {
        if (HasDxc(installedFolder)) return installedFolder;
        if (string.IsNullOrEmpty(sdkRoot)) return null;

        string bin = Path.Combine(sdkRoot, "bin");
        if (!Directory.Exists(bin)) return null;
        string result = null;
        var newest = new Version(0, 0);
        foreach (string directory in Directory.EnumerateDirectories(bin))
        {
            Version version;
            if (!Version.TryParse(Path.GetFileName(directory), out version) || version <= newest)
                continue;

            string candidate = Path.Combine(directory, "x64");
            if (!HasDxc(candidate)) continue;
            result = candidate;
            newest = version;
        }
        return result;
    }

    private static string RequireDxc()
    {
        string directory = FindDxc(ReadWindowsSdkRoot(), DxcFolder);
        if (directory == null)
            throw new IOException("DXC 1.9 or later (x64) is missing. Select Install prerequisites.");
        return directory;
    }

    private static void InstallDxc(string installedFolder)
    {
        byte[] payload;
        using (var handler = new HttpClientHandler { SslProtocols = SslProtocols.Tls12 })
        using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) })
            payload = client.GetByteArrayAsync(DxcArchive).GetAwaiter().GetResult();

        using (var sha = SHA256.Create())
        {
            string digest = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", "");
            if (!string.Equals(digest, DxcArchiveSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The Microsoft DXC download failed its SHA-256 integrity check.");
        }

        string staging = installedFolder + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(staging);
        try
        {
            using (var archive = new ZipArchive(new MemoryStream(payload, false)))
            {
                foreach (string name in DxcFiles)
                {
                    var entry = archive.GetEntry("bin/x64/" + name);
                    if (entry == null) throw new IOException("The Microsoft DXC download is missing " + name + ".");
                    using (var input = entry.Open())
                    using (var output = File.Create(Path.Combine(staging, name)))
                        input.CopyTo(output);
                }
            }

            if (!HasDxc(staging))
                throw new IOException("The Microsoft DXC download did not contain a complete DXC 1.9 x64 installation.");
            if (Directory.Exists(installedFolder)) Directory.Delete(installedFolder, true);
            Directory.Move(staging, installedFolder);
        }
        finally { RemoveTemporaryDirectory(staging); }
    }

    private static void SetDxcEnvironment(ProcessStartInfo start, string directory)
    {
        if (directory == null) return;
        if (string.IsNullOrEmpty(start.EnvironmentVariables["VICTORY_DXIL"]))
            start.EnvironmentVariables["VICTORY_DXIL"] = Path.Combine(directory, "dxil.dll");
        if (string.IsNullOrEmpty(start.EnvironmentVariables["VICTORY_PC_DXC"]))
            start.EnvironmentVariables["VICTORY_PC_DXC"] = Path.Combine(directory, "dxc.exe");
    }
}
