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
snapshot of that branch's head -- currently `magtools-api-a9` (review-closed
head `9c26778` on `claude/magtools-plugin-api`, which adds the A9 headless
fixes: real character identity, `Objects`, storage and trade wiring on the
headless host; the A7 fixes:
character identity at login, walk-to-use for world objects, a truthful
clipboard write; and the A8 fixes: the plugin's use of a world container
or vendor arms the container request like a click, and a stalled approach
gives up instead of holding the use gate. The earlier `a4`/`a5`/`a6`/`final`/`a7`/`a8`
snapshots this pointed at are retired). Override it with `-p:OpenAcRoot=<path>` if yours lives
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

Of the parity checklist's 48 rows, 40 are Shipped and 8 are Not applicable
(P13 moved `/mt client minimize`, `/mt quit` and `/mt exit` from the
not-applicable `/mt` sub-command appendix into their own Shipped rows above,
onto OpenAC's new `IHostWindow` host-window surface -- see
`docs/deviations.md`; the top-level Not-applicable count is unchanged because
those three were never counted there, only noted under the already-Shipped
`/mt` console row). Of
the 40 shipped rows, the live gate (rounds P1–P8, Round 3, Round 4, the
closing Round 5, the P12 fix round, and the P13 host-window round) has
reached PASS on 27, PARTIAL on 8,
and PENDING on 5 (see
the checklist's `Live` column and
[docs/live-results.md](docs/live-results.md) for the exact round and evidence
per row). Every row has now been exercised live at least once. Almost every
PARTIAL/PENDING row shares one blocker class: something the local ACE server
or this admin character will not produce — a miss, a hit taken, low mana, a
monster attack, a death, NPC/vendor chatter (Round 5 ruled out cloaking as
the reason nothing fights back), a vendor transaction ACE will accept, an
openable chest, a complete salvage bag, an `Intricate Carving Tool`, a
healing kit. The remaining non-server blocker is the test harness itself:
after a teleport the client sits on a loading screen for longer than the UI
probe's world gate waits, which is what keeps the trade-accept row open.

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

