# Americas Launcher

Open `Americas Launcher.exe` on Windows x64. To rebuild it locally with .NET Framework 4.8, run `powershell.exe -ExecutionPolicy Bypass -File .\Build.ps1`.

Open **Build settings** and select **Install prerequisites**. The launcher extracts the bundled VIctory runtime, installs missing Windows build tools through WinGet, and downloads VIctory's pinned Rust toolchain and locked Cargo dependencies. Setup needs an internet connection and may prompt for administrator access. If an installer requests a restart, reopen the launcher and run setup again. Use **Check prerequisites** to verify the runtime and build tools, or **update version** to compile the latest VIctory revision.

Setup uses DXC 1.9 or later from the Windows SDK when available. Otherwise it downloads and verifies Microsoft's DXC 1.9 x64 package and installs it for the current user.

Select the build folder and executable before playing. The launcher keeps those selections for the current session. Each game launch uses a fresh temporary save directory that the launcher removes when VIctory exits. Existing saves from older versions stay in their original directory.
