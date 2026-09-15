# Mag-Tools for OpenAC — port design and slice plan

Date: 2026-09-16. Owner: Erik. Planner: Fable. Implementers: Opus, one slice
at a time. Reviewers: a fresh Opus per slice.

Inputs (private repo `acdream`, `docs/research/mag-tools/`):

- `2026-09-15-mag-tools-feature-inventory.md` — every Mag-Tools feature, rule,
  format, setting and message, derived from source. **The parity oracle.**
- `2026-09-15-openac-plugin-api-inventory.md` — the OpenAC plugin contract as
  of 2026-09-15 and its gap list.

## 1. Goal and non-goals

Full user-facing feature parity with Mag-Tools (Mag-nus, LGPL 2.1) as an
OpenAC plugin that references only `AcDream.Plugin.Abstractions`. Where the
contract lacks a surface, OpenAC gains a general plugin API (not a Mag-Tools
hook) on a branch, with tests, reported before push.

Not goals: pixel-identical VirindiViewService layout (we use OpenAC's markup
and its retail look), Decal/Win32 hacks (recorded as not applicable, §7),
upstream bugs (Appendix B of the inventory is a do-not-reproduce list).

## 2. Repository layout (`github.com/eriknihlen/OpenAC-MagTools`, private)

```
LICENSE.md                      LGPL 2.1 verbatim from Mag-Plugins
README.md                       what it is, how to build/deploy, PARITY CHECKLIST
docs/2026-09-16-port-design.md  this file
docs/live-results.md            one written result per feature, live vs ACE
src/OpenAC.MagTools/            the plugin (net10.0)
  OpenAC.MagTools.csproj        ProjectReference to Abstractions via $(OpenAcRoot)
  plugin.json                   id openac.magtools, displayName Mag-Tools, kinds [Gameplay]
  MagToolsPlugin.cs             IAcDreamPlugin entry
  Views/magtools.xml            main window markup (+ magtools-hud.xml)
  Settings/ Chat/ ItemInfo/ Trackers/ Macros/ Loggers/ Inventory/ Commands/ Ui/
tests/OpenAC.MagTools.Tests/    xunit; ONE shared FakeHost; markup contract test
tools/deploy.ps1                copies build output + plugin.json to %LOCALAPPDATA%\acdream\plugins\OpenAC.MagTools
Directory.Build.props           OpenAcRoot default = ..\acdream\OpenAC (override -p:OpenAcRoot=)
```

Build rule: `<ProjectReference ... Private="false" ExcludeAssets="runtime">`
exactly as MossTank does; no other references; `TreatWarningsAsErrors`.
The plugin never uses `System.IO` paths of its own; all files go through
`IPluginStorage` (see E-STORAGE for the root-path disclosure).

Namespaces: `OpenAC.MagTools.*`. Plugin id `openac.magtools`; every chat line
the plugin prints keeps the original `<{Mag-Tools}>: ` prefix.

## 3. Architecture inside the plugin

One composition root (`MagToolsPlugin`) builds:

- `SettingsFile` + `Setting<T>` — the original XML schema (`Mag-Tools.xml`,
  root `<Mag-Tools>`, XPath-addressed elements, `_<account>_<server>/<char>`
  scoping, `Misc/WindowPositions` dropped). Stored under `IPluginStorage` key
  `Mag-Tools.xml` so a user can copy their existing file in. Writes are
  debounced to one save per 250 ms (fixes upstream bug 8) but semantics stay
  "write-through".
- `SessionContext` — the in-world edge detector. The contract has no login
  event today; after E-LIFECYCLE it subscribes `IEvents`. Everything that
  Mag-Tools did on `Login`/`LoginComplete`/`Logoff` hangs off this one owner
  so reconnects (which do not recycle plugins) behave like a fresh login.
- `ChatOutput` — `PostMessage` with the original colours (5 plugin text,
  14 item info) through the extended `IPluginChat`.
