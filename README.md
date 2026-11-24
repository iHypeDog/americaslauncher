Americas Launcher

Americas Launcher runs compatible game builds on Windows using the bundled [victory-gta6](https://github.com/fxceless/victory-gta6) runtime. Bring your own game files.

## Play

You need Windows 10 or 11 x64, a CPU with AVX2, and a Direct3D 12 GPU with Shader Model 6.6 support. World rendering also requires mesh shaders, and startup needs several GiB of available Windows commit memory.

1. Download and open [Americas Launcher.exe](./Americas%20Launcher.exe).
- in launcher UI
2. In **Build settings**, use **Choose build folder** and **Choose executable (.exe)** to select your game files.
3. Select **Play**. If the launcher reports a missing runtime, select **Install prerequisites** in **Build settings** and try again.

**Play** uses Direct3D 12 hardware rendering. The launcher extracts the bundled runtime as needed and pauses its background video during gameplay. World rendering and frame rates depend on the fork's compatibility.

## Saves and settings

The launcher remembers the build folder and executable in `%LOCALAPPDATA%\ViceCityLauncher\game-path.txt`. VIctory stores saves and in-game settings in `%LOCALAPPDATA%\victory\data` by default. The launcher and terminal share saves when they use the same VIctory data folder.
