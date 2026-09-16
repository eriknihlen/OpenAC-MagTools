# Handoff

Short status for anyone picking this port up next. See
[docs/2026-09-16-port-design.md](docs/2026-09-16-port-design.md) for the full
slice plan, [README.md](../README.md) for the parity checklist, and
[docs/live-results.md](live-results.md) for what has and has not been proven
against a live server.

## What shipped

Slices P1–P8: the whole Mag-Tools user-facing feature set in the port
design's inventory is implemented and unit-tested against this repo's fake
host — settings/XML schema, the `/mt` console, chat filters, item info on
ident, the loot-rule bridge, auto buy/sell, auto trade add/accept, the
looter, the inventory packer, idle inventory automation, tinkering
auto-confirm, mana recharge, one-touch heal, the six trackers (combat,
corpse, player, equipment, inventory/consumables, profit/loss), the chat
logger, the inventory logger and clipboard export, the HUD, the whole
`magtools.xml` tab tree including the P8 Character/Server command lists, and
the P8 login conveniences (`LoginActions`, `PeriodicCommands`,
`OpenMainPackOnLogin`, `LogOutOnDeath`). 926 tests pass, 0 warnings.
`docs/deviations.md` is the index of every place this port knowingly differs
from the original, with the reason and the file.

## What is pending live gating, and why

`docs/live-results.md`'s Pending table lists 18 rows, each with the exact
steps to close it. In short:

- **P8's own new surface has never been exercised live**: the
  Character/Server On-Login/On-Login-Complete/Periodic command lists (add,
  move, delete, persistence, and the actual dispatch on a real
  reconnect/minute boundary), and Log Out on Death (which needs an actual
  in-world death). Both are unit-tested against the fake host but have not
  run against ACE.
- **Open Main Pack On Login is blocked on a real host gap, not just untested
  (H1)**: `IItemAutomation.Use` on the local player's own object id is
  rejected by the current host (`IsPlayerOwned` excludes the player object),
  and there is no other plugin-reachable route to the inventory window
  today. The macro now warns instead of swallowing the rejection and is
  marked Pending (not Shipped) in the parity checklist until OpenAC slice A6
  (`IUiRegistry.ShowClientWindow`) lands and this is rewired to use it.
- **Everything that needs a scripted world event** (a real fight for the
  combat tracker, a real vendor for auto buy/sell, a real corpse/chest for
  the looter, a real low-mana item for auto recharge, real damage for
  one-touch heal, provoked chat lines for all 32 filters) was out of scope
  for the automated probe rounds run so far and needs a deliberate gate
  session.
- **Everything that needs a second party** (auto trade add/accept, the
  corpse/player trackers' actual tracking, the full `/mt trade`/`/mt vendor`
  transaction flows beyond the no-target messages already gated, `/mt fellow
  create`) needs `testaccount2`/`+Horan` alongside the primary session, per
  the design doc's §8 live-gate protocol.
- **File-backed features on a timer** (the chat logger's 10-minute flush,
  the inventory logger's fresh-character id-request pass, inventory export
  to clipboard) need either a long-running session or a clipboard/file
  inspection step that the automated probe rounds did not include.

None of these are known defects — they are simply unrun. The one item that
WAS run and failed is recorded as FAIL/PARTIAL in the Gated table with the
host fix that resolved it (see below); every host gap found during P1–P7 live
gating has since landed and re-gated PASS.

## OpenAC API changes (branch `claude/magtools-plugin-api`, reviewed head `d5c37bb`)

Five slices, each Sonnet-implemented and Opus dual-lens reviewed (contract
hygiene + parity), landed on this branch (not `main` — push/merge waits for
the owner):

- **A1** — `E-CHAT` (chat `Received`/`RegisterFilter`/`PostMessage`,
  `LogTextType`/`CombatKind`/`Received` on `PluginChatMessage`),
  `E-LIFECYCLE` (`IEvents.LoginComplete`/`Logoff`/`LocalPlayerDied`,
  `ICharacterInfo.ServerPopulation`), `E-SPELLS` (`ISpellCatalog.All`/
  `TryFindByName`), `E-STORAGE` (`IPluginStorage.RootPath`), `E-CLIPBOARD`
  (`IPluginHost.Clipboard.TrySetText`).
- **A2** — `E-OBJECTS` (`ObjectChanged`/`ContainerOpened`/`ContainerClosed`),
  `E-CONFIRM` (`ConfirmationRequested` + `Dialogs.Answer`), `E-SESSION`
  (`ILoginAutomation.Logout`), `E-LOOT` (`NeedsIdentification`/
  `TryClassifyWithProfile` on `IPluginLootClassifier`).
- **A3** — `E-TRADE` (`ITradeAutomation`), `E-VENDOR` (`IVendorAutomation`),
  `E-HOTKEYS` (`IHotkeyRegistry`).
- **A4** — appraisal profiles: retains `WeaponProfile`/`ArmorProfile` on
  `ClientObject` and exposes them through the plugin surface, so item-info
  formatting can fill the weapon/armor segments the original's assess window
  shows (damage range, attack bonus, armor mods) instead of leaving them
  blank.
- **A5** — silent appraisals: tags plugin-initiated `Identify` requests so
  they never pop the client's own examination window (splitting appraisal
  *completion* from *presentation*, and refusing a plugin from evicting a
  user's own in-flight assess).

### Host bugs found by live gates (all fixed and re-gated PASS)

- **Lifecycle emission**: `LoginComplete` was only emitted at command
  boundaries in the A2 host, so the plugin's online banner never appeared
  during idle world time; fixed in A3 by emitting the real async in-world
  edge from the session tick (`dcfa997`, `705de5c`, `94a296f`).
- **Identify scope**: `IWorldObjectAutomation.Identify` refused anything
  that was not a corpse or inside the open container, so a plugin could
  never appraise an owned or landscape object; fixed to accept any known
  object (`fa5e111`).

## Known test-environment note

A copy of this plugin deployed to the default `%LOCALAPPDATA%\acdream\plugins`
during manual live-gate sessions was found to skew two OpenAC Headless tests
that enumerate the real plugins directory without redirecting it. Both live
sessions now set `ACDREAM_DATA_DIR` to a sandboxed directory before
deploying, so the default plugins directory (and the Headless suite that
reads it) stays clean regardless of what a developer has manually installed
for live testing.
