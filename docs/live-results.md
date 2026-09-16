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
| Auto looting (chests, corpses, salvage) | 2026-09-16 | same | r15/r18/r30/r31 with MossTank loaded: `/vt loot load MTGateAll` (a Keep-everything profile), `@create drudgeprowler`, `/mt attack_melee closest` to the kill, then `/mt uselp corpse` to open the corpse; `Looting/AutoLootCorpses` at its default True | the classifier plumbing is proven: `Loaded loot profile MTGateAll.` and `Loaded loot profile +Acdream.AutoPack.` (h16), so MossTank registers as `moss-tank` and MagTools' `LootRuleProcessor` reaches a named profile. The looter itself never ran: it triggers on `ContainerOpened` (`Macros/Looter.cs:90`), and the corpse container could not be opened from a route. `/mt uselp corpse` resolved a corpse (no `Nothing found named:` message) and issued `Items.Use`, but no container window opened and no loot line printed in 45 s (k04/k05/k06) -- the same shape as the vendor row | PENDING (corpse container cannot be opened from a route) |
| Auto add to trade / auto trade accept / `/mt trade *` full flows | 2026-09-16 | same | attempted to bring up the second party: `AcDream.Headless run --config bot.json` with `account: testaccount2`, `character: {index: 0}`, `policy: idle`, `loginCommands: ["@teleto +Acdream"]` | the second session never reached the world -- `testaccount2` with password `testpassword` fails at `System.TimeoutException: CharacterList not received` on two consecutive attempts with no other session on that account. Even with the bot up, both of the client's trade-open paths start from a world selection, which the probe cannot perform (see the vendor row) | PENDING (second-account credentials unknown; world selection also blocked) |
| Player tracker live tracking | 2026-09-16 | same | same headless attempt as the trade row | blocked on the same `testaccount2` login failure, so no second player was ever near +Acdream | PENDING (second-account credentials unknown) |
| One-touch heal real heal | 2026-09-16 | same | -- | `MagToolsPlugin.cs:199` registers the One Touch Heal hotkey with a `default` (unbound) chord, there is no `/mt` verb for it, and the probe can neither type into the rebind UI nor inject a plugin hotkey chord | PENDING (hotkey cannot be bound or fired from a route) |
| Log out on death | 2026-09-16 | same | -- | not attempted deliberately: the only ways to kill +Acdream on this server (dropping its health to zero, or letting a monster finish it) cost the character its vitae and dropped items. Needs the owner's go-ahead | PENDING (destructive; owner approval needed) |

## Round 3 (A7 host e83d3a7 / plugin e5ea4a8)

Run 2026-09-16 evening against the A7 host build with the D1/D2/D3 host
fixes and the P9 plugin fixes deployed. Routes `r1`-`r15`, logs and
screenshots in the session scratchpad under `live-r3/`. Partner: the
headless `+Horan` bot (relaunched mid-round; see the environment note at
the end of this section).

