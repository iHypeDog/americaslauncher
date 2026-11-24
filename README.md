Americas Launcher

Americas Launcher runs compatible game builds on Windows using the bundled [victory-gta6](https://github.com/fxceless/victory-gta6) runtime. Bring your own game files.

## Play

You need Windows 10 or 11 x64, a CPU with AVX2, and a Direct3D 12 GPU with Shader Model 6.6 support. World rendering also requires mesh shaders, and startup needs several GiB of available Windows commit memory.

1. Download and open [Americas Launcher.exe](./Americas%20Launcher.exe).
- in launcher UI
2. In **Build settings**, use **Choose build folder** and **Choose executable (.exe)** to select your game files.
3. Select **Play**. If the launcher reports a missing runtime, select **Install prerequisites** in **Build settings** and try again.

**Play** uses Direct3D 12 hardware rendering. The launcher extracts the bundled runtime as needed and pauses its background video during gameplay. World rendering and frame rates depend on the fork's compatibility.

## Prerequisites and updates

- **Install prerequisites** sets up the Visual C++ x64 runtime and the tools used to build updates. It uses WinGet for missing Windows packages and prepares Git, MSVC, the Windows SDK, the fork's pinned Rust toolchain, and its locked Cargo dependencies. Setup needs internet access and may request administrator permission. If prompted to restart Windows, reopen the launcher and run setup again.
- **Check prerequisites** verifies the runtime and build tools.
- **update version** fetches the latest revision of the fork and compiles it in Release mode.

Setup uses DXC 1.9 or later from the Windows SDK when available. Otherwise, it downloads and verifies Microsoft's DXC 1.9 x64 package and installs it for the current user.

## Saves and settings

The launcher remembers the build folder and executable in `%LOCALAPPDATA%\ViceCityLauncher\game-path.txt`. VIctory stores saves and in-game settings in `%LOCALAPPDATA%\victory\data` by default. The launcher and terminal share saves when they use the same VIctory data folder.

## Build the launcher

The launcher uses C# and WPF. Run the build script from the repository root on Windows x64 with .NET Framework 4.8.

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\Build.ps1
```

The script creates `Americas Launcher.exe` in the repository root.

## Bundled runtime

The included VIctory executable was built from revision [`90228a7d`](https://github.com/fxceless/victory-gta6/commit/90228a7dc66010281fdcfad10ac00c3a252be2f3).

```powershell
cargo build --locked --release -p victory --target x86_64-pc-windows-msvc
```

The corresponding [source archive](./runtime/victory-source.zip) and [GPL license](./runtime/LICENSE) ship with the launcher.
