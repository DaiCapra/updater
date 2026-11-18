# Launcher

[![Tests](https://github.com/DaiCapra/updater/actions/workflows/tests.yml/badge.svg?branch=master)](https://github.com/DaiCapra/updater/actions/workflows/tests.yml)

A reusable Windows launcher shell for games and other desktop apps. It requires .NET 10 and WPF.

## Configure a launcher

Put a `launcher.json` next to `Launcher.exe`, or use a separate profile for each app with `Launcher.exe --profile "C:\Apps\Example\launcher.json"`.

```json
{
  "displayName": "Example Game",
  "backgroundImage": "art/launcher.jpg",
  "updateFeedUrl": "https://downloads.example.com/example-game/latest.json",
  "installedVersion": "1.2.0"
}
```

The background is optional and can use a path relative to the profile. Supply both update fields to show the update controls. The installed app or its installer supplies the installed version and updates it whenever a release is installed.

## Publish an update

Host a JSON file at a direct HTTPS URL. A repository can publish it as a static file or release asset. Keep the feed URL stable across releases.

```json
{
  "version": "1.3.0",
  "downloadUrl": "https://downloads.example.com/example-game/ExampleGame-1.3.0.exe"
}
```

Use two to four numeric version components. When a newer release is available, the launcher offers to open its HTTPS download page or asset in the default browser. The user runs the installer separately.

On Windows, build and test with `dotnet test Launcher.slnx`. Run the shell with `dotnet run --project src/Launcher -- --profile path/to/launcher.json`.