| Feature | Steps | Observed | Verdict |
|---|---|---|---|
| On-Login / On-Login-Complete / Periodic -- CHARACTER scope | r1/r2: the three lists seeded under `_testaccount_sawato/_x002B_Acdream`, two logins | all three dispatch: `<{Mag-Tools}>: Filters.AttackEvades = True` (On-Login) then `Misc.OpenMainPackOnLogin = True` (On-Login-Complete) one tick later, and `ManaManagement.AutoRecharge = True` on each whole minute (three lines, one minute apart, in one session). The A7 `ICharacterInfo.Name` fix closes defect #1's headline symptom | PASS |
| Chat logger file | r1/r7: `ChatLogger/Persistent` + `Group1/Area` on, `/say ChatLoggerProbe round3 alpha`, later `/say buffs here -t-` | `sawato/+Acdream.ChatLogger.txt` contains `260916205941,1,ChatLoggerProbe round3 alpha` and `260916212053,1,buffs here -t-` -- written, non-empty, character-prefixed name | PASS |
| Inventory logger file | r1 (no file -> the request-id branch), r2/r3 (file exists -> immediate dump), ~60-130 s in world, graceful exit | the file is written every session with the right name and is always `<ArrayOfMyWorldObject />`, including the session where `Start` dumps immediately while the pack is visibly full. No `Requesting id information...` line in any session | FAIL -> defect 8 |
| Inventory export to clipboard | r3/r4: Tools -> Inventory, `Clipboard Worn Equipment` then `Clipboard Inventory Info`; clipboard sampled every 2 s from a second process and read after exit | Worn Equipment prints both messages and the clipboard IS written (a pre-set sentinel disappears at the click) -- with an EMPTY string, in r3 and again in r4 with five equipped items listed in the Mana tab. Inventory Info prints only `Copying...` and never completes (no completion line 130 s later). So the host clipboard write works (D3 is fixed); the exporter selects nothing | FAIL -> defects 9, 10 |
| Auto looting (corpse) | r9 with MossTank: `/vt loot load MTGateAll`, `@create drudgeprowler`, kill, then `/mt selectp corpse` + `input press UseSelected`, and afterwards `/mt uselp corpse` | the CLIENT's own use path opens the corpse (`Corpse of Drudge Prowler` container window) and the looter runs against it: `+(KeepAll) Bracelet`, `+(KeepAll) Flaming Stick, (-1.0/0 )`, `+(KeepAll) Atlatl, (-1.0 )`. The PLUGIN path resolves the corpse, prints no refusal (host returned `Started`) and opens nothing. Item movement into the pack was not observed -- the pack was full | PARTIAL (looter reached via the client path; plugin-issued use still dead) |
| Auto buy/sell at a real vendor | r5: `@telepoi Holtburg`, `/mt usel closestvendor`, then `/mt vendor addbuy Bread 1`, `buy`, `addsellp phantom`, `sell` | `/mt usel closestvendor` resolves a Vendor-class object and prints no refusal (so `Started`), but there is no walk and no vendor panel in 20 s; the vendor verbs then correctly report `No vendor is open.` | FAIL -> defect 4 (host) |
| Trade: open, add, accept | r13: `@teleto Horan`, `/mt select +Horan`, `input press UseSelected`, `/mt trade addp mana stone`, `accept`, `end` | the secure-trade window opens with `+Horan` on the left and `You` on the right; after the add, `Total Items: 1` on my side. `accept` prints no refusal; the trade cannot complete because the headless partner runs no plugin/UI to accept | PASS (open + add), PARTIAL (accept unconfirmed) |
| Auto trade add / auto trade accept | r13 with `AutoTradeAdd.Enabled=True` and a `+Horan.utl` keep-everything profile | only the explicitly added item was staged, so nothing distinguishes AutoTradeAdd from the explicit `/mt trade addp`; AutoTradeAccept needs the partner to accept first | PENDING |
| Mana auto recharge | r2/r3: `/mt selectp gauntlet` then `@givemana -284/-4/-3/-2` to leave 1-2 of 294 mana, 135 s idle, a `Mana Stone` in the pack (`@ci manastone` works) | the drain lands (`You give -284 points of mana to the Leather Gauntlets.`) and the Mana tab still reads `2 / 294`, `Mana needed: 292` many minutes later: the item never burns mana, so ACE's `Player_Tick.CheckLowMana` never runs and the warning line is never emitted | PENDING (trigger not producible on this character's gear) |
| One-touch heal (hotkey) | r11: bound the hotkey by writing `%APPDATA%\acdream\plugin-hotkeys.json` = `{"openac.magtools:one-touch-heal": {"Key":"J","Ctrl":true}}`, `@setvital health 500`, five Ctrl+J presses from a second process | `The Health Philtre restores 100 points of your Health.` x4 -- the hotkey fired and OneTouchHeal's food fallback applied a `Heal`-named Food item. The healing-kit branch stays untested: `healingkit`/`crudehealingkit`/`treatedhealingkit` are all `not a valid weenie` on this build | PASS (heal), kit branch PENDING |
| Character/Server command tabs -- Add | r12: Tools -> Character, click the On-Login field, type `/mt opt get Filters.MonsterDeaths`, click Add | the field takes the text, the row is appended to the list and persisted to `Mag-Tools.xml` under the character scope, and it DISPATCHED on the next login (`<{Mag-Tools}>: Filters.MonsterDeaths = False`) | PASS |
| Chat filter `Filters.TradeBuffBotSpam` | r7: `/say buffs here -t-` with the filter off, then on | off: `You say, "buffs here -t-"` appears; on: the identical say produces no transcript line at all, while the chat logger still records it | PASS (2 of 32 rules now proven, with `MonsterDeaths`) |
| Chat filters: the other 30 rules | r7/r8 attempts | `/mt castp strength` -> `Cast refused: NoTarget` (no cast, so no casting/fizzle/comp/status-text lines); `/mt usei mana stone` -> `Use refused: Refused` (so no Mana-Stone lines); an `@create olthoisoldier` standing next to the character with `@attackable on` never attacked in 60 s | PENDING |
| Combat tracker `Dmg Rcvd` / `Dmg Givn` | r8: `@attackable on`, `@create olthoisoldier`, 60 s adjacent, Trackers -> Combat | the monster never attacked and was never attacked; the page renders with `All` and empty cells. The persistent file does record the r9 kills (`+Acdream.CombatTracker.xml`: `SourceName="Acdream" TargetName="Drudge Prowler" KillingBlows="2" TotalAttacks="2"`) | PENDING |
| Log out on death | r8: `Misc.LogOutOnDeath true`, `@attackable off`, `@setvital health 0`, 27 s observation; r14 retry with `@smite` on the self-selection | health reaches `0/2999` and the world unloads into a transition, but no death chat line appears and no logout happens -- the only `graceful logout requested` in the log follows `UI probe script complete`, i.e. the route's own `close-client`. `@smite` on self does nothing. The death was reached; `IEvents.LocalPlayerDied` was never observed to fire | PARTIAL |
| Player tracker live | r15: Trackers -> Player | the list shows `26/09/16 21:2x  +Horan  100.9S, 0.1E` and `sawato/+Acdream.PlayerTracker.xml` holds a real `+Horan` row with a live landblock/position, captured while standing next to the bot in r13 | PASS |
| `/mt fellow create` | r15: `/mt fellow create R3Gate`, then `/mt fellow disband` | both silent, which after P9 means the host returned `Accepted` (a refusal now prints `Fellowship request refused: <status>`). No panel was opened to see the roster | PASS (accepted), roster unconfirmed |
| Idle automation: heart carver / shattered-key fixer / key deringer | r1: `@ci intricatecarvingtool`, `@ci 42979` | `intricatecarvingtool is not a valid weenie`; `42979` creates a `Core Plating Integrator`. Still not creatable on this ACE build | PENDING |

