# OpenAC-MagTools

A port of [Mag-Tools](https://github.com/Mag-nus/Mag-Plugins) (by Mag-nus) to
[OpenAC](https://github.com/eriknihlen/OpenAC), built only against
`AcDream.Plugin.Abstractions`.

Licensed under the GNU Lesser General Public License v2.1, the same license as
the original Mag-Plugins. Original copyright remains with Mag-nus and the
Mag-Plugins contributors. See [LICENSE.md](LICENSE.md).

## Build

The plugin compiles against an OpenAC checkout. `Directory.Build.props` points
`OpenAcRoot` at `../acdream/OpenAC/` by default; override it if yours lives
elsewhere.

```powershell
dotnet build -c Release
dotnet test -c Release

# against a checkout somewhere else
dotnet build -c Release -p:OpenAcRoot=D:\src\OpenAC\
```

The plugin has exactly one project reference (the abstractions) and no package
references. The abstractions assembly is referenced with `Private=false` and
`ExcludeAssets=runtime` because the host supplies it; shipping a second copy
would give every interface two incompatible identities.

## Deploy

```powershell
./tools/deploy.ps1                        # build Release and install
./tools/deploy.ps1 -NoBuild               # install what is already built
./tools/deploy.ps1 -PluginsRoot D:\plugins
```

It installs the assembly, its `.deps.json`, both markup files and `plugin.json`
into `<plugins root>/OpenAC.MagTools/`. The plugins root is the client's data
directory plus `plugins`:

| Platform | Default |
|---|---|
| Windows | `%LOCALAPPDATA%\acdream\plugins` |
| macOS | `~/Library/Application Support/acdream/plugins` |
| Linux | `$XDG_DATA_HOME/acdream/plugins` (default `~/.local/share/acdream/plugins`) |

`ACDREAM_DATA_DIR` moves the data directory, and the script follows it.

Settings live under the client's *config* directory, scoped to the plugin id, as
`Mag-Tools.xml`. An existing Mag-Tools settings file copies in unchanged.

## Status

Slice P1 (skeleton, settings, `/mt` console, window shells) is in. See
[docs/2026-09-16-port-design.md](docs/2026-09-16-port-design.md) for the slice
plan and [docs/live-results.md](docs/live-results.md) for live verification.

## Parity checklist

<!-- One row per Mag-Tools feature, filled in as each slice lands. -->
_To be filled in as the slices land._
