# Handoff

Short status for anyone picking this port up next. See
[docs/2026-09-16-port-design.md](2026-09-16-port-design.md) for the slice
plan, [README.md](../README.md) for the parity checklist (with a live-gate
verdict per row), [docs/live-results.md](live-results.md) for every live
result and the exact steps behind it, and [docs/deviations.md](deviations.md)
for every place this port knowingly differs from the original.

## What shipped

Plugin slices P1–P13 (P9–P13 were live-gate fix/porting rounds): every
user-facing Mag-Tools feature in the design's inventory is implemented and
tested against this repo's fake host. P13 ported `/mt client minimize`,
`/mt quit` and `/mt exit` onto the new `IHostWindow` host-window surface
(OpenAC A10), moving them from Not applicable to Shipped: 40 of the 45
checklist rows are Shipped; the other 5 remain Not applicable
(Decal/Win32-only) with a one-line reason and the OpenAC-native equivalent in
the README. 1,025 tests, 0 warnings.

Process: Fable wrote the design and slices; Sonnet implemented one slice at a
time; a fresh Opus reviewed every slice (and every fix round) before the next
started; findings were fixed before moving on.

## Live gating

Five probe rounds against the local ACE server with `+Acdream` (and the
headless `+Horan` bot on `testaccount2` as the partner), all recorded in
`docs/live-results.md`: rounds P1–P8 during the slices, then rounds 3 and 4
(and 5, see below) after the host fixes. Proven live, among others: the
`/mt` console and option family, item info on ident with appraisal profiles,
the loot-rule bridge through MossTank, the looter opening a corpse through the
plugin's own `Use` and moving loot into the pack, the vendor panel opening
through the plugin's `Use` with AutoBuySell running, trade open/add and the
bot's AutoTradeAccept completing a trade, the combat/corpse/player/equipment/
inventory/profit trackers, the chat logger and inventory logger files, both
clipboard exports, the HUD and the whole tab tree, character- and
server-scoped On-Login / On-Login-Complete / Periodic commands, the Add
fields, the one-touch-heal hotkey, and per-account/server/character settings.

Rows that stay PENDING have a written reason each: server-side triggers this
admin character cannot provoke on the local ACE (it one-shots everything and
is never attacked, so no damage lines, no evade/resist/fizzle filters, no
real death for Log Out on Death; a drained item never emits the low-mana
line), and weenies this ACE build cannot create (Intricate Carving Tool,
healing kits). The steps to close them are in the Pending table.

## OpenAC API changes awaiting the owner's push

Branch `claude/magtools-plugin-api` in `OpenAC/.worktrees/magtools-api`
(head `eb91874`; 17 files in
`AcDream.Plugin.Abstractions`, 124+ files overall). Every slice was implemented with fail-first tests and reviewed by a
fresh Opus; nothing is pushed and no PR exists. The frozen detached snapshot `magtools-api-a10` (that head) is what
`Directory.Build.props` builds against; once the branch merges, point
`OpenAcRoot` back at the checkout.

- **A1** chat (`Received`/`RegisterFilter`/`PostMessage`, structured
  `PluginChatMessage`), lifecycle (`LoginComplete`/`Logoff`/`LocalPlayerDied`,
  `ServerPopulation`), spells (`ISpellCatalog.All`/`TryFindByName`), storage
  root path, clipboard.
- **A2** object events (`ObjectChanged`/`ContainerOpened`/`ContainerClosed`),
  confirmations (`ConfirmationRequested` + `Dialogs.Answer`), logout, loot
  classifier (`NeedsIdentification`/`TryClassifyWithProfile`).
- **A3** `ITradeAutomation`, `IVendorAutomation`, `IHotkeyRegistry`; plus the
  lifecycle-emission fix (in-world edge from the session tick) and
  `Identify` for any known object.
- **A4** appraisal weapon/armor profiles retained on the object and exposed
  to plugins.
- **A5** silent plugin appraisals (request origin; completion split from
  presentation).
- **A6** client-window control (`Show/Hide/Toggle/IsClientWindowVisible`).
- **A7** character identity valid at `LoginComplete`; plugin `Use` on an
  unowned world object takes the walk-then-use path with a truthful outcome;
  clipboard writes marshalled to the main thread and verified.
- **A8** the automation use route arms the container/vendor request exactly
  as a click does (the plugin's `Use` now opens corpses, chests and vendors);
  a stalled approach gives up on the move-to's own progress counter instead
  of holding the use gate for the session (private register row AD-141);
  targeted-use items get a specific refusal; automation outcomes logged.
- **A9** the headless host: real `ICharacterInfo`, `Objects.TryGet`/
  `CaptureObjects`, per-plugin storage, and the trade owner wired into its
  inbound router (it dropped every trade message before). Shared
  `FilePluginStorage`, `RuntimeCharacterIdentity` and
  `RuntimeWorldObjectProjection` now serve both hosts.
- **A10** `IPluginHost.Window` (`IHostWindow`): minimize/restore/request-close
  over the client's own OS window, the same three controls its title bar
  already offers. Graphical host is real; headless answers Minimize/Restore
  as Unavailable (no window) and RequestClose ends the plugin's own session
  gracefully. Platform caveat: Wayland cannot report iconification back to
  the client at all, so `Minimize` always reports Unavailable there even
  when the window did minimize.

Host defects found only by live gating and fixed on the branch: lifecycle
deltas only at command boundaries; `Identify` limited to loot containers;
appraisal profiles not retained; plugin appraisals opened the assess window;
no client-window control; empty character name at login; `Use` no-op on
unowned objects; clipboard write silently lost off-thread; the automation use
route never armed the container request; an unreachable approach wedged the
use gate; the headless host dropped trade messages and stubbed
identity/objects/storage. One host bug is reported, not fixed: an occasional
crash in Vulkan device destruction at shutdown after a clean session (D7 in
`docs/live-results.md`).

## Test-environment notes

- A plugin deployed to the default `%LOCALAPPDATA%\acdream\plugins` skews two
  OpenAC Headless tests that enumerate the real plugins directory; run those
  suites with `ACDREAM_DATA_DIR` at an empty directory.
- The headless bot drops to character select about five minutes after
  `enteredWorld`; gate partner tests inside that window and trust the status
  file's events, not the resource samples.
- A hard-killed client or bot leaves its ACE session stuck for 3–8 minutes.
- A client can linger as a process object with one stuck thread after a
  confirmed graceful logout (host D7 class); while it does, it holds the
  deployed plugin DLL, so `tools/deploy.ps1` to the default plugin folder
  fails. No reboot is needed: rename the locked DLL aside (Windows allows
  moving a mapped DLL), deploy, and delete the renamed copy after the next
  reboot. Done once on 2026-09-17 (`OpenAC.MagTools.dll.locked-9060`).
