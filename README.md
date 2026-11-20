# DonutButBetter Installer

A small Windows app that installs and updates the DonutButBetter modpack for the official Minecraft Launcher.

It is **not** a launcher. You still start the game from the Minecraft Launcher, and it never asks for or touches your Microsoft account.

## What it does

Every time you open it:

1. Downloads the pack list (`manifest.json`) from the DonutButBetter website, or from this organization's GitHub releases if the website is down.
2. Installs Fabric Loader for the pack's Minecraft version and adds a **DonutButBetter** profile to the Minecraft Launcher.
3. Downloads the pack's mods into its own folder, `%APPDATA%\.minecraft\donutbutbetter`. Every file is checked against the SHA-512 hash in the manifest before it's used. Third-party mods come straight from Modrinth.
4. Removes mods that an older pack version installed but the new one doesn't use. Mods you added yourself, plus your options, saves and screenshots, are left alone.

Your normal Minecraft profiles and mods are not changed. A log is written to `donutbutbetter\installer.log`.

If you're offline, it keeps the version you already have.

## What it changes on your computer

- `%APPDATA%\.minecraft\donutbutbetter\` (the pack's own game folder)
- `%APPDATA%\.minecraft\versions\fabric-loader-<version>-<minecraft>\` (Fabric, same as the official Fabric installer)
- `%APPDATA%\.minecraft\launcher_profiles.json` (adds or updates the DonutButBetter profile)

Nothing else: no registry changes, no background service, no admin rights.

## Building it yourself

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet publish DonutInstaller.csproj -c Release -o out
```

This makes `out\DonutButBetter-Installer.exe`, a single self-contained exe (no .NET needed to run it).

Releases are built the same way by GitHub Actions (`.github/workflows/release.yml`) straight from the tagged commit, and each one comes with a `.sha256` file to check the download against.

## Command-line options

```
--minecraft-dir <folder>   install somewhere other than %APPDATA%\.minecraft
--manifest <url or file>   use a different manifest
--no-launcher-check        don't require or wait for the Minecraft Launcher
--silent                   no window; exit code 0 on success
```

## License

MIT, see [LICENSE](LICENSE).
