# Live results

One row per feature, verified against a running client and a live ACE server.
Automated tests say the code does what it was written to do; these rows say the
feature works in the game.

Protocol: one live session at a time (ACE allows one session per account).
Launch the client with the plugin deployed (`tools/deploy.ps1`), exercise the
steps, record what was observed, and give a verdict of PASS, FAIL or PARTIAL.
A FAIL or PARTIAL row names the issue it is tracked under. A row with no
verdict yet is PENDING, with the exact steps a tester would run to close it —
no PASS is ever claimed without having actually observed it.

Account: `testaccount` / `+Acdream` (server guid `0x5000000A`) is the primary
session. Rows that need a second party (trade, fellowship, corpse/player
tracking) use `testaccount2` / `+Horan` via the headless host, run alongside
the graphical primary session per the design doc's live-gate protocol (§8).

## Gated (P1–P8 rounds)

| Feature | Date | Build | Steps | Observed | Verdict |
|---|---|---|---|---|---|
| Plugin load + both windows mount | 2026-09-16 | plugin 052a6f0 / OpenAC 3d623e3 | launch snapshot client, session.json selects +Acdream | log: `plugin loaded: openac.magtools`, `plugin UI window loaded: ...:main` and `...:hud`; shelf shows MT and MH | PASS |
| `/mt test`, `opt list`, `opt get`, `opt set`, `opt remember` | 2026-09-16 | same | probe `command /mt ...` | chat (colour 5, purple): full option list, `ItemInfoOnIdent.Enabled = True`, `Set Filters.AttackEvades = True`, `Remembered ...`; `Mag-Tools.xml` contains `<AttackEvades>True` | PASS |
| `/mt dumpspells` | 2026-09-16 | same | probe | `Spell dump written: mt spelldump.txt`; file has 6,266 spells (whole table via `Spells.All`) | PASS |
| `/mt select <name>` with no match | 2026-09-16 | same | `/mt select Ulgrim` (not nearby) | `Nothing found named: ulgrim` (lower-cased as original) | PASS |
| not-applicable + deferred command messages | 2026-09-16 | same | `/mt jump`, `/mt trade accept`, `/mt logoff` | `jump is not applicable in OpenAC`; `trade accept requires the trade/vendor API (coming)`; `logout is not available yet` | PASS (deferred ones re-gate in P7/P8) |
| Login banner `Plugin now online. Server population: N` | 2026-09-16 | same | screenshots at +4 s and +12 s in world (A2 host), then +12 s on the A3 host | A2 host: never appears (lifecycle deltas were only emitted at command boundaries; fixed in A3 by emitting the real in-world edge from the session tick); A3 host: `<{Mag-Tools}>: Plugin now online. Server population: 0` shown in purple | PASS (after OpenAC A3 fix) |
| Main window tab tree, HUD window | 2026-09-16 | same | — | shelf MT/MH buttons; MT click opens the Mag-Tools window: top tabs Trackers/Loggers/Tools/Misc, second row Mana/Combat/Corpse/Player/Inv. Items, Mana page with list + `Mana needed: 0` + Recharge toggle + `Unretained Items: 0` (screenshot 10-window) | PASS (tab-by-tab walk pending) |
| Main window tab walk (all top tabs + Trackers pages) | 2026-09-16 | plugin 2710cbe / OpenAC 1941f71 | probe 5: open MT, click Mana/Combat/Corpse/Player/Inv. Items, Loggers, Tools, Misc; screenshots 11–18 | every page renders its authored controls: Combat shows the three sub-tabs, the monster list header (`KB's` / `Dmg Rcvd` / `Dmg Givn`) + `All` row and the labelled damage list; Loggers shows Chat Group 1/2/Options; Tools shows Inventory/Tinkering/Character/Server with the two clipboard buttons + regex field + lists; Misc → Options lists every option with the original defaults and the `Target Output Window` field = 1. Visual nits: `Typeless` truncates in the 40 px column, `Dmg Rcvd` header clips, `Target Output Window:` label clips | PASS (nits fixed at P8 -- see `docs/deviations.md`; re-screenshot owed) |
| Combat tracker live (real fight) | — | — | needs a monster near +Acdream; not yet exercised | — | PENDING (P8 gate round) |
| Item info on ident (user path, selection + LeftClickIdent) | 2026-09-16 | plugin 2710cbe / OpenAC 1941f71 | probe 6: `/mt opt set ItemInfoOnIdent.LeftClickIdent true`, `/mt selectp e` (selected `Bronze Acid Katar`), `/mt selectp a` | selection works; no item-info line printed. Root cause in OpenAC: `IWorldObjectAutomation.Identify` delegates to the loot surface's `Identify`, which returns `InvalidItem` for anything that is not a corpse or inside the open container (`AppAutomationSurface.cs:3058-3086` on 1941f71), so owned/landscape objects can never be appraised by a plugin. Fix queued for the OpenAC A3 second round; plugin will also log refused ident requests when DebuggingEnabled | FAIL → host fix pending |
| Item info on ident (user path) — on the host with the Identify fix | 2026-09-16 | plugin 5cd8e6c / OpenAC 94a296f | probe 7: LeftClickIdent on, `/mt selectp e`, `/mt selectp a` | two item-info lines printed in colour 14: `Acid Phyntos Wasp Essence (80), Lvl 70, Summoning 370, Craft 6, Value 5,000, BU 50, [DR 4, CD 10, CR 7]` and `Bronze Acid Katar (Unarmed Weapon), 1%md, 1.5%msl.d, (-1.0/0 1.0), Craft 3, Value 159, BU 135`. Defects: the katar's buffed block shows `-1.0/0` (no damage) and no attack-bonus term although the assess window shows Damage 5.72–11 and +1% attack: the host parses the appraisal WeaponProfile/ArmorProfile (`AppraiseInfoParser.cs`) but only `ItemAppraisalTextFormatter` consumes them — they are not retained on the object nor exposed through `TryCaptureProperties`/`PluginInventoryItem`, so segments 8–12/17 (weapon) and 25 (armour mods) cannot be filled. Also `(80)` after the essence name needs explaining (mastery lookup). OpenAC slice A4 (profiles on the object + plugin surface) queued; plugin re-gate after | PARTIAL → host gap (A4) |
| HUD window (14 rows) | 2026-09-16 | plugin 5cd8e6c / OpenAC 94a296f | probe 7: MH shelf click | `Mag-Tools HUD` window with the 14 rows in §4.3 order; Monsters 11, Pack Slots 28, Players empty (only player), Mana/Comps/DPS empty; `Net Profit 5m/1h = 893.0/h` seconds after login — WRONG (single-sample rate bug, sent to the P5 review) | PARTIAL → P5 fix |
| Mana tab (equipment tracker) | 2026-09-16 | same | probe 7: Trackers → Mana | four equipped items with icons, state icons, `cur / max` (Leather Gauntlets `0 / 294`, red state icon), `Mana needed: 294`, Recharge toggle on, `Unretained Items: 0` | PASS |
| Inv. Items tab (consumables + profit/loss) | 2026-09-16 | same | probe 7: Trackers → Inv. Items | authored layout renders (profit block rows, consumables header, rows with icons/counts sorted by class); rates are non-zero seconds after login (`Net Profit 783.5`, `Prismatic Taper Avg/h 278,063.6`) — same single-sample rate bug; 55 px name column truncates names as the original's did | PARTIAL → P5 fix |
| HUD + Inv. Items rates after the P5 fixes | 2026-09-16 | plugin 7fc29ee / OpenAC 19355ef | probe 8: +15 s after login | HUD `Net Profit 5m/1h` blank, Monsters 11, Pack Slots 28; Inv. Items profit block and every `Avg/h` cell blank (zero) — the fabricated rates are gone | PASS |
| Plugin-initiated appraisal is silent | 2026-09-16 | same | observed during probe 8 | the equipment tracker's re-identify opened the client's "Leather Gauntlets" examination window; Decal's request was silent. OpenAC slice A5 (appraisal origin tagging; window only for user-originated requests) | FAIL → host fix (A5) |
| Item info with the appraisal weapon profile (A4) | 2026-09-16 | plugin 94b3b99 / OpenAC 19355ef | probe 9: LeftClickIdent on, `/mt selectp a` | `Bronze Acid Katar (Unarmed Weapon), 5.72-11, +1%a, 1%md, 1.5%msl.d, (40.6/11 1.0/1.0), Craft 3` — damage range, attack bonus and the buffed block now match the original's segment formats (assess window: Damage 5.72–11, +1% attack) | PASS |
| Tinkering tab | 2026-09-16 | same | probe 9: Tools → Tinkering | Add Selected Item, material menu (Brass…), Minimum Percent 100, Target Total Tinks, Start/Stop, list — renders as authored; scan loop not exercised (needs salvage bags) | PASS (render) |
| Corpse / Player tabs | 2026-09-16 | same | probe 9: Trackers → Corpse, Player | both pages render with their sub-tabs (Tracked Corpses/Options, Tracked Players/Options); lists empty (no corpses/other players near +Acdream) | PASS (render; tracking needs a second character / a kill — P8 gate round) |
| Plugin-initiated appraisal is silent (A5) | 2026-09-16 | plugin 3c30f06 / OpenAC d5c37bb | probe 10: idle 25 s after login (equipment tracker re-identifies equipped items) | no examination window opened (screenshots 31/32); the Mana list still populates from the silent appraisals | PASS |
| `/mt trade *`, `/mt vendor *`, `/mt autopack` (P7 commands) | 2026-09-16 | same | probe 10: `/mt trade accept`, `/mt vendor buy`, `/mt vendor addbuy Foo 3`, `/mt autopack`, `/mt trade add Foo` with no vendor/trade open and no AutoPack profile | `No vendor is open.` ×2, `No inventory item found named: foo`; trade accept and autopack silent (no trade open / no profile) as the original | PASS (messages); real trade/vendor flows need a partner/vendor — P8 gate round |
| `/mt logoff` | 2026-09-16 | same | probe 10 | log: `logout-confirmed`, `returning to character select`, `character logoff complete` — the client's own graceful logout | PASS |
| Open main pack on login (P8, enabled default) | 2026-09-16 | plugin 5822062 / OpenAC abc748d | probe 12: login with `Misc/OpenMainPackOnLogin` at its default (true), screenshot at +10 s | `Inventory of +Acdream` window open at +10 s — `Ui.ShowClientWindow(PluginClientWindow.Inventory)` (OpenAC slice A6) opens the main pack the same as the original's `Actions.UseItem(myId, 0)` did | PASS |
| Combat page after the P8 column polish | 2026-09-16 | plugin e8fba24 / OpenAC d5c37bb | probe 11: Trackers → Combat | `Typeless`/`Electric` labels and the `Dmg Rcvd`/`Dmg Givn` headers render in full (screenshot 35) | PASS |
| Combat tracker live (real fight) | 2026-09-16 | plugin a016805 / OpenAC 669832d | r2/r3/r6/r8: `@create drudgeprowler` (plus tuskerguard/olthoisoldier/5 drudges), `/mt attack_melee closest` x3-8, `@smite all`; Trackers -> Combat -> Current Session Stats | monster list and counters populate live: `All 7`, `Drudge Prowler 5`, `Tusker Guard 1`, `Olthoi Soldier 1` with per-row KB's, plus `Attacks 7 (100%)`, `Av/Mx 0 / 0`, `Crits 0 (0.0%)` (screenshots g07/g23/g36). `Dmg Rcvd`/`Dmg Givn` stayed blank: +Acdream one-shots every spawn (kill message only, no damage line) and no monster ever landed a hit, even with `@attackable on` -- the damage cells were never provoked, not observed wrong | PARTIAL (rows + KB's + attack counter PASS; damage cells unprovoked) |
| Corpse tracker live tracking | 2026-09-16 | same | r7/r16: `/mt opt set CorpseTracker.TrackAllCorpses true`, kill with `/mt attack_melee closest` and with `@smite all`, Trackers -> Corpse -> Tracked Corpses | one row per corpse with Time/Name/Coords: `Wed 16:3x  Corpse of Drudge Prowler  95.8S, 101.0W` (g27) and nine rows after a smite (Drudge Prowler + Mosswart Feeder/Creeper Mosswart, h05); persisted to `sawato/.CorpseTracker.xml`. With `TrackAllCorpses` off a monster corpse is correctly NOT tracked (the port's burden>6000 + killer-name rule) | PASS |
| Chat filter `Filters.MonsterDeaths` | 2026-09-16 | same | baseline r2/r3/r6 (filter at its default False), then r16 with `Filters.MonsterDeaths=True` seeded in `Mag-Tools.xml`: `@create drudgeprowler` + `@smite all` | filter OFF: `You killed Mosswart Feeder!`, `You obliterate Drudge Prowler!`, `You cleave Drudge Prowler in twain!` all reach the transcript (g06/g10/g21). Filter ON: `@smite all` killed nine-plus monsters (proved by the nine Corpse-tracker rows, h05) and not one kill line reached the transcript (h04) | PASS (1 of 32 rules) |
| Chat filters: the other 31 rules | 2026-09-16 | same | -- | never provoked this round. Blocker: +Acdream never misses, is never hit, never fizzles, never salvages, and met no vendor/NPC chatter, so AttackEvades/DefenseEvades/AttackResists/DefenseResists/NPKFails/DirtyFighting/SpellCasting*/SpellCastFizzles/CompUsage/SpellExpires/HealingKit*/Salvaging*/AuraOfCraftman/ManaStoneUsage/TradeBuffBotSpam/FailedAssess/KillTaskComplete/VendorTells/MonsterTell/NpcChatter/MasterArbitrator*/StatusText* had no matching line to suppress | PENDING (needs a character that can miss/be hit, and an NPC/vendor) |
| Idle inventory automation: Aetheria revealer + key ringer | 2026-09-16 | same | r21: `/mt opt set InventoryManagement.AetheriaRevealer true` + `InventoryManagement.KeyRinger true`, then `@ci 42645` (Aetheria Mana Stone), `@ci 42635` (Coalesced Aetheria), `@ci 48746` (Aged Legendary Key), `@ci 48954` (Burning Sands Keyring); idle | both fired within one idle think, unattended: `A sigil rises to the surface as you bathe the aetheria in mana.` (twice) and `You add the key to the keyring.` (h31); the confirmation dialog was answered automatically | PASS |
| Idle inventory automation: heart carver / shattered-key fixer / key deringer | 2026-09-16 | same | -- | all three need an `Intricate Carving Tool`; it has no entry in this ACE build's weenie class-name table and `@ci intricatecarvingtool` answers `intricatecarvingtool is not a valid weenie` | PENDING (item not creatable on this server) |
| On-Login / On-Login-Complete / Periodic commands -- SERVER scope | 2026-09-16 | same | r11/r22/r23: `_sawato/OnLoginCommands` + `OnLoginCompleteCommands` + `PeriodicCommands offset=0 interval=1` pre-written into `Mag-Tools.xml`, reconnect | both lists dispatch in order one tick apart: `<{Mag-Tools}>: Failed to Get Charlie` (last On-Login row) then `<{Mag-Tools}>: Misc.OpenMainPackOnLogin = False` (On-Login-Complete row) (h38); the periodic command fires on each whole UTC minute, two `Misc.LogOutOnDeath = False` lines one minute apart (g55) | PASS |
| On-Login / On-Login-Complete / Periodic commands -- CHARACTER scope | 2026-09-16 | same | r10: the identical three lists written under `_testaccount_sawato/_x002B_Acdream`, reconnect | nothing dispatched. Root cause: `ICharacterInfo.Name` is empty at the LoginComplete edge, so `SessionContext.cs:98` caches an empty character name and `SettingsScope.Character` builds `_testaccount_sawato/` + empty. The same defect names the tracker files `sawato/.CorpseTracker.xml` and `sawato/.Inventory.xml` with no character prefix | FAIL -> defect |
| Open main pack on login: disabled setting | 2026-09-16 | same | r11 closed the inventory window with `input press ToggleInventoryPanel` and exited; r12/r22 then logged in with `Misc/OpenMainPackOnLogin=False` | the `Inventory of +Acdream` window does NOT open at LoginComplete (g56/h32); the enabled/default case stays PASS (probe 12) | PASS |
| Character/Server command tabs: Move + Delete | 2026-09-16 | same | r23: three rows (`/mt opt get Alpha|Bravo|Charlie`) seeded in the server scope, Tools -> Server, click the up icon on row 2, then the delete icon on row 1 | the up icon swaps Bravo above Alpha and highlights the moved row (h39 -> h40); delete removes it (h41); both survive to disk, `Mag-Tools.xml`'s `_sawato/OnLoginCommands` ends as Alpha, Charlie | PASS (Move/Delete) |
| Character/Server command tabs: Add | 2026-09-16 | same | -- | the UI probe has no text-entry verb (`click`/`hover`/`drag`/`input press|down|up <InputAction>` only), so the command text box cannot be filled and Add cannot be exercised meaningfully | PENDING (harness cannot type) |
| Inventory packer real run | 2026-09-16 | same | r17/r18/r19 with MossTank loaded as the loot classifier: `Default.AutoPack.utl` (KeepUpTo, KeepCount 1, name regex `Prismatic Taper`) in `%LOCALAPPDATA%\acdream\vtank`, then `/mt autopack` | `<{Mag-Tools}>: Auto Pack - Started.` then `Auto Pack - Completed.` for both invocations (h18/h20). Two findings: (a) with only `+Acdream.AutoPack.utl` present the run is a silent no-op, the character-scoped profile name never resolves (same empty-`Character.Name` defect), and it only starts once `Default.AutoPack.utl` exists; (b) items actually landing in their profile-assigned side pack was not verified (no per-item message, pack contents not diffed) | PARTIAL |
| Inventory export to clipboard | 2026-09-16 | same | r13/r14: Tools -> Inventory, click `Clipboard Worn Equipment` (808,398) then `Clipboard Inventory Info` (962,398); `Get-Clipboard` read from a separate PowerShell session both during the run (+65 s, client still up) and after a graceful exit | the plugin prints the exact messages, `Copying all inventory item info to clipboard...` then `All inventory item info has been copied to the clipboard.` (g62), but the Windows clipboard was EMPTY both times (a `Set-Clipboard`/`Get-Clipboard` self-test in the same shell round-trips fine). `InventoryExporter.cs:192` ignores `TrySetText`'s result, so the completion message prints whether or not the clipboard was set | FAIL -> defect |
| Chat logger file | 2026-09-16 | same | r10/r11: `ChatLogger/Persistent=True` + `ChatLogger/Group1/Area=True` seeded, `/say ChatLoggerProbe alpha`, graceful logoff | the GUI transcript works, Loggers -> Chat Group 1 shows `26/09/16 16:5x  ChatLoggerProbe alpha` (g53), but no `<Server>/<Character>.ChatLogger.txt` is ever written. `Loggers/Chat/ChatLogger.cs:187` returns early from `Flush()` when `_character` is empty, and `_character` is the empty LoginComplete name | FAIL -> defect (same root cause) |
| Inventory logger file | 2026-09-16 | same | r29: `InventoryManagement/InventoryLogger=True` seeded, fresh login, about 60 s in world, graceful logoff | a file IS written, but as `sawato/.Inventory.xml` (no character prefix) and empty: `<ArrayOfMyWorldObject />`. No `Requesting id information...` messages printed | PARTIAL -> defect (empty content plus missing character prefix) |
| `/mt fellow create` | 2026-09-16 | same | r9: `/mt fellow create MagToolsGate` with no fellowship active, then `/mt fellow disband` | no chat output from the plugin and no server response of any kind; a fellowship was not observed to form (the social panel was not opened to confirm) | PENDING (inconclusive; needs a Fellowship-panel or second-party check) |
| Mana auto recharge | 2026-09-16 | same | r19/r20/r23 idle sessions with `ManaManagement/AutoRecharge` at its default True | never triggered. The port keys on a server line containing `Your` and ` is low on Mana.` (`AutoRecharge.cs:77-79`) and deliberately ignores your own `/say` echo, so it cannot be hand-provoked; +Acdream's equipped items read `294 / 294` once appraised, so the server never emits the warning. The `The Leather Gauntlets is already full of mana.` lines seen in r19 came from MossTank's own recharger, not Mag-Tools -- they do not appear in a Mag-Tools-only session | PENDING (trigger not provokable on this character) |
| Auto buy/sell at a real vendor | 2026-09-16 | same | r24/r25/r28: `@telepoi Holtburg`, `/mt usel closestvendor` (twice, 20 s + 15 s), `/mt usel closestnpc`, then `click at <npc>` + `input press UseSelected` | no vendor panel ever opened, the character never moved, nothing was printed. `/mt usel closestvendor` DID resolve a Vendor-class object (it never printed `Nothing found named:`) and issued `Items.Use`, but no walk-to-use and no panel followed. Harness blocker on the alternative path: the UI probe's `click at` goes through `UiRoot` only (`RetailUiAutomationProbe.ClickAtPoint`), so a 3-D world object cannot be picked or selected from a route | PENDING (vendor could not be opened; plugin path and harness path both blocked) |
| Auto looting (chests, corpses, salvage) | 2026-09-16 | same | r15/r18 with MossTank loaded; `/vt loot load MTGateAll` (a Keep-everything profile) | the classifier plumbing is proven: `Loaded loot profile MTGateAll.` and `Loaded loot profile +Acdream.AutoPack.` (h16), so MossTank registers as `moss-tank` and MagTools' `LootRuleProcessor` reaches a named profile. The looting itself was not exercised: the kill in that run did not land, and no chest is creatable at the login spot | PENDING |
| Auto add to trade / auto trade accept / `/mt trade *` full flows | 2026-09-16 | same | -- | not attempted. Needs a second live session (`testaccount2`/`+Horan`) beside +Acdream and a trade opened from the graphical side; both of the client's trade-open paths start from a world selection, which the probe cannot do (see the vendor row) | PENDING |
| Player tracker live tracking | 2026-09-16 | same | -- | not attempted; needs `+Horan` standing next to +Acdream | PENDING |
| One-touch heal real heal | 2026-09-16 | same | -- | `MagToolsPlugin.cs:199` registers the One Touch Heal hotkey with a `default` (unbound) chord, there is no `/mt` verb for it, and the probe can neither type into the rebind UI nor inject a plugin hotkey chord | PENDING (hotkey cannot be bound or fired from a route) |
| Log out on death | 2026-09-16 | same | -- | not attempted deliberately: the only ways to kill +Acdream on this server (dropping its health to zero, or letting a monster finish it) cost the character its vitae and dropped items. Needs the owner's go-ahead | PENDING (destructive; owner approval needed) |

## Pending (owed before the port can be called fully live-gated)

Every row below carries the exact blocker that stopped it on 2026-09-16, so a
future session does not re-derive it. The gate round's harness and its limits
are described under "Harness notes" at the bottom.

| Feature | Blocker | Verdict |
|---|---|---|
| Chat filters: the 31 rules other than `MonsterDeaths` | needs a character that can miss, be evaded, be hit, fizzle, resist, salvage and use comps, plus an NPC/vendor to talk. +Acdream one-shots everything and is never hit | PENDING |
| Auto buy/sell at a real vendor | the vendor panel could not be opened from a route: `/mt usel closestvendor` resolves a Vendor-class object and issues `Items.Use` but produces no movement, no panel and no message, and the UI probe's `click at` only reaches retained UI, so a 3-D world pick is impossible | PENDING |
| Auto add to trade / auto trade accept / `/mt trade *` full flows | needs `testaccount2`/`+Horan` in a second session AND a world-selection-driven trade open, which the probe cannot perform | PENDING |
| Auto looting (chests, corpses, salvage) | the loot-classifier path is proven (`/vt loot load <name>` works, MossTank registers as `moss-tank`), but a corpse/chest loot run was not driven to completion | PENDING |
| Inventory packer: items land in their profile-assigned pack | the Started/Completed lifecycle is PASS; the per-item placement was not verified (no per-item message; pack contents not diffed before/after) | PENDING |
| Idle automation: heart carver / shattered-key fixer / key deringer | `Intricate Carving Tool` is not creatable on this ACE build (`@ci intricatecarvingtool` -> not a valid weenie) and is absent from the weenie class-name table | PENDING |
| Mana auto recharge | the trigger is a server line containing `Your` + ` is low on Mana.`; +Acdream's equipped items are at full mana so the server never emits it, and the port deliberately ignores a self-`/say` echo | PENDING |
| One-touch heal real heal | the hotkey is registered unbound (`default` chord), there is no `/mt` verb for it, and the probe can neither type into the rebind UI nor inject a plugin chord | PENDING |
| Log out on death | killing +Acdream costs the owner's character vitae and dropped items; not attempted without the owner's go-ahead | PENDING |
| Character/Server command tabs: Add | the UI probe has no text-entry verb, so the command text box cannot be filled | PENDING |
| Player tracker live tracking | needs `+Horan` standing next to +Acdream | PENDING |
| `/mt fellow create` | issued live and produced no plugin output and no server response; a fellowship was not observed to form. Needs a Fellowship-panel or second-party confirmation before it can be called FAIL | PENDING |
| Combat tracker `Dmg Rcvd` / `Dmg Givn` cells | never provoked: every +Acdream hit is a one-shot kill (kill message only, no damage line) and no monster landed a hit even with `@attackable on` | PENDING |

## Defects found in the 2026-09-16 gate round

1. **`ICharacterInfo.Name` is empty at the LoginComplete edge** (host) and is
   cached there by the plugin (`src/OpenAC.MagTools/SessionContext.cs:98`).
   Every character-scoped name derived from it is wrong for the whole session.
   Observed consequences, each independently reproduced:
   - character-scoped On-Login / On-Login-Complete / Periodic command lists
     never dispatch (the identical server-scoped lists do);
   - tracker files are written as `sawato/.CorpseTracker.xml` and
     `sawato/.Inventory.xml` (no `+Acdream` prefix);
   - `Loggers/Chat/ChatLogger.cs:187` refuses to flush at all, so the chat log
     file is never written;
   - `Macros/InventoryPacker.cs:138` cannot resolve `+Acdream.AutoPack` and the
     packer is a silent no-op unless `Default.AutoPack.utl` exists.
   Host side: `src/AcDream.App/Plugins/AppAutomationSurface.cs:804-816` reads
   the name off the player object in the object table, which has no name yet at
   that moment. The live read works later in the session.
2. **Clipboard export never reaches the clipboard.** Both Tools -> Inventory
   buttons print their start and completion messages, but the Windows clipboard
   stays empty when read from another process during and after the session.
   `src/OpenAC.MagTools/Inventory/InventoryExporter.cs:192` discards
   `IPluginClipboard.TrySetText`'s result, so the success message is printed
   unconditionally.
3. **Inventory logger writes an empty document.** With
   `InventoryManagement/InventoryLogger` on, a fresh session produced
   `<ArrayOfMyWorldObject />` and printed no `Requesting id information...`
   lines.
4. **`/mt usel closestvendor` is a silent no-op.** A Vendor-class object is
   resolved (no `Nothing found named:` message) and `Items.Use` is issued, but
   the character does not walk and no vendor panel opens within 35 s.
5. **`/mt castp <spell> on <target>` is silent.** No cast, no
   `No spell named:`/`No target found named:` message, no server reaction.
6. **`/mt fellow create <name>` is silent.** No plugin output and no server
   response.
7. **Host shutdown crashes after a clean session** (OpenAC, not the plugin):
   two runs ended with `Fatal error 0xC0000005` in
   `Silk.NET.Vulkan.Vk.DestroyDevice` from
   `VulkanGraphicsContext.Dispose`, and one with `0xC0000374` (heap
   corruption), each AFTER `graceful logout confirmed` and
   `Mag-Tools disabled`.

## Harness notes (2026-09-16)

- Client: `AcDream.App.exe` built from the final API branch head `669832d`
  (`OpenAC/.worktrees/magtools-api-final`), plugin deployed from `a016805`.
- Routes, logs and screenshots for this round live in the session scratchpad
  under `live-gates/` (`r1.txt`..`r29.txt`, `launch-r*.log`,
  `artifacts/screenshots/g01..g67`, `h01..h54`).
- The UI probe's verbs are `dump`, `click`, `hover`, `mousemove`,
  `doubleclick`, `drag`, `wait`, `sleep`, `assert`, `command`, `input`,
  `mouselook`, `checkpoint`, `renderpack`, `potato`, `uionly`, `focus`,
  `resize`, `screenshot`, `close-client`. `hover` and `doubleclick` take
  `element <datId>` / `item <guid>` only -- there is no `hover at`/
  `doubleclick at`. A bad verb aborts the script and leaves the client running
  forever, which then needs a graceful `CloseMainWindow`.
- `click at <x> <y>` dispatches through `UiRoot` only, so nothing in the 3-D
  world can be picked or selected from a route. Every gate that starts from a
  world selection (vendor, trade, corpse row selection) is blocked on that.
- Mag-Tools window visibility persists across sessions; the selected tab does
  not (it resets to Trackers -> Mana).
- Loot-classifier gates need MossTank in the session's plugin list
  (`"plugins": ["openac.magtools", "acdream.mosstank"]`); it registers the
  `moss-tank` classifier that `LootRuleProcessor` resolves. MossTank stays
  inert otherwise (autostart only acts on session settings).
- `.utl` profiles written for this round and left in
  `%LOCALAPPDATA%\acdream\vtank`: `MTGateAll.utl` (Keep everything),
  `+Acdream.AutoPack.utl` and `Default.AutoPack.utl` (KeepUpTo 1 on
  `Prismatic Taper`).
- Server toggles used and restored: `@attackable on` -> `@attackable off`
  (confirmed by `Monsters will only attack you if provoked by you first.`).
  `@telepoi Holtburg` was undone with
  `@teleloc 0x0108020D 46.438545 -54.503498 0.004200 -0.128570 0 0 -0.991700`.
  `Mag-Tools.xml` was restored to its pre-round contents.