| Feature | Status | Live | Notes |
|---|---|---|---|
| `/mt` console (native subset + `opt` family) | Shipped | PASS (r1) | A handful of sub-commands are individually not applicable — see [Not-applicable `/mt` sub-commands](#not-applicable-mt-sub-commands) below |
| `/mt client minimize` | Shipped | PASS (r6) — confirmed via `user32.dll` `IsIconic` from a second process; the window actually iconified, not just a reported `Done` | drives `IHostWindow.Minimize()` (OpenAC A10); see `docs/deviations.md` for the Wayland confirmation caveat |
| `/mt quit` | Shipped | PASS (r6) — graceful logout confirmed, client exited on its own with no `close-client` probe | drives `IHostWindow.RequestClose()` (OpenAC A10) — graceful logout, then teardown, then process exit |
| `/mt exit` | Shipped | PASS (r6) — same as `/mt quit` | alias of `/mt quit`, same `IHostWindow.RequestClose()` route |
| Chat filters (32) | Shipped | PARTIAL (r1/r3) — 2 of 32 rules provoked (MonsterDeaths, TradeBuffBotSpam); the other 30 need misses/hits/fizzles/resists/comps/an NPC-vendor this character/server never produces. Round 5 ruled out cloaking as the cause: with `@cloak off` acknowledged, eight hostile monsters still never attacked | `src/OpenAC.MagTools/Chat/ChatFilter.cs` |
| Item info on ident (user + container path) | Shipped | PASS (r1) — user path only; container path not exercised | Buffed-value model, weapon/armor appraisal profile segments (OpenAC slice A4) |
| VTank loot-rule bridge | Shipped | PASS (r4) — `moss-tank` classifier registers and `LootRuleProcessor` reaches its profile during real looting | Maps to the registered `IPluginLootClassifierRegistry` (MossTank), not VTank/VTClassic — see `docs/deviations.md` |
| Auto buy/sell at vendors | Shipped | PARTIAL (r5) — vendor opens, AutoBuySell picks from its profile and drives the commands, and a rejected transaction now says so (`Vendor transaction failed.`); ACE rejects both buy and sell, so a completed transaction is still unproven | Buy and sell are two separate commits, not one combined Item Tool op — `docs/deviations.md` |
| Auto add to trade / mule loading | Shipped | PASS (r4) — AutoTradeAdd staged a partner's pack unattended; mule loading not separately exercised | |
| Auto trade accept | Shipped | PENDING (r5) — the A9 host wires `Objects` into the headless surface, so round 4's host gap is closed; five attempts still could not stage an item because the post-teleport world load outruns the probe's world gate | |
| Auto looting (chests, corpses, salvage) | Shipped | PARTIAL (r5) — plugin-opened corpse looted again (`+(KeepAll)` lines, tracker row `Opened="True"`); a chest in reach opens by neither the plugin nor the client's own path, so it is the chest, not the port; salvage not exercised | |
| Inventory packer / auto-stack | Shipped | PARTIAL (r5) — `Started`/`Completed` proven again; the main-pack grid is unchanged before and after, and a fresh loose taper could not be minted to settle whether there was anything to move | hotkey default Ctrl+P |
| Idle inventory automation | Shipped | PARTIAL (r1) — Aetheria revealer + key ringer fired unattended; heart carver / shattered-key fixer / key deringer blocked (their item is not creatable on this ACE build) | |
| Tinkering auto-confirm | Shipped | PENDING (r5) — the plugin's Apply reaches the server, but ACE's `@ci` salvage is incomplete (`The material is not complete!`) so the crafting-percent confirmation can never be raised | answers on the wire (E-CONFIRM), not a pixel click |
| Tinkering tab: salvage-ID scan | Shipped | PARTIAL (r1) — tab renders as authored; the scan loop itself was not exercised (needs salvage bags) | `TinkeringToolsHost` Start/Stop; requests ids for unidentified salvage bags matching the chosen material, one per second, until combat mode leaves Peace or nothing is left -- a salvage-ID helper, not tinkering itself, matching the original |
| Mana management / auto recharge | Shipped | PENDING (r3) — trigger (`Your <item> is low on Mana.`) never fires because this character's equipped items don't burn mana | |
| One-touch heal | Shipped | PASS (r3) — hotkey bound and fired via the food fallback; the healing-kit branch is PENDING (no healing-kit weenie resolves on this build) | hotkey, unbound by default |
| Open main pack on login | Shipped | PASS (r1) — both the enabled-default and disabled cases observed | `Misc/OpenMainPackOnLogin`, default true; opens via `Ui.ShowClientWindow(PluginClientWindow.Inventory)` (OpenAC slice A6) rather than the original's `Items.Use(self)` -- see `docs/deviations.md` |
| Maximize chat on login | Not applicable | N/A | chat window is a native retained window; see Maximize/Minimize Chat below |
| Log out on death | Shipped | PENDING (r5) — no real death was ever provoked; monsters on this server never land a hit on this character, even uncloaked | `Misc/LogOutOnDeath`, default false |
| Remove window frame | Not applicable | N/A | see the not-applicable table |
| Window position (set/del) | Not applicable | N/A | see the not-applicable table |
| On-Login / On-Login-Complete / Periodic commands | Shipped | PASS (r3) — server-scoped list dispatches at `LoginComplete`, character-scoped list dispatches at the plugin's session-ready edge, each observed dispatching in order with its own dummy slot | server-scoped lists run at `LoginComplete`, character-scoped lists run at the plugin's session-ready edge (once the character name is known), each with its own dummy slot; cross-scope ordering between the two edges is not preserved — `docs/deviations.md`; Character and Server tool tabs |
| Client FPS throttling (No-Focus FPS / Max FPS) | Not applicable | N/A | see the not-applicable table |
| Chat window size control (maximize/minimize) | Not applicable | N/A | see the not-applicable table |
| Inventory export to clipboard | Shipped | PASS (r4) — both Worn Equipment and Inventory Info exports non-empty and readable back from the clipboard | Clipboard Worn Equipment / Clipboard Inventory Info |
| Tools -> Inventory regex search | Shipped | PARTIAL (r1) — page renders with field + lists; live keystroke-filtering behavior not separately exercised | `InventoryToolsPageViewModel`; re-filters every owned item's formatted info line against the typed pattern on each keystroke (invalid pattern -> empty results, never throws); clicking a result opens its container (if not the main pack) and shows its full info line |
| Inventory logger | Shipped | PASS (P12) — real id data is captured and persisted (15 records with int/double/bool/string values), a second session correctly skips the request pass, and the P12 fix closed defect 15 (previous-record match now keyed on Id alone): three consecutive sessions held `HasIdData=true` steady with no drop | |
| Chat logger (2 groups + file) | Shipped | PASS (r3) — file written, non-empty, correctly character-prefixed | |
| HUD (14-row status bar) | Shipped | PASS (r1) — all 14 rows render; the P5 fix corrected the fabricated login-time rates | 13 of 14 rows; `ID Queue` is not applicable — see below |
| Combat tracker | Shipped | PARTIAL (r5) — monster rows, KB's and the attack counter populate live; `Dmg Rcvd`/`Dmg Givn` still unprovoked, with cloaking now ruled out as the reason nothing fights back | current-session + persistent, DPS snapshots, export/import |
| Corpse tracker | Shipped | PASS (r1) | |
| Player tracker | Shipped | PASS (r3) | |
| Equipment ("mana") tracker | Shipped | PASS (r1) | |
| Inventory (consumables) tracker | Shipped | PASS (r1) — after the P5 rate fix | |
| Profit/loss tracker | Shipped | PASS (r1) — after the P5 rate fix | |
| Main window: Trackers tab (Mana/Combat/Corpse/Player/Inv. Items) | Shipped | PASS (r1) | |
| Main window: Loggers tab (Chat Group 1/2/Options) | Shipped | PASS (r1) | |
| Main window: Tools tab (Inventory/Tinkering/Character/Server) | Shipped | PASS (r3) — render plus Character/Server Move/Delete/Add all proven live | Character/Server command lists live as of P8 |
| Main window: Misc tab — Options | Shipped | PASS (r1) | |
| Main window: Misc tab — Filters | Shipped | PASS (r1) | |
| Main window: Misc tab — Client | Not applicable | N/A | hosted exactly the window-frame/position/FPS controls above — see Dropped settings/tab below |
| Main window: Misc tab — About | Shipped | PASS (r1) | |
| Settings model (single XML file, account/server/character scoping) | Shipped | PASS (r1) — `Mag-Tools.xml` persistence proven across opt remember, chat logger and command-list rows | 5 settings and the Client tab dropped — see below |
| Hotkey: Pack Inventory | Shipped | PENDING (P12) — two Ctrl+P deliveries (the second targeted at the client's process id and confirmed sent) produced no packer run, while `/mt autopack` in the same build does; P12 confirmed live that the default chord resolves and binds (`IsBound=True`) and every plugin-side code path (collision checks, dispatch, `InventoryPacker.Start()`) reads correctly, narrowing the defect to raw key delivery in the host, unverifiable without a live keyboard | default Ctrl+P |
| Hotkey: One Touch Heal | Shipped | PASS (r3) | unbound by default |
| Hotkey: Maximize Chat | Not applicable | N/A | see Maximize/Minimize Chat below |
| Hotkey: Minimize Chat | Not applicable | N/A | see Maximize/Minimize Chat below |

**Counts:** 40 shipped, 8 not applicable (48 checklist rows total; the
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
| `/mt send *`, `/mt click *`, `/mt jump*`, `/mt movement`, `/mt get xy` | synthetic `PostMessage`/Win32 input against the retail window | `/mt face`, movement via the navigation automation; jump has no automation surface (recorded, not a gap in the port itself) |

### Dropped settings / Client tab

Five settings from the original's Options page controlled exactly the Win32
behaviours above and have no OpenAC-side toggle to bind to, so they are not
ported: `RemoveWindowFrame`, `WindowPositions`, `NoFocusFPS`, `MaxFPS`,
`MaximizeChatOnLogin`. The original's Misc → Client tab (the tab that hosted
these window/FPS controls) is dropped in full for the same reason.
