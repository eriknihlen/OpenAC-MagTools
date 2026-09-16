# OpenAC-MagTools

A port of [Mag-Tools](https://github.com/Mag-nus/Mag-Plugins) (by Mag-nus) to
[OpenAC](https://github.com/eriknihlen/OpenAC), built only against
`AcDream.Plugin.Abstractions`.

Licensed under the GNU Lesser General Public License v2.1, the same license as
the original Mag-Plugins. Original copyright remains with Mag-nus and the
Mag-Plugins contributors. See [LICENSE.md](LICENSE.md).

## Build

The plugin compiles against an OpenAC checkout. As of slice P2 it needs the
extended plugin contract added on OpenAC branch `claude/magtools-plugin-api`
(chat `Received`/`RegisterFilter`/`PostMessage`, `IEvents.LoginComplete`/
`Logoff`, `ISpellCatalog.All`/`TryFindByName`, `ICharacterInfo.ServerPopulation`,
and by P7 also E-TRADE/E-VENDOR/E-SESSION/E-HOTKEYS), which has not merged to
OpenAC `main` yet. Until it does,
`Directory.Build.props` points `OpenAcRoot` at a frozen detached worktree
snapshot of that branch's head -- currently `magtools-api-final` (review-closed
head `669832d` on `claude/magtools-plugin-api`; the branch is
expected to pick up one more commit from the A6 fix round, so treat the
exact SHA as a moving target until it settles. The earlier `magtools-api-a4`/
`magtools-api-a5` snapshots this pointed at are retired and no longer
checked out). Override it with `-p:OpenAcRoot=<path>` if yours lives
elsewhere (and once the API branch merges, point the default back at
`main`).

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

Slices P1–P8 are in — the full user-facing feature set described in the port
design doc is ported. See
[docs/2026-09-16-port-design.md](docs/2026-09-16-port-design.md) for the slice
plan, [docs/deviations.md](docs/deviations.md) for every place this port
knowingly behaves differently from the original, and
[docs/live-results.md](docs/live-results.md) for live verification against a
real ACE server (the connected user-gate rounds are still in progress; see
that file for which rows are proven live vs. code-complete).

## Parity checklist

One row per user-facing Mag-Tools feature from the port design's feature
inventory. **Shipped** means the code is in this repo (build/test evidence in
[docs/live-results.md](docs/live-results.md) tells you whether it is also
live-gated yet); **Not applicable** means the original feature was a Decal/
Win32 hack, or read a piece of client internals OpenAC's plugin contract does
not expose, with no OpenAC equivalent to port to — see the Notes column and
the two tables below for the reason and the native alternative, if any;
**Pending (host gap)** means the feature IS applicable but the current host
contract rejects or has no route for the call it needs — see the Notes
column and `docs/deviations.md` for the specific gap and what unblocks it.
A Pending row is never claimed Shipped.

| Feature | Status | Notes |
|---|---|---|
| `/mt` console (native subset + `opt` family) | Shipped | A handful of sub-commands are individually not applicable — see [Not-applicable `/mt` sub-commands](#not-applicable-mt-sub-commands) below |
| Chat filters (32) | Shipped | `src/OpenAC.MagTools/Chat/ChatFilter.cs` |
| Item info on ident (user + container path) | Shipped | Buffed-value model, weapon/armor appraisal profile segments (OpenAC slice A4) |
| VTank loot-rule bridge | Shipped | Maps to the registered `IPluginLootClassifierRegistry` (MossTank), not VTank/VTClassic — see `docs/deviations.md` |
| Auto buy/sell at vendors | Shipped | Buy and sell are two separate commits, not one combined Item Tool op — `docs/deviations.md` |
| Auto add to trade / mule loading | Shipped | |
| Auto trade accept | Shipped | |
| Auto looting (chests, corpses, salvage) | Shipped | |
| Inventory packer / auto-stack | Shipped | hotkey default Ctrl+P |
| Idle inventory automation | Shipped | |
| Tinkering auto-confirm | Shipped | answers on the wire (E-CONFIRM), not a pixel click |
| Tinkering tab: salvage-ID scan | Shipped | `TinkeringToolsHost` Start/Stop; requests ids for unidentified salvage bags matching the chosen material, one per second, until combat mode leaves Peace or nothing is left -- a salvage-ID helper, not tinkering itself, matching the original |
| Mana management / auto recharge | Shipped | |
| One-touch heal | Shipped | hotkey, unbound by default |
| Open main pack on login | Shipped | `Misc/OpenMainPackOnLogin`, default true; opens via `Ui.ShowClientWindow(PluginClientWindow.Inventory)` (OpenAC slice A6) rather than the original's `Items.Use(self)` -- see `docs/deviations.md` |
| Maximize chat on login | Not applicable | chat window is a native retained window; see Maximize/Minimize Chat below |
| Log out on death | Shipped | `Misc/LogOutOnDeath`, default false |
| Remove window frame | Not applicable | see the not-applicable table |
| Window position (set/del) | Not applicable | see the not-applicable table |
| On-Login / On-Login-Complete / Periodic commands | Shipped | collapsed onto the host's one login edge — `docs/deviations.md`; Character and Server tool tabs |
| Client FPS throttling (No-Focus FPS / Max FPS) | Not applicable | see the not-applicable table |
| Chat window size control (maximize/minimize) | Not applicable | see the not-applicable table |
| Inventory export to clipboard | Shipped | Clipboard Worn Equipment / Clipboard Inventory Info |
| Tools -> Inventory regex search | Shipped | `InventoryToolsPageViewModel`; re-filters every owned item's formatted info line against the typed pattern on each keystroke (invalid pattern -> empty results, never throws); clicking a result opens its container (if not the main pack) and shows its full info line |
| Inventory logger | Shipped | |
| Chat logger (2 groups + file) | Shipped | |
| HUD (14-row status bar) | Shipped | 13 of 14 rows; `ID Queue` is not applicable — see below |
| Combat tracker | Shipped | current-session + persistent, DPS snapshots, export/import |
| Corpse tracker | Shipped | |
| Player tracker | Shipped | |
| Equipment ("mana") tracker | Shipped | |
| Inventory (consumables) tracker | Shipped | |
| Profit/loss tracker | Shipped | |
| Main window: Trackers tab (Mana/Combat/Corpse/Player/Inv. Items) | Shipped | |
| Main window: Loggers tab (Chat Group 1/2/Options) | Shipped | |
| Main window: Tools tab (Inventory/Tinkering/Character/Server) | Shipped | Character/Server command lists live as of P8 |
| Main window: Misc tab — Options | Shipped | |
| Main window: Misc tab — Filters | Shipped | |
| Main window: Misc tab — Client | Not applicable | hosted exactly the window-frame/position/FPS controls above — see Dropped settings/tab below |
| Main window: Misc tab — About | Shipped | |
| Settings model (single XML file, account/server/character scoping) | Shipped | 5 settings and the Client tab dropped — see below |
| Hotkey: Pack Inventory | Shipped | default Ctrl+P |
| Hotkey: One Touch Heal | Shipped | unbound by default |
| Hotkey: Maximize Chat | Not applicable | see Maximize/Minimize Chat below |
| Hotkey: Minimize Chat | Not applicable | see Maximize/Minimize Chat below |

**Counts:** 37 shipped, 8 not applicable (45 checklist rows total; the
not-applicable rows point at the detail tables below, which break each one
down further by individual command/hotkey).

### Not applicable (Decal / Win32 only, or no host equivalent)

The original drove the retail client through Decal hooks and synthetic Win32
input against its own window, or read Decal/VirindiHUDs engine internals
OpenAC's plugin contract has no reason to expose. Nothing in OpenAC plays
either role, so these have no equivalent to port — not a gap, a different
architecture. Full detail and reasoning:
[docs/2026-09-16-port-design.md §7](docs/2026-09-16-port-design.md).

| Mag-Tools feature | Why not applicable | OpenAC-native equivalent |
|---|---|---|
| Remove window frame | Win32 style hack against the retail window | client Options → display/fullscreen |
| Window position (set/del) | `MoveWindow` on the retail HWND | client window placement persistence |
| No-focus FPS / Max FPS | `Thread.Sleep` inside Decal's render hook | client frame limiter option (file an OpenAC issue if absent) |
| Maximize/Minimize chat, maximize chat on login, the Maximize Chat/Minimize Chat hotkeys | blind pixel clicks on the retail chat glyph at a hard-coded UI-element offset; the hotkeys just fired the same click | chat window is a native retained window with its own resize/maximize controls — nothing to automate |
| `ID Queue` HUD row | `CoreManager.Current.IDQueue.ActionCount` — a Decal engine-internal identify-request queue depth | no host equivalent; the row is still present (empty) so the HUD's row order matches the original — `src/OpenAC.MagTools/HudUpdater.cs` |
| VCS/VHS/VHUD connectors, Decal proxy | Virindi/Decal presence probes | plugin command bus, markup panels |
| Tinkering "click yes" by pixel | dialog-button pixel offsets | E-CONFIRM answers the dialog on the wire |

### Not-applicable `/mt` sub-commands

| Sub-command | Why not applicable | OpenAC-native equivalent |
|---|---|---|
| `/mt send *`, `/mt click *`, `/mt jump*`, `/mt movement`, `/mt quit`, `/mt exit`, `/mt client minimize`, `/mt get xy` | synthetic `PostMessage`/Win32 input against the retail window | `/mt face`, movement via the navigation automation; jump has no automation surface (recorded, not a gap in the port itself) |

### Dropped settings / Client tab

Five settings from the original's Options page controlled exactly the Win32
behaviours above and have no OpenAC-side toggle to bind to, so they are not
ported: `RemoveWindowFrame`, `WindowPositions`, `NoFocusFPS`, `MaxFPS`,
`MaximizeChatOnLogin`. The original's Misc → Client tab (the tab that hosted
these window/FPS controls) is dropped in full for the same reason.
