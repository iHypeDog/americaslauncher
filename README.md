# Americas Launcher

Open `Americas Launcher.exe` on Windows x64. To rebuild it locally with .NET Framework 4.8, run `powershell.exe -ExecutionPolicy Bypass -File .\Build.ps1`.

Open **Build settings** and select **Install prerequisites**. The launcher extracts the bundled [victory-gta6](https://github.com/fxceless/victory-gta6) runtime, installs missing Windows build tools through WinGet, and downloads the fork's pinned Rust toolchain and locked Cargo dependencies. Setup needs an internet connection and may prompt for administrator access. If an installer requests a restart, reopen the launcher and run setup again. Use **Check prerequisites** to verify the runtime and build tools, or **update version** to compile the latest revision of the fork in Release mode.

Setup uses DXC 1.9 or later from the Windows SDK when available. Otherwise it downloads and verifies Microsoft's DXC 1.9 x64 package and installs it for the current user.

Select the build folder and executable before playing. The launcher remembers both paths for the current Windows user. VIctory keeps game saves and in-game settings in its usual data folder (`%LOCALAPPDATA%\victory\data` by default). Launches from the launcher and a terminal share saves when they use the same VIctory data folder.

Play uses Direct3D 12 hardware rendering and pauses the launcher's background video until the game exits. World rendering and frame rate depend on the fork's current compatibility.

The bundled runtime was built from revision `90228a7dc66010281fdcfad10ac00c3a252be2f3` with `cargo build --locked --release -p victory --target x86_64-pc-windows-msvc`. Its corresponding source and GPL license are in `runtime/`.