- Feature owners, one class per inventory section, each with `Start/Stop`,
  fed by a `TickScheduler` (100 ms think loops, N-second timers, the 10-minute
  persistence save) built on `IEvents.Tick`.
- `MainViewModel` — the single binding object for `magtools.xml`, with a child
  view model per tab page. Tabs follow the MossTank convention (tab buttons +
  visible-bound groups). Nested notebooks (Combat/Corpse/Player) become a
  second row of tab buttons inside the page.
- `HudViewModel` — a second small panel (`magtools-hud.xml`) that renders the
  14 status rows exactly as VirindiHUDs showed them (rows are text in a
  status window in the original too; no overlay API is needed).

Loot rules: Mag-Tools depended on VTank/VTClassic; the port depends on the
registered loot classifier (MossTank) through `IPluginLootClassifierRegistry`.
Live-profile calls map to `TryClassify`; named-file calls (`<Vendor>.utl`,
`<Trader>.utl`, `<Char>.AutoPack.utl`) map to the new
`TryClassifyWithProfile` (E-LOOT). No classifier registered → the original
degradation messages ("Is Virindi Tank running?" becomes "Is MossTank
loaded?").

## 4. OpenAC API extensions (general contracts, landed first)

Branch in OpenAC: `claude/magtools-plugin-api`. Each item ships with fake-host
defaults (so existing plugins compile), host implementations for graphical
and headless, unit tests at the matching layer, and a `docs/plugin-*.md`
update. Nothing here names Mag-Tools.

| Id | Contract change | Underlying seam (from the API inventory) |
|---|---|---|
| E-CHAT | `PluginChatMessage` gains `LogTextType`, `CombatKind`, `Received`; `IPluginChat.Received` push event; `IPluginChat.RegisterFilter(Func<PluginChatMessage,bool> suppress)` (a suppressed line never reaches the transcript or the file log); `PostMessage(string text, int logTextType)`; status-text lines flow through the same filter with a distinct kind | `RuntimeCommunicationState.cs:311-317`, `AppAutomationSurface.OnChat`, `RetailLogTextType` |
| E-LIFECYCLE | `IEvents.LoginComplete`, `IEvents.Logoff`, `IEvents.LocalPlayerDied(string deathMessage)`; `ICharacterInfo.ServerPopulation` | `RuntimeLifecycleDelta`, `VictimNotification`/`PlayerKilled`, server-name message |
| E-OBJECTS | `IEvents.ObjectChanged(PluginObjectChange(ObjectId, Kind))`, `Kind ∈ Created, Updated, IdentReceived, Moved, Released`, plus `ContainerOpened(uint)`/`ContainerClosed` | `RuntimeEntityChange`, `RuntimeInventoryChange`, appraisal completion, `OpenContainerObjectId` |
| E-CONFIRM | `IEvents.ConfirmationRequested(PluginConfirmation(ContextId, Type, Text))` and `IAutomationSurface.Dialogs.Answer(contextId, bool yes)` | the confirmation-panel game event (type 5 = crafting percent) |
| E-SESSION | `ILoginAutomation.Logout()` (the client's own graceful logout) | existing logout route |
| E-TRADE | `ITradeAutomation`: `IsOpen, PartnerObjectId, PartnerName, MyItems, PartnerItems, MyAccepted, PartnerAccepted`; `Add(itemId)`, `Accept()`, `Decline()`, `Reset()`, `End()`; events `Opened`, `Closed`, `PartnerAccepted` | `RuntimeTradeState` / `IRuntimeTradeView` |
| E-VENDOR | `IVendorAutomation`: `IsOpen, VendorObjectId, VendorName, Items(PluginVendorItem: TemplateId, Name, ObjectClass, UnitPrice, Properties)`, `AddToBuyList(templateId, count)`, `AddToSellList(itemId)`, `ClearBuyList()`, `ClearSellList()`, `BuyAll()`, `SellAll()`, `IsBusy`; events `Opened`, `Closed` | `VendorState`, `VendorShopProfile`, `VendorUiController` |
| E-LOOT | `IPluginLootClassifier` gains `bool NeedsIdentification(in context)` and `bool TryClassifyWithProfile(string profileName, in context, out classification)`; registry forwards | MossTank's loot engine (already loads `.utl`) |
| E-SPELLS | `ISpellCatalog.All` (whole table) and `TryFindByName(string, bool partial, out PluginSpellInfo)` | spell table already in host |
| E-STORAGE | `IPluginStorage.RootPath` (string?, null headless-sandboxed) | `FilePluginStorage` |
| E-CLIPBOARD | `IPluginHost.Clipboard.TrySetText(string)` | window clipboard |
| E-HOTKEYS | `IHotkeyRegistry.Register(id, displayName, PluginKeyChord default, Action)`; host binds through `InputDispatcher`, suppressed while the chat bar has focus unless Ctrl/Alt is held; user overrides persisted in `keybinds.json` under `plugin:<pluginId>:<hotkeyId>` | `InputDispatcher`, `KeyBindings` |

Grouped into three OpenAC slices (§6). Push and PR wait for the owner.

## 5. Feature → surface map

| Inventory § | Feature | Surfaces (existing + extension) |
|---|---|---|
| 1.1 | `/mt` console | `Commands.Register("mt")`, Items/Objects/Loot/Combat/Magic/Fellowship/Navigation automation, E-TRADE, E-VENDOR, E-SESSION, E-SPELLS; `send/click/jump/movement/quit/client minimize/get xy/fellow create-by-clicks` → §7 |
| 1.2 | Chat filters (32) | E-CHAT filter + `Objects.TryGet` for speaker class |
| 1.3–1.4 | Item info on ident, container scan, loot verdict | `Selection.Changed`, E-OBJECTS IdentReceived/ContainerOpened, `TryCaptureProperties`, `Objects.Identify`, E-LOOT, E-CLIPBOARD, `Spells.TryGet` |
| 1.5 | Auto buy/sell | E-VENDOR, E-LOOT profile `<Vendor>.utl` |
| 1.6–1.7 | Auto trade add / accept | E-TRADE, E-LOOT profile `<Trader>.utl`, whitelist regexes |
| 1.8 | Looter | `Loot.*`, E-OBJECTS ContainerOpened, `Items.IsBusy`, E-LOOT live |
| 1.9 | Inventory packer | `Items.MoveToContainer/Merge`, E-LOOT profile, E-HOTKEYS Ctrl+P, chat trigger |
| 1.10–1.11 | Idle actions, tinkering auto-yes | `Items.Apply`, `Character.Skills`, `Combat.Snapshot.Mode`, E-CONFIRM |
| 1.12–1.13 | Mana recharge, one-touch heal | `Items.Apply`, `ItemCurrentMana`, `HealKitModifier`, vitals, E-HOTKEYS |
| 1.14 | Login/periodic commands, open pack, logout on death | E-LIFECYCLE, `Chat.Submit`, `Items.Use(self)`, E-SESSION |
| 1.17–1.18 | Inventory clipboard export, inventory logger | `Items.CaptureOwnedItems`, `TryCaptureProperties`, E-CLIPBOARD, storage |
| 1.19 | Chat logger | E-CHAT, storage (`<Server>/<Char>.ChatLogger.txt`, 10-min flush, roll at 100 MB, import last 1 MiB) |
| 2.1 | Combat tracker | E-CHAT (kind Combat + text), storage XML |
| 2.2–2.3 | Corpse / player trackers | E-OBJECTS, `PluginWorldObject.Position`, `Selection.Select` |
| 2.4 | Equipment/mana tracker | `Equipment.CaptureOwnedEquipment`, `TryCaptureProperties` (mana, rate, Retained), `Character.ActiveEnchantments`, `Spells.TryGet` |
| 2.5–2.6 | Consumables + profit/loss | `Items.CaptureOwnedItems` (Value, StackSize, IconId) |
| 4.x | Main window, HUD | `Ui.AddPanel`, markup |
| 5 | Settings | storage + original XML schema |

Timing constants, message strings, regex tables, formats and defaults are
taken verbatim from the inventory. Coordinates: `Util.GetCoords` formula as
written there.

## 6. Slices

Every slice: implement → build + tests green → fresh-Opus review (two lenses:
architecture/contract hygiene, and parity against the inventory section) →
fix findings → review-closed. Plugin slices also get their live-results rows.

| # | Repo | Scope | Depends on |
|---|---|---|---|
| A1 | OpenAC | E-CHAT, E-LIFECYCLE, E-SPELLS, E-STORAGE, E-CLIPBOARD | — |
| A2 | OpenAC | E-OBJECTS, E-CONFIRM, E-SESSION, E-LOOT | — |
| A3 | OpenAC | E-TRADE, E-VENDOR, E-HOTKEYS | — |
| P1 | plugin | skeleton, settings file, `/mt` console (native subset + `opt` family), chat output, main window shell with full tab tree + Options/Filters/About pages, HUD panel shell, shared FakeHost, markup contract test, deploy script | A1 |
| P2 | plugin | chat classification helpers, 32 chat filters, chat logger (2 groups, file, import) | A1 |
| P3 | plugin | item-info string + buffed-value model, ident detection (user + container), loot verdict, inventory clipboard export, inventory search tab | A1, A2 |
| P4 | plugin | combat tracker (parsers, aggregation, DPS snapshots, XML import/export, 10-min save, export-on-logoff) + Combat tab pages | A1 |
| P5 | plugin | equipment/mana tracker + Mana tab + auto recharge; consumables + profit/loss trackers + Inv. Items tab; HUD rows | A1, A2 |
| P6 | plugin | corpse + player trackers + tabs; inventory logger; tinkering tab + auto-confirm; idle actions | A2 |
| P7 | plugin | looter, inventory packer, auto trade add/accept, auto buy/sell, one-touch heal, hotkeys | A2, A3 |
| P8 | plugin | login / login-complete / periodic commands + Character/Server tabs; open pack on login; logout on death; not-applicable table; README parity checklist; live-results completion; handoff | all |

## 7. Not applicable (Decal / Win32 only)

| Mag-Tools feature | Why not applicable | OpenAC-native equivalent |
|---|---|---|
| Remove window frame | Win32 style hack against the retail window | client Options → display/fullscreen |
| Window position (set/del) | `MoveWindow` on the retail HWND | client window placement persistence |
| No-focus FPS / Max FPS | `Thread.Sleep` inside Decal's render hook | client frame limiter option (file an OpenAC issue if absent) |
| Maximize/Minimize chat, maximize on login | blind pixel clicks on the retail chat glyph | chat window is a native retained window |
| `/mt send *`, `/mt click *`, `/mt jump*`, `/mt movement`, `/mt quit`, `/mt client minimize`, `/mt get xy` | synthetic `PostMessage` input | `/mt face`, movement via the navigation automation; jump has no automation surface (recorded) |
| `/mt fellow create` by clicks | drove the retail fellowship panel by clicks | `Fellowship.Create(name, shareXp)` — ported natively |
| VCS/VHS/VHUD connectors, Decal proxy | Virindi/Decal presence probes | plugin command bus, markup panels |
| Tinkering "click yes" by pixel | dialog-button pixel offsets | E-CONFIRM answers the dialog on the wire |

## 8. Live gate protocol

One live session at a time (shared ACE account). Graphical client from the
private root with the plugin deployed; UI evidence via the probe script
(`ACDREAM_UI_PROBE_SCRIPT`, `ACDREAM_AUTOMATION_ARTIFACT_DIR`) and screenshots;
logic evidence via chat transcript and storage files. Each feature gets a row
in `docs/live-results.md`: date, build SHA, steps, observed, verdict. Features
that need a second character (trade) use `testaccount2`/`+Horan` via the
headless host.
