# Unreal Plugins Manager (GUI)

**Alpha version** - Application for managing Unreal Engine and project plugins with installation from ZIP archives, plugin version reading, and project installation.

Avalonia-based desktop app to manage Unreal Engine projects and plugins, including installing plugins from archives and keeping a local plugin library with versioned entries.

## Author

- **Telegram**: https://t.me/yenchzk_archive
- **GitHub**: https://github.com/yenichw

## Features

### Project management

- Detect Unreal projects from common locations
- Manually add projects and persist them across restarts

### Plugin library (versioned)

- Import plugin archives into a local library
- Keep multiple versions per plugin
- Sort library entries by version
- “Add to Project” installs the selected library version into any saved project

### Engines

- Detect Unreal Engine installations
- Read engine version from `Build.version` / registry association

## Requirements

- **Windows 10/11** (primary target)
- **.NET 8 SDK**

## Build & Run

```bash
dotnet restore
dotnet build
dotnet run
```

## Usage

1. Click **Refresh** in **Engines** and **Projects**.
2. Use **Add Project** to add a `.uproject` (it will be persisted).
3. Use **Install Plugin** to install an archive directly to the selected project.
4. Use the **Library** tab to **Import** plugin archives, then select a version and **Add to Project**.

## Logs

Logs are written under the `logs/` directory (daily rolling files).

## License

This project is provided as-is. Make sure you comply with Unreal Engine and plugin licensing terms when using or redistributing plugins.