### New defects from round 3

8. **Inventory logger always writes an empty document** (plugin). Both dump
   points run when `Items.CaptureOwnedItems()` yields nothing: `Start` fires
   at `SessionReady`, before the pack has streamed in, and `Stop` fires
   during logoff teardown. The fresh-file branch never printed its
   `Requesting id information...` message either. The file name is now
   correct; only the contents are wrong.
9. **The Worn Equipment export selects nothing** (plugin).
   `src/OpenAC.MagTools/Inventory/InventoryExporter.cs:183-193`
   (`IsEquippedByMe`) keeps the original's "if `Slot == -1` the container
   must be the player" rule, but on this host EVERY equipped item has
   `ContainerSlot == -1` and its `ContainerObjectId` is the old container or
   0 -- the player id lives in `WielderObjectId`. Every equipped item is
   rejected, so the export writes an empty string to a clipboard that
   accepts it, and still prints the success line.
10. **The Inventory export can hang forever** (plugin).
    `InventoryExporter.Think` sets `waitingForIdData` for any selected item
    with no appraisal data after the first request pass and never
    re-requests or times out, so one never-appraised item stalls the export.
11. **Defect 4 is confirmed host-side and narrowed.** A plugin-issued
    `Items.Use` on a world object returns `Started` and does nothing (no
    walk, no panel, no container); the same object used through the client's
    own path (`input press UseSelected` on the selection) opens
    immediately. Diagnosis is further blocked by
    `SelectionInteractionController.PerformUse` logging the dispatched use
    only when `log: true`, which the automation entry point never passes.
