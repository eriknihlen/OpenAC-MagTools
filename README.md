# OpenAC-MagTools

A port of [Mag-Tools](https://github.com/Mag-nus/Mag-Plugins) (by Mag-nus) to
[OpenAC](https://github.com/eriknihlen/OpenAC), built only against
`AcDream.Plugin.Abstractions`.

Licensed under the GNU Lesser General Public License v2.1, the same license as
the original Mag-Plugins. Original copyright remains with Mag-nus and the
Mag-Plugins contributors. See [LICENSE.md](LICENSE.md).

## Build

The plugin compiles against an OpenAC checkout. As of slice P2 it needs the
extended plugin contract added on OpenAC branch `magtools-api-a4` (chat
`Received`/`RegisterFilter`/`PostMessage`, `IEvents.LoginComplete`/`Logoff`,
`ISpellCatalog.All`/`TryFindByName`, `ICharacterInfo.ServerPopulation`), which
has not merged to OpenAC `main` yet. Until it does, `Directory.Build.props`
points `OpenAcRoot` at a frozen detached worktree snapshot of that branch's
head; override it with `-p:OpenAcRoot=<path>` if yours lives elsewhere (and
once the API branch merges, point the default back at `main`).

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
_Shipped-feature rows are filled in starting with slice P8; see
[docs/2026-09-16-port-design.md §6](docs/2026-09-16-port-design.md) for the
slice plan._

### Not applicable (Decal / Win32 only)

The original drove the retail client through Decal hooks and synthetic Win32
input against its own window. Nothing in OpenAC plays that role, so these
have no equivalent to port — not a gap, a different architecture. Full detail
and reasoning: [docs/2026-09-16-port-design.md §7](docs/2026-09-16-port-design.md).

| Mag-Tools feature | Why not applicable | OpenAC-native equivalent |
|---|---|---|
| Remove window frame | Win32 style hack against the retail window | client Options → display/fullscreen |
| Window position (set/del) | `MoveWindow` on the retail HWND | client window placement persistence |
| No-focus FPS / Max FPS | `Thread.Sleep` inside Decal's render hook | client frame limiter option (file an OpenAC issue if absent) |
| Maximize/Minimize chat, maximize on login | blind pixel clicks on the retail chat glyph | chat window is a native retained window |
| `/mt send *`, `/mt click *`, `/mt jump*`, `/mt movement`, `/mt quit`, `/mt client minimize`, `/mt get xy` | synthetic `PostMessage` input | `/mt face`, movement via the navigation automation; jump has no automation surface (recorded) |
| VCS/VHS/VHUD connectors, Decal proxy | Virindi/Decal presence probes | plugin command bus, markup panels |
| Tinkering "click yes" by pixel | dialog-button pixel offsets | E-CONFIRM answers the dialog on the wire |

### Dropped settings

Five settings from the original's Options page controlled exactly the Win32
behaviours above and have no OpenAC-side toggle to bind to, so they are not
ported: `RemoveWindowFrame`, `WindowPositions`, `NoFocusFPS`, `MaxFPS`,
`MaximizeChatOnLogin`. The original's Misc → Client tab (the tab that hosted
these window/FPS controls) is dropped in full for the same reason.