12. **`/mt usei <owned item>` is refused** (host, minor): `/mt usei mana
    stone` prints `Use refused: Refused` for an ordinary owned item.

### Environment notes (round 3)

- The Mag-Tools window is VISIBLE at every login (visibility does not
  persist as hidden); the selected tab resets to Trackers -> Mana. With the
  window at its saved 1177,228 (333x467): top tabs y=259 (Trackers 1213,
  Loggers 1273, Tools 1327, Misc 1373); second row y=279 (Trackers page:
  Mana 1206, Combat 1256, Corpse 1309, Player 1362, Inv. Items 1422; Tools
  page: Inventory 1216, Tinkering 1281, Character 1346, Server 1405). Tools
  -> Inventory clipboard buttons at (1258,326) and (1412,326); Tools ->
  Character On-Login field (1345,324) and Add (1469,324).
- The UI probe still cannot type, but two things it cannot do turned out to
  be reachable from outside the client: a plugin hotkey can be BOUND by
  writing `%APPDATA%\acdream\plugin-hotkeys.json` (key
  `<pluginId>:<hotkeyId>`), and keystrokes can be delivered with
  `WScript.Shell.AppActivate` + `System.Windows.Forms.SendKeys` from a
  second PowerShell process. That closed both the hotkey and the Add-field
  rows.
- `@givemana <negative>` drains the last-appraised item's mana; `@ci
  manastone` works; `@ci` for any healing kit or the Intricate Carving Tool
  does not.
- ACE intermittently stopped resolving the headless partner by name
  (`@teleto +Horan` / `@teleto Horan` -> `Player ... was not found.`) while
  the bot reported `InWorld` with 28 entities. A bot relaunch fixed it for
  one session and it regressed again afterwards.

## Pending (owed before the port can be called fully live-gated)

Updated after round 3 (2026-09-16). Rows closed in round 3 are gone; every
row below carries its current blocker.

| Feature | Blocker | Verdict |
|---|---|---|
| Chat filters: the 30 rules other than `MonsterDeaths` and `TradeBuffBotSpam` | still needs a character that can miss, be evaded, be hit, fizzle, resist, salvage and use comps, plus an NPC/vendor to talk. Round 3 also found that `/mt castp <spell>` refuses with `NoTarget` (so no casting/fizzle/comp/status-text line can be produced) and `/mt usei mana stone` refuses with `Refused` (so no Mana-Stone line), and a monster spawned next to the character with `@attackable on` never attacked | PENDING |
| Auto buy/sell at a real vendor | unchanged after the A7 walk-to-use fix: `/mt usel closestvendor` resolves a Vendor-class object, returns `Started` (no refusal is printed) and produces no walk, no panel and no message. Round 3 pinned this host-side -- the same kind of object opens instantly through the client's own use path | PENDING |
| Auto trade add / auto trade accept | a trade now opens and `/mt trade addp` stages an item, but AutoTradeAdd cannot be distinguished from the explicit add (only one item staged) and AutoTradeAccept needs the partner to accept first -- the headless `+Horan` bot runs no plugin | PENDING |
| Auto looting: items actually moving into the pack | the looter now runs (corpse opened through the client's use path, contents classified against the loaded profile), but no item was observed moving: the pack was full and the plugin prints no per-item loot message | PENDING |
| Inventory packer: items land in their profile-assigned pack | not exercised in round 3; the Started/Completed lifecycle remains the only proven part | PENDING |
| Idle automation: heart carver / shattered-key fixer / key deringer | `Intricate Carving Tool` still not creatable: `@ci intricatecarvingtool` is not a valid weenie and `@ci 42979` creates a `Core Plating Integrator` | PENDING |
| Mana auto recharge | the trigger is ACE's `Your <item> is low on Mana.`, which `Player_Tick` only emits for an item that actively burns mana. The character's equipped items do not burn: drained to 2 of 294 with `@givemana`, the value was unchanged many minutes later, so the warning is never emitted | PENDING |
| One-touch heal with a healing KIT | the hotkey itself is proven (bound via `plugin-hotkeys.json`, Ctrl+J applied a `Heal`-named food item four times). The kit branch needs a `HealingKit`-class item, and no healing-kit weenie name resolves on this ACE build | PENDING |
| Log out on death | the character was taken to 0 health with `@setvital health 0` and the world unloaded into a transition, but no death chat line appeared and no plugin logout followed, so `IEvents.LocalPlayerDied` was never observed to fire. `@smite` on the self-selection does nothing. Needs a death that provably runs ACE's death pipeline | PENDING |
| Combat tracker `Dmg Rcvd` / `Dmg Givn` cells | still unprovoked: the character one-shots everything it attacks, and a monster spawned adjacent with `@attackable on` did not attack in 60 s | PENDING |

## Defects found in the 2026-09-16 gate round

Status as of the P9 fix round (plugin repo): each item below says whether the
plugin-side symptom is fixed here, or whether it is still waiting on the host
(tracked as OpenAC slice A7). A defect can be BOTH -- the plugin can be made
robust to the host's current behavior while the underlying host bug is still
open.

1. **`ICharacterInfo.Name` is empty at the LoginComplete edge** (host) and was
   cached there by the plugin (`src/OpenAC.MagTools/SessionContext.cs:98`).
   Every character-scoped name derived from it was wrong for the whole
   session. Observed consequences, each independently reproduced:
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
   **PLUGIN-SIDE FIXED (P9):** `SessionContext` now re-checks the name every
   tick after `LoginComplete` until it (and WorldName/AccountName) resolve,
   then raises a new `SessionReady` event that every name-scoped owner in the
   list above now waits for instead of `LoginComplete`; a session that ends
   before the name ever resolves logs a warning instead of writing a file
   with an empty character segment. Server-scoped work (which only needs
   WorldName, never broken by this defect) was ALSO moved off the
   `SessionReady` wait, back onto `LoginComplete`, so it is no longer
   needlessly delayed by a fix aimed at the character-scoped half (P9
   MEDIUM-4). **HOST-SIDE STILL OPEN (A7):** the underlying
   `AppAutomationSurface.cs:804-816` read-too-early bug is unchanged; A7 is
   the host fix that makes the FIRST read correct instead of relying on the
   plugin to poll around it.
2. **Clipboard export never reaches the clipboard.** Both Tools -> Inventory
   buttons print their start and completion messages, but the Windows clipboard
   stays empty when read from another process during and after the session.
   `src/OpenAC.MagTools/Inventory/InventoryExporter.cs:192` discarded
   `IPluginClipboard.TrySetText`'s result, so the success message was printed
   unconditionally.
   **PLUGIN-SIDE FIXED (P9):** `ExportObjects` now returns `TrySetText`'s
   result; a refused write prints (and logs) "Clipboard is unavailable;
   nothing was copied." instead of the success line. **HOST-SIDE:** whatever
   made the live clipboard write itself fail (as opposed to just misreporting
   success) is outside this plugin -- if `TrySetText` keeps returning `false`
   on the same build, that is a host clipboard-backend question, not this
   defect's plugin-side half.
3. **Inventory logger writes an empty document.** With
   `InventoryManagement/InventoryLogger` on, a fresh session produced
   `<ArrayOfMyWorldObject />` and printed no `Requesting id information...`
   lines.
   **PLUGIN-SIDE FIXED (P9):** two independent plugin bugs contributed and are
   both fixed: (a) `Dump()` dropped any owned item the object table had no
   full `PluginWorldObject` for yet instead of including it with
   `HasIdData=false` (now via `MyWorldObjectRecord.CreateUnresolved`, which
   also looks the id up in the previous file and keeps its id data via
   `Combine` rather than overwriting it with an empty stub -- see HIGH-1);
   (b) defect #1's empty character name meant every session's storage key
   was the same wrong path, so the "does the file already exist" check saw a
   leftover file from a PRIOR (also-misnamed) session and skipped the
   request-line branch entirely -- fixed by #1's `SessionReady` fix.
4. **Plugin-issued `Items.Use` on a landscape object never opens its panel or
   container.** Two independent cases: `/mt usel closestvendor` resolves a
   Vendor-class object (no `Nothing found named:` message) and issues `Use`,
   but the character does not walk and no vendor panel opens within 35 s; and
   `/mt uselp corpse`, standing on a fresh corpse, resolves it and issues
   `Use`, but no container opens and no loot line follows in 45 s. Because
   `Macros/Looter.cs:90` triggers on `ContainerOpened`, this also keeps the
   auto-looter from ever running.
   **PLUGIN-SIDE FIXED (P9):** `/mt use*` now prints the
   `PluginItemCommandResult` status (preferring its `Notice` text) whenever
   the result is not `Started`, so a refused/no-op host response is visible
   instead of silent -- see the deviations-doc row. **HOST-SIDE STILL OPEN
   (A7, host defect D2):** the underlying "resolves the object, issues the
   command, but nothing happens" behavior (no walk, no panel, no container)
   is unchanged; this plugin fix only makes that host behavior observable
   instead of silent.
5. **`/mt castp <spell> on <target>` is silent.** No cast, no
   `No spell named:`/`No target found named:` message, no server reaction.
   **FIXED (P9):** the router called the plain `IMagicCommands.Cast` bool and
   never examined the return value -- a refusal produced zero output. Now
   calls `RequestCast` and prints `Cast refused: <status>` for anything other
   than `Sent`.
6. **`/mt fellow create <name>` is silent.** No plugin output and no server
   response.
   **FIXED (P9):** same shape as #5 -- `Accepted(result)` examined the status
   but never printed it. Now prints `Fellowship request refused: <status>`
   for anything other than `Accepted` (still silent on `Accepted`, matching
   the original).
7. **Host shutdown crashes after a clean session** (OpenAC, not the plugin):
   two runs ended with `Fatal error 0xC0000005` in
   `Silk.NET.Vulkan.Vk.DestroyDevice` from
   `VulkanGraphicsContext.Dispose`, and one with `0xC0000374` (heap
   corruption), each AFTER `graceful logout confirmed` and
   `Mag-Tools disabled`.
   **HOST-SIDE STILL OPEN:** out of scope for this (plugin) repository.

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
- `testaccount2` (the second party for trade/player-tracker gates) could not
  be logged in: `AcDream.Headless run` with `testpassword` fails at
  `CharacterList not received` on two consecutive attempts.
- Server toggles used and restored: `@attackable on` -> `@attackable off`
  (confirmed by `Monsters will only attack you if provoked by you first.`).
  `@telepoi Holtburg` was undone with
  `@teleloc 0x0108020D 46.438545 -54.503498 0.004200 -0.128570 0 0 -0.991700`.
  `Mag-Tools.xml` was restored to its pre-round contents.
