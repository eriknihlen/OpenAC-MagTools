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
   **Fixed in `45239ca` (slice P10).** `Start()` now waits for the first
   `Tick` on which `CaptureOwnedItems()` is non-empty before deciding the
   fresh-file-vs-existing-file branch (or runs immediately if the pack is
   already populated at `Start()` time); `Stop()` dumps from the last
   non-empty snapshot this instance actually captured instead of a fresh,
   typically-empty-by-then capture.
   **Round-4 re-gate: HALF CLOSED.** The file is written non-empty (195
   records, 91,092 bytes) and the fresh-file branch printed `Requesting id
   information for all armor/weapon inventory. This will take a few
   minutes...`. Remaining symptom, tracked in the Pending table: every record
   is `HasIdData=false` in every session, so the log carries names only.
9. **The Worn Equipment export selects nothing** (plugin).
   `src/OpenAC.MagTools/Inventory/InventoryExporter.cs:183-193`
   (`IsEquippedByMe`) keeps the original's "if `Slot == -1` the container
   must be the player" rule, but on this host EVERY equipped item has
   `ContainerSlot == -1` and its `ContainerObjectId` is the old container or
   0 -- the player id lives in `WielderObjectId`. Every equipped item is
   rejected, so the export writes an empty string to a clipboard that
   accepts it, and still prints the success line.
   **Fixed in `6489985` (slice P10).** `IsEquippedByMe` now checks
   `WielderObjectId == Character.ObjectId` instead of the container/slot
   rule; see the `docs/deviations.md` row for this class.
   **Round-4 re-gate: CLOSED.** 566 characters, five equipped items, weapon
   last, read from the clipboard by a second process.
10. **The Inventory export can hang forever** (plugin).
    `InventoryExporter.Think` sets `waitingForIdData` for any selected item
    with no appraisal data after the first request pass and never
    re-requests or times out, so one never-appraised item stalls the export.
    **Fixed in `9c1cb35` (slice P10).** `Think()` now re-requests any
    still-unappraised item every 5 seconds, up to 3 retries, then exports
    with what is available and reports the count of items missing id data
    instead of waiting forever; see the `docs/deviations.md` row for this
    class (no original retry behavior was available to port faithfully).
    **Round-4 re-gate: CLOSED.** The export completed and printed `40 item(s)
    never received identification data and were exported without it.` before
    the success line, and wrote 3,335 characters / 191 lines.
11. **Defect 4 is confirmed host-side and narrowed.** A plugin-issued
    `Items.Use` on a world object returns `Started` and does nothing (no
    walk, no panel, no container); the same object used through the client's
    own path (`input press UseSelected` on the selection) opens
    immediately. Diagnosis is further blocked by
    `SelectionInteractionController.PerformUse` logging the dispatched use
    only when `log: true`, which the automation entry point never passes.
    **Round-4 re-gate: CLOSED by the A8 host.** A plugin-issued `Items.Use`
    opened a vendor (twice, walking through a shut shop door first) and a
    corpse, and the auto-looter ran against the corpse the plugin opened.
12. **`/mt usei <owned item>` is refused** (host, minor): `/mt usei mana
    stone` prints `Use refused: Refused` for an ordinary owned item.
    **Round-4 re-gate: CLOSED by the A8 host.** `/mt usei mana stone` now
    prints `Use refused: This item requires a target; call Apply(objectId,
    targetObjectId) instead.`

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
- The headless partner silently drops back to CHARACTER SELECT about five
  minutes after login, and its own telemetry does not notice. Status file
  (`horan-status.jsonl`): `enteredWorld` at 19:24:48 UTC, then a bare
  `characterList` at 19:29:57 UTC with no `enteredWorld` after it -- while
  the host's periodic `resources` samples kept reporting
  `sessions.inWorldCount: 1` and lifecycle `running`/`InWorld` right up to
  the end, and the process later exited with code 1. That, not a name-lookup
  quirk, is why `@teleto +Horan` / `@teleto Horan` answered
  `Player ... was not found.` in r6 and again in r15: the bot was out of
  world both times. It also means r15's `+Horan` player-tracker row is the
  persisted one captured live in r13, inside the five-minute window. Plan a
  partner gate to run within ~4 minutes of the bot's `enteredWorld` event,
  and check that event -- not `inWorldCount` -- before trusting it.

## Round 4 (A8 host `f868960` / plugin `40af8d1`)

Run 2026-09-17 against the A8 host build (`OpenAC/.worktrees/magtools-api-a8`,
`AcDream.App.exe` built 01:45) with the plugin DLL deployed at 01:45 to
`%LOCALAPPDATA%\acdream\plugins\OpenAC.MagTools`. Routes `r1`-`r7`, logs and
screenshots in the session scratchpad under `live-r4/`.

| # | Feature | Steps | Observed | Verdict |
|---|---|---|---|---|
| 1 | Auto buy/sell at a real vendor | r1/r2: `@telepoi Holtburg`, `/mt uselp door`, `/mt usel closestvendor`; then `/mt vendor addbuy Bread 1`, `buy`, `addsellp taper`, `sell` (r1) and `addbuyp a`, `addbuyp e`, `buy`, `addsellp taper`, `sell` (r2) | The vendor OPENS from the plugin path, twice: the retail browse panel (Items/Buying/Selling, Buy, Add to List) is up and `Fispur Ansel the Grocer tells you, "Welcome! What's your pleasure today?"` (p04-vendor, q02-vendor). AutoBuySell runs at the open: `<{Mag-Tools}>: AutoBuySell: Nothing to Buy` / `AutoBuySell: Nothing to Sell`. `/mt vendor addbuy Bread 1` answers `No vendor item found named: Bread` (this grocer's list has one entry). `addbuyp a`/`addbuyp e`, `buy`, `addsellp taper`, `sell` all printed nothing and produced no purchase/sale line and no pyreal change (21,629p before and after) | PARTIAL -- vendor open + AutoBuySell PASS (defect 4 closed); an actual buy or sell was not observed |
| 2 | Auto looting through the plugin path | r3 with MossTank: `/vt loot load MTGateAll`, `@create drudgeprowler`, `/mt attack_melee closest` x3, `/mt uselp corpse` | The PLUGIN's own use opens the corpse and the looter runs and MOVES items: `+(KeepAll) Drudge Head`, `+(KeepAll) Pyreal`, `+(KeepAll) Partizan, (-1.0/0 )`, `+(KeepAll) Amulet`, `+(KeepAll) Yumi, (-1.0 )` (s02-loot/s03-loot2), and the pyreal counter went `21629` -> `21633` in the same screenshot, i.e. loot really landed in the pack. Chest: `@create chest` spawned it in a neighbouring cell (`0x010801F8` vs the player's `0x0108020E`); `/mt uselp chest` printed nothing and opened nothing | PASS (corpse, including items into the pack); chest not exercised (spawn landed out of reach) |
| 3 | Corpse tracker rows | r3: Trackers -> Corpse -> Tracked Corpses after the kill | One row, `Thu 00:38  Corpse of Drudge Prowler   21.5S, 1.8W` (s04-corpse), and `sawato/+Acdream.CorpseTracker.xml` holds it with `Opened="True"` plus the round's other corpses | PASS |
| 4 | Inventory logger file | r1 with no prior file, r2/r3 with the file present | r1 (fresh file) printed `<{Mag-Tools}>: Requesting id information for all armor/weapon inventory. This will take a few minutes...` (p00-login) and wrote a NON-EMPTY `sawato/+Acdream.Inventory.xml`: 91,092 bytes, 195 `<MyWorldObject>` records with real names (`Acid Katar`, `Acid Phyntos Wasp Essence (80)`, ...). Defect 8's empty `<ArrayOfMyWorldObject />` is gone. But every record in every session is `<HasIdData>false</HasIdData>` (195/195 after r1 AND after r2), so no id data is ever captured or retained | PARTIAL -- non-empty PASS; id-data retention FAIL |
| 5 | Worn-equipment + Inventory Info clipboard export | r2: Tools -> Inventory, `Clipboard Worn Equipment`; r3: `Clipboard Inventory Info` alone. Clipboard sampled every 2 s from a second PowerShell process | Worn Equipment writes 566 chars, five equipped items, weapon last: `Leather Leather Helm, AL 223, Craft 5` / `Leather Leather Gauntlets, AL 163, Diff 52, Craft 4` / `Greater Amuli Shadow Coat, AL 190` / `Leather Leather Greaves, AL 276, Missile Defense 209 to Activate, Diff 174, Craft 8` / `Decapitator's Blade (Two Handed Combat), Heroic Destroyer Set, SlashRend, 60.75-75, +38%a, ...` (defect 9 closed). Inventory Info completes and writes 3,335 chars / 191 lines (`Aetheria`, `Aetheria Mana Stone`, `Turpeth`, ... ) and prints `<{Mag-Tools}>: 40 item(s) never received identification data and were exported without it.` then `All inventory item info has been copied to the clipboard.` (s00-invinfo / s03-loot2) -- defect 10 closed | PASS (both exports non-empty; the missing-id line appears). "Equipped first" is not observable: the two buttons partition the sets (`ExportGroups.WornEquipment` vs `ExportGroups.Inventory`), so no single export mixes them |
| 6 | Combat tracker `Dmg Rcvd` / `Dmg Givn` + combat chat filters | r4: `@attackable on`, Tusker Guard, five `/mt attack_melee closest`, Combat tab; then `Filters.AttackEvades` off -> fight -> on -> fight. r6: `@attackable on`, `@create tuskerguard 8` (all adjacent), two kills, `@setvital health 200`, 80 s standing among the six survivors | Rows and counters populate: `All 3`, `Tusker Guard 1`, `Creeper Mosswart 1`, `Mosswart Feeder 1`, `Attacks 3 (100%)` (t04-combat-tab); r6 `All 2`, `Tusker Guard 2`, `Attacks 2 (100%)` (v08-combat). `Dmg Rcvd`/`Dmg Givn` stayed blank in both: the character one-shots every spawn (`You cleave Tusker Guard in twain!`, `Tusker Guard is utterly destroyed by your attack!`) so no damage line is ever emitted, and SIX hostile Tusker Guards standing on top of the character at 204/72999 health with `Monsters will attack you normally.` never landed a single hit in 80 s (health drifted 204 -> 206 by regen). The filter toggles were accepted (`<{Mag-Tools}>: Set Filters.AttackEvades = False` / `= True`) but no evade/resist line was ever produced to suppress | PENDING -- the trigger is not producible on this character/server; no evidence the cells or the filters are wrong |
| 7 | Log out on death | r4: `Misc.LogOutOnDeath` True, `@attackable on`, `@setvital health 40`, adjacent Tusker Guard, 40 s; r6: health 200 surrounded by six hostile Tusker Guards for 80 s | The character never died -- nothing attacks it (see row 6). No death, so no `LocalPlayerDied`, so no logout to observe | PENDING -- same blocker as row 6 (a real server-driven kill could not be provoked) |
| 8 | Auto trade add / auto trade accept | r5 with the headless `+Horan` bot running the plugin (`horan.json` `plugins: ["openac.magtools"]`, `enteredWorld` confirmed in its status file), `AutoTradeAccept/Enabled=True` + whitelist `\+Acdream` and `AutoTradeAdd/Enabled=True` seeded in the shared `Mag-Tools.xml`: `@teleto +Horan`, `/mt select +Horan`, `input press UseSelected`; then `/mt trade end`, `AutoTradeAdd` off, re-open, `/mt trade addp drudge head`, `/mt trade accept` | **AutoTradeAdd PASS**: the trade opens and the `+Horan.utl` keep-everything profile stages the pack unattended -- `+Horan  Total Items: 0` / `You  Total Items: 134` with the grid full (u02/u03), plus the server's `You cannot trade that!` for the refused ones. **AutoTradeAccept FAIL**: on the second trade `/mt trade addp drudge head` staged exactly one item (`You Total Items: 1`, the Drudge Head icon) and `/mt trade accept` was sent silently, but 18 s later the window is still open with `+Horan Total Items: 0` and no completion (u06/u07); the item never left the pack. The bot's log shows the plugin loaded (`plugin loaded: openac.magtools`) and nothing trade-related at all. r7 repeated it with a freshly relaunched bot (`enteredWorld` confirmed, route started inside the five-minute window): same result 27 s after the accept (w04/w05) | PARTIAL -- AutoTradeAdd PASS, AutoTradeAccept FAIL (defect 13, reproduced twice) |
| 9 | `/mt usei <owned item>` notice | r1: `@ci manastone`, `/mt usei mana stone` | `<{Mag-Tools}>: Use refused: This item requires a target; call Apply(objectId, targetObjectId) instead.` (p01-usei / p04-vendor) -- the specific notice, replacing round 3's bare `Use refused: Refused` | PASS (defect 12 closed) |
| 10 | Character-scoped commands + chat logger on the new build | r1/r5: `_testaccount_sawato/_x002B_Acdream/OnLoginCommands` = `/mt opt get Filters.AttackEvades`, and `ChatLogger/Persistent` + `Group1/Area` on with `/say ChatLoggerProbe round4 alpha` | On-Login dispatches: `<{Mag-Tools}>: Filters.AttackEvades = True` at the top of r5's transcript (u02/u03). Chat log file `sawato/+Acdream.ChatLogger.txt` gained `260917015017,1,ChatLoggerProbe round4 alpha` | PASS |

### Defects and re-gate verdicts (round 4)

Re-gate verdicts for the round-3 defects, plus what is new.

- **Defect 8 (inventory logger writes an empty document) -- FIXED (8a), and
  8b (every record `HasIdData=false`) FIXED plugin-side, P11, root cause
  corrected P12.** The file is written non-empty (195 records, 91 KB) and
  the fresh-file branch prints its `Requesting id information...` line
  (8a). Round 4's remaining symptom -- every record `HasIdData=false` in
  every session, even after a full id sweep and even though the same
  session's clipboard exporter saw real appraisal data for 151/191 items
  via its own live `TryGet` poll -- was root-caused to `InventoryLogger`'s
  only real-data dump being gated on an all-or-nothing "every ident-worthy
  item now has id data" completion latch driven by the host's
  `IdentReceived` `ObjectChanged` event, combined with a SEPARATE bug the
  P11 round missed: the fresh-file startup pass called `Identify` for every
  missing item in one tight `foreach`, but `AppAutomationSurface.Identify`'s
  `CanBeginRequest` gate (`InventoryTransactionState.CanBeginRequest =>
  _busyCount == 0 && ...`, a client-wide single-in-flight gate shared with
  every other inventory transaction) refuses -- `Busy`, with no call to the
  underlying appraisal request at all -- every attempt after the first, so
  only ONE of N items in a startup batch could ever be identified at all;
  the other N-1 were silently refused, yet the old code still recorded all
  of them into `_requestedIds`, permanently poisoning them against retry.
  (P11's own explanation -- that a later Automation-origin request
  "displaces" an earlier one still awaiting its response in
  `RuntimeInteractionTransactionState.TryRequestAppraisal` -- was wrong;
  that guard exists but only fires for a User-vs-Automation origin
  conflict, and `CanBeginRequest` refuses the call before that guard is
  ever reached.) With almost every item's identify request never actually
  sent, the completion latch could not trip, nothing real ever reached
  Storage before logoff, and `Stop()`'s post-teardown dump fell back to the
  empty "unresolved" shape for every item. Fixed at P12 by pacing every
  identify call site (`RunStartupCapture`, `OnObjectChanged`'s per-item
  path, `Dump`'s own re-identify pass) through a bounded one-at-a-time
  queue (`EnqueueIdentify`/`PumpIdentifyQueue`), recording an id into
  `_requestedIds` only once the host confirms it was actually accepted, and
  retaining P11's partial-progress persistence (now gated on the
  identified-id SET rather than a count, LOW-1). Row 4.
- **Defect 9 (Worn Equipment export selects nothing) -- FIXED, verified live.**
  566 chars, five equipped items, via `WielderObjectId`.
- **Defect 10 (Inventory export can hang forever) -- FIXED, verified live.**
  The export completed and reported `40 item(s) never received identification
  data and were exported without it.`
- **Defect 11 (plugin-issued `Items.Use` on a world object does nothing) --
  FIXED, verified live.** A plugin `Use` opened a vendor (twice, with a walk
  through a closed shop door first) and a corpse, and the looter ran against
  the corpse it opened.
- **Defect 12 (`/mt usei <owned item>` refused with a bare `Refused`) --
  FIXED, verified live.** The specific notice is printed.
- **Defect 13 (NEW) -- the partner never accepts, so a trade never completes.
  ROOT CAUSE CONFIRMED, HOST-SIDE, plugin-side re-review (P11).**
  `+Horan` running this plugin with `AutoTradeAccept/Enabled=True` and a
  whitelist matching `+Acdream` did not accept after `+Acdream` accepted; the
  trade stayed open with the partner at 0 items, twice (r5 and r7, the second
  with a freshly relaunched bot). Reading `AcDream.Headless.Plugins.
  HeadlessAutomationSurface` under `OpenAcRoot` (magtools-api-a8 snapshot)
  confirms the cause: that surface overrides only
  `Chat`/`Login`/`Dialogs`/`Trade`/`Vendor` on `IAutomationSurface` --
  `Character`, `Items`, `Objects`, `Loot`, `Navigation`, etc. all fall
  through to the interface's own default, `NoOpAutomationSurface.Instance`,
  whose `IWorldObjectAutomation.TryGet` unconditionally returns `false`.
  `AutoTradeAccept.OnPartnerAccepted`'s `Objects.TryGet(partnerObjectId, ...)`
  (`src/OpenAC.MagTools/Macros/AutoTradeAccept.cs`) therefore can NEVER
  resolve the partner's name on a headless bot, so it always bails before
  the whitelist match or `Trade.Accept()` -- independent of whitelist
  content, rate-limit state, or timing. This is a DIFFERENT (broader) gap
  than the "empty `Character.Name` at `SessionReady`" pattern seen elsewhere
  in this port: `AutoTradeAccept` never reads `Character` at all, and no
  headless plugin has a working world-object lookup yet, not just this one.
  The plugin's own whitelist match and `Trade.Accept()` call are correct and
  need no change once the host wires a real `Objects` implementation into
  `HeadlessAutomationSurface` (the same way `Trade`/`Vendor` already got
  one). A characterization test
  (`AutoTradeAcceptTests.DoesNotAcceptWhenThePartnerCannotBeResolved`) locks
  in that this fails safely (no exception, no accept) rather than crashing --
  this is UNCHANGED, already-correct behavior being pinned down, not a
  fail-first fix: it never failed against the pre-existing code (MEDIUM-1,
  P12 review corrects an earlier overly-broad claim that every new test in
  this round was red on the base commit; this one specifically was not, by
  design, since there was nothing to fix on the plugin side). Out of scope
  for this plugin repo to fix; needs an OpenAC-side change to
  `HeadlessAutomationSurface`.
- **Defect 14 (NEW, cosmetic/honesty) -- a vendor buy/sell that does nothing is
  silent. 14a (honesty) FIXED plugin-side, P11.** `/mt vendor addbuyp a` +
  `buy` and `addsellp taper` + `sell` printed nothing and changed nothing;
  only a name miss (`No vendor item found named: Bread`) was ever reported,
  so a staged-but-unfulfilled buy/sell was indistinguishable from success.
  Every `/mt vendor` verb and `AutoBuySell`'s own `AddToBuyList`/
  `AddToSellList`/`BuyAll`/`SellAll` calls discarded the host's
  `PluginVendorCommandResult` down to a bare bool -- a host-level refusal
  (Busy, InvalidItem, NotOpen, Unavailable) printed nothing at all, and
  `AutoBuySell` additionally kept advancing its Buying/Selling phase on a
  refusal, wedging that vendor visit's automation while waiting forever for
  a `TransactionCompleted` a refused command never sends; a server-rejected
  (`Success == false`) completion was also treated identically to a real
  one. All four now report through chat, matching `/mt use*`'s existing
  refusal-reporting shape. 14b (an actual completed buy/sell) remains
  UNPROVEN -- confirming that needs a live vendor with known stock and
  buy-list items, out of scope for this repo's automated tests. Row 1.
- **Environment note (not a plugin defect):** with MossTank in the session
  (r5) its own recharger destroyed a piece of gear --
  `The Mana Stone drains 882 points of mana from the Leather Gauntlets.` /
  `The Leather Gauntlets is destroyed.` Run Mag-Tools-only sessions unless the
  loot classifier is needed.
- **Environment note:** `@create <weenie>` frequently spawns into a
  NEIGHBOURING cell (`0x010801F8`/`0x010801F3` while the player is in
  `0x0108020E`), which is what made the chest row and two combat attempts
  unreachable. `@create <weenie> 8` did spawn all eight adjacent.

### Restored checklist (round 4)

- [x] `@attackable off` -- issued at the end of round 4 (route r7).
- [x] Home position `@teleloc 0x0108020D 46.438545 -54.503498 0.004200 -0.128570 0 0 -0.991700` -- r1, r2 and r7 each ended with it; r3-r6 never left home (w06-home shows the return in progress).
- [x] `Mag-Tools.xml` restored from `$S/Mag-Tools.backup.xml`.
- [x] Headless `+Horan` bot stopped.
- [x] `%LOCALAPPDATA%\acdream\vtank\+Horan.utl` left as found (keep-everything).

## Round 5 (A9 host `9c26778` / plugin `7fc327a`) -- closing round

Host: OpenAC snapshot worktree `magtools-api-a9`, head `9c26778`; Release
`AcDream.App.exe` and `acdream-headless.exe` both built 2026-09-17 04:08.
Plugin: `7fc327a`, deployed DLL written 2026-09-17 04:09 (the brief's "after
07:30" is a timezone slip -- the deployed binary is from this session's build,
two minutes before the first route). Both heads verified before the first
launch; the plugin repo working tree was clean.

Routes, logs, screenshots and crops live in the session scratchpad under
`live-r5/` (`r1`..`r9`, `r7b`..`r7f`, `launch-r*.log`,
`artifacts1..14/screenshots`).

| # | Row | Steps | Observed | Verdict |
|---|---|---|---|---|
| 1 | Inventory logger id data | r1: backed up and deleted `sawato/+Acdream.Inventory.xml`, seeded `InventoryManagement/InventoryLogger=True`, logged in, stayed ~4.5 min, graceful close. r2/r3: two further sessions with the file present | Fresh-file branch printed `<{Mag-Tools}>: Requesting id information for all armor/weapon inventory. This will take a few minutes...` (a01-usei). The file was written with 198 records, of which **15 carry `<HasIdData>true</HasIdData>` with real data** -- `Acid Katar` (IntValues `19=159` value, `5=135` burden, `47=1`, `45=32`; DoubleValues `29=1.01`, `149=1.0149999856948853`; StringValues `16`/`1` = long description + name) and `Covenant Sollerets` (IntValues `28=215` AL, `19=10075` value, `5=268` burden, `160=205`, `106=316`, `107=1634`; BoolValues `100=true`; DoubleValues `5=-0.0555555559694767`). 60 of the 198 records are ident-worthy by the port's own `ObjectClassNeedsIdent` rule, so **15/60 (25%) were identified in one ~4.5-minute session** -- about one item per 20 s, not the "most identifiable armor/weapons" the fix aimed at. Round 4's "every record false" symptom is gone. **Second session: no re-identify storm** -- b00-login shows the login transcript with no `Requesting id information...` line at all (existing-file branch). **But retention FAILED:** the file after r2 holds only 2 `HasIdData=true` records, and every one of the 13 lost records is present in both files under the *same* `Id`+`ObjectClass` key (`Decapitator's Blade`, `Covenant Sollerets`, `Leather Helm`, `Leather Greaves`, `Greater Amuli Shadow Coat`, `Pathwarden Plate Hauberk`, `Pathwarden Robe`, `Amulet`, `Bracelet`, `Atlatl`, `Flaming Stick`, `Partizan`, `Acid Zombie Essence (50)`), each downgraded to `false` with an empty value block. No `Inventory file is corrupt.` message was printed, so the previous file parsed. Still 2 after r3 | **PARTIAL** -- id data is captured and persisted (8b closed), the request line and the no-storm second session are correct; **carry-over across sessions regresses (new defect 15)** and throughput is low |
| 2 | Vendor buy / sell, an actual transaction | r3: `@telepoi Holtburg`, `/mt uselp door`, `/mt usel closestvendor`, `dump`, `/mt vendor addbuyp a`, `buy`, `/mt vendor addsellp taper`, `sell`. r9 (retry, MossTank loaded): the same open with a `Fispur Ansel the Grocer.utl` profile (KeepUpTo 5 on `Pack`, Sell on `Prismatic Taper`) so AutoBuySell has something to pick | Vendor opens from the plugin path: `Fispur Ansel the Grocer tells you, "Welcome! What's your pleasure today?"` (c05-buy). The grocer's stock is a single item, **`Pack`, `costs 88p (you have 21,678p)`** (c03-vendor crop). `addbuyp a` matched it and `buy` produced the new failure line **`<{Mag-Tools}>: Vendor transaction failed.`**; `addsellp taper` + `sell` produced a second identical line (c06-sell). Pyreals are **21,678p before and 21,678p after both** -- nothing moved. The host log records `[B-Drag] InventoryServerSaveFailed guid=0x5000000A err=0x0 rolledBack=False` immediately after each, so the commands reached the wire and ACE rejected them. r9 with the vendor profile: `<{Mag-Tools}>: AutoBuySell: Nothing to Buy` **and no "Nothing to Sell"** -- AutoBuySell picked a sell item from the profile unattended, issued `AddToSellList`+`SellAll`, and reported `Vendor transaction failed.`; it then went quiet for the remaining 30 s instead of re-issuing, i.e. the P12 deactivate-on-failed-completion behaviour holds | **PARTIAL** -- defect 14a (a silent no-op buy/sell) is CLOSED and verified live, AutoBuySell drives a profile-picked transaction and deactivates on failure; **14b (a completed purchase or sale) still unproven -- ACE rejects both** |
| 3 | AutoTradeAccept on the closing builds | Five attempts (r7, r7c, r7d, r7e, r7f) against a freshly relaunched headless `+Horan` bot each time, `enteredWorld` confirmed in its status file before each launch, `AutoTradeAccept/Enabled=True` + whitelist `\+Acdream` seeded in the shared `Mag-Tools.xml`; `@teleto +Horan`, `/mt select +Horan`, `input press UseSelected`, `/mt trade addp <item>`, `/mt trade accept`, with the post-teleport wait stretched from 26 s to 120 s across attempts | The secure-trade window opens every time from the plugin path -- `+Horan  Total Items: 0` / `You  Total Items: 0` with the bot's avatar in front of the character and `Server population: 1` (g02, h04, k04, n05). **Nothing could ever be staged.** The blocker is the harness, not the feature: after `@teleto +Horan` the graphical client shows a flat white loading screen for **over 2.5 minutes** (still white at n03, 165 s after the teleport), while the probe's `wait world-visible` returns almost immediately and every subsequent `command` executes into that dead window. r7d proves the commands really do run there -- `/mt trade addp lockpick` answered `<{Mag-Tools}>: No inventory item found named: lockpick` (k04). With a name that does resolve (`taper`, r7e/r7f) the add is silent and the grid still reads `Total Items: 0`, and the accept then rides an empty trade. Because nothing was staged and `+Acdream` never had a real accept to answer, the bot's `AutoTradeAccept` was never given the chance to fire; its log shows the plugin loaded and nothing trade-related | **PENDING** -- five attempts, blocked on the harness/environment (post-`@teleto` load time), not on evidence against the feature. Needs a route that waits on the *destination* world being drawn (or a partner that comes to the character instead of the character teleporting) |
| 4 | Tinkering auto-confirm | r8: `Tinkering/AutoClickYes=True` seeded, `@ci materialiron` (ACE weenie 20986 `Salvaged Iron`), then `/mt useip salvaged iron on acid katar` twice | The salvage is created (`"+Acdream has created Salvaged Iron (0x8002108C) in their inventory."`) and the plugin's `Items.Apply(source, target)` route reaches the server -- ACE answers **`The material is not complete!`** for both attempts (h03-tinker3). ACE's `@ci` mints a salvage item with no structure/units, so the server never gets as far as the crafting roll and **no type-5 `ConfirmationRequested` ("You determine that you have a N percent chance to succeed.") can be raised at all** | **PENDING** -- blocker: no complete salvage bag is creatable on this ACE build, so the confirmation the feature answers cannot be provoked. The Apply path itself is proven to reach the server |
| 5 | Pack Inventory hotkey (Ctrl+P) | r2 and r3: `+Acdream.AutoPack.utl` (KeepUpTo 1 on `Prismatic Taper`, pack 1) in place, Ctrl+P delivered from a second PowerShell process via `AppActivate` + `SendKeys` -- first by window title, then (r3) by the client's process id, logged as `pid=4716 title='acdream 0.1.10 | 217 fps | ...'`, `AppActivate(pid)=True`, `sent ctrl+p`. r4/r5: `/mt autopack` as the non-hotkey control | **The hotkey never fired**: neither session printed `Auto Pack - Started.` and the host log has no hotkey activity (b02/b03, c01/c02). The chord itself is not obviously blocked -- the client's own `P` binding is `SelectionPreviousSelection` with no modifier and `AppHotkeyRegistry.CollidesWithClient` is modifier-aware. The `/mt autopack` control DOES work: `<{Mag-Tools}>: Auto Pack - Started.` then `Auto Pack - Completed.` (d01/d02, e01/e02). **No item moved**: the main-pack grid is pixel-identical before and after in both runs. r5 tried to remove the ambiguity by minting a fresh loose taper first, but `@ci prismatictaper` answers `prismatictaper is not a valid weenie`, so whether the profile-matched items were simply already in their assigned pack could not be settled | **PENDING** -- two SendKeys attempts, one of them process-id targeted and confirmed sent, produced no packer run, and whether the default chord never armed or the client swallowed the key is not separable from outside the process; the packer's own lifecycle still only proves Started/Completed |
| 6 | Chest looting | r5: `@create chestthievesden 8` (all eight spawned in the player's own cell `0x0108020E`, 2 m away -- round 4's out-of-reach blocker is gone), `/mt uselp chest`. r6: the same spawn, then the plugin path AND the client's own path (`/mt selectp chest` + `input press UseSelected`) back to back | Neither path opens the chest. The plugin's `Use` prints no refusal (so the host returned `Started`) and nothing opens in 32 s (e04/e05); the client's own selection+use, with `Chest` shown as the live selection in the status bar, opens nothing either in 15 s (f01). Because **both** paths behave identically this is not the plugin-issued-Use class (defect 11, closed in round 4 and re-proven this round on corpses) -- the chest itself does not open on this server/content | **PENDING** -- blocker: `chestthievesden` does not open for this character by any path; needs a chest that is actually openable (unlocked, or with its key) |
| 7 | Combat damage cells + combat filters + Log Out on Death | r6: `Misc/LogOutOnDeath=True` seeded, **`@cloak off`** (new this round -- the character had been cloaked all along), `@attackable on`, `@create tuskerguard 8`, three `/mt attack_melee closest`, then 85 s standing among the survivors; Trackers -> Combat | The server acknowledged both toggles: `You are no longer cloaked, can no longer pass through doors and will appear as an admin.` and `Monsters will attack you normally.` (f03). Kills still one-shot (`You cleave Drudge Prowler in twain!`, `You cleave Mosswart Feeder in twain!`, `You split Tusker Guard apart!`), and **nothing attacked the character in 85 s** -- not one damage, evade or resist line, health drifted upward on regen (f04/f05). Combat tab populates correctly: `All 3`, `Drudge Prowler 1`, `Mosswart Feeder 1`, `Tusker Guard 1`, `Attacks 3 (100%)`; `Dmg Rcvd` and `Dmg Givn` stay blank (f06). No death, so `IEvents.LocalPlayerDied` and the Log-Out-on-Death path were again unobservable | **PENDING** -- unchanged blocker, now with `@cloak off` ruled out as the cause. Two routes spent, as instructed |
| 8 | Quick re-confirms | r1: `@ci manastone` + `/mt usei mana stone`; r1: Tools -> Inventory -> `Clipboard Worn Equipment`, clipboard read from a second process after a sentinel was planted; r6: `/mt uselp corpse` on a fresh corpse; `sawato/+Acdream.CorpseTracker.xml` | All four hold on the closing builds. `<{Mag-Tools}>: Use refused: This item requires a target; call Apply(objectId, targetObjectId) instead.` (a02). Corpse looting through the plugin's own Use: `+(KeepAll) Reliable Lockpick`, `+(KeepAll) Orb, (-200.0 )`, `+(KeepAll) Chainmail Girth` (f03). Corpse tracker persisted three rows including `Description="Corpse of Mosswart Feeder" Opened="True"`. Worn-equipment clipboard: 513 characters, four equipped items, weapon last -- `Leather Leather Helm, AL 223, Craft 5` / `Greater Amuli Shadow Coat, AL 190` / `Leather Leather Greaves, AL 276, Missile Defense 209 to Activate, Diff 174, Craft 8` / `Decapitator's Blade (Two Handed Combat), Heroic Destroyer Set, SlashRend, 60.75-75, +38%a, 38%md, (107.5/82 38.0/38.0), Epic Two Handed Combat Aptitude, ...`, preceded by `Copying all inventory item info to clipboard...` / `All inventory item info has been copied to the clipboard.` | **PASS** |

### Defects and verdicts (round 5)

- **Defect 14a (a vendor buy/sell that does nothing is silent) -- CLOSED,
  verified live.** `Vendor transaction failed.` is printed for both the
  manual `/mt vendor buy`/`sell` and the AutoBuySell-driven round, and
  AutoBuySell stops instead of re-issuing.
- **Defect 14b (an actual completed buy or sale) -- still open, now
  attributed server-side.** The commands reach the wire; ACE rejects them and
  the host logs `InventoryServerSaveFailed err=0x0 rolledBack=False`. The
  pyreal count is unchanged across both. Not a plugin defect on this evidence.
- **Defect 8b (inventory-logger records carry no id data) -- CLOSED.** 15
  records with real int/double/bool/string appraisal values, quoted above.
- **Defect 15 (NEW) -- id data captured in one session is lost on the next
  session's dump.** After r1 the file held 15 `HasIdData=true` records; after
  r2 it held 2, with all 13 lost records still present under the identical
  `Id`+`ObjectClass` key and downgraded to an empty `false` record. The
  logger has exactly one write path
  (`src/OpenAC.MagTools/Loggers/Inventory/InventoryLogger.cs:882`), which
  first reads the previous file and merges through
  `MyWorldObjectRecord.Combine` (`Loggers/Inventory/MyWorldObjectRecord.cs:74`,
  which correctly keeps the older id data when the new snapshot has none), so
  the two candidates are (a) `_host.Storage.ReadText(_storageKey)` returning
  nothing at the dump that produced this file, or (b) the merge branch at
  `InventoryLogger.cs:846-873` not being reached. No `Inventory file is
  corrupt.` was printed, so the import itself did not fail. Distinguishing
  the two needs instrumentation; this round can only report the symptom,
  reproduced once and stable across a third session.
- **Defect 16 (NEW, low) -- the Pack Inventory hotkey does not fire.** Two
  deliveries of Ctrl+P (the second targeted at the client's own process id and
  confirmed sent by the sender log) produced no `Auto Pack - Started.`, while
  `/mt autopack` in the same build does. The One Touch Heal hotkey was proven
  in round 3 only after its chord was written into
  `%APPDATA%\acdream\plugin-hotkeys.json`; no override exists for
  `openac.magtools:pack-inventory`, so the registered default chord
  (`PluginKeyChord(PluginKey.P, Ctrl: true)`) is the untested half. Whether
  the default chord never arms or SendKeys' Ctrl+P is swallowed by the client
  is not separable from outside.
- **Environment: the client can hang after a confirmed graceful logout.** The
  r6 process stayed alive (responding, 618 MB) for minutes after
  `[session] graceful logout confirmed` and `Mag-Tools disabled`; it needed a
  force kill, which was safe because ACE had already taken the logout. This is
  the round-3 defect-7 host shutdown class, out of scope for this repository.
- **Environment: the post-`@teleto` world load takes over two minutes** in
  this graphical build, and the UI probe's `wait world-visible` returns long
  before it finishes -- the screen is still a flat white at 137 s after the
  teleport. Every command issued in that window executes but lands before the
  destination UI is live. This is what cost the trade row all five
  attempts.
- **Environment: `@ci` gaps.** `prismatictaper` and `intricatecarvingtool` are
  not valid weenies on this build; `materialiron` creates a salvage item the
  server considers incomplete.

### Restored checklist (round 5)

- [x] `@attackable off` -- r10 confirmed `Monsters will only attack you if provoked by you first.`
- [x] `@cloak on` (r6 had turned cloaking off) -- r10 confirmed `You are now cloaked.` and `You are now ethereal and can pass through doors.`
- [x] Home position `@teleloc 0x0108020D 46.438545 -54.503498 0.004200 -0.128570 0 0 -0.991700` -- issued at the end of r3, r4, r7c/d/e/f and r10.
- [x] `Mag-Tools.xml` restored byte-for-byte from `$S/Mag-Tools.backup.xml` (the round's own copy is kept as `live-r5/Mag-Tools.after-r5.xml`).
- [x] Headless `+Horan` bot stopped (console Ctrl+C via `stopbot.ps1`, `EXITED`); no `acdream-headless.exe` left.
- [x] Every route ended with `close-client` and a `[session] graceful logout confirmed`; no client was hard-killed while holding a session.
- [ ] **One process object remains**: pid 9060, the r6 client. It logged
      `graceful logout confirmed` and `Mag-Tools disabled` and then hung in
      shutdown; .NET reports `HasExited`, `taskkill` answers "no running
      instance of the task", but Windows still lists it with one stuck thread
      (633 MB working set, 611 handles) -- the round-3 defect-7 Vulkan
      teardown class. It holds no ACE session; it needs a reboot to clear.
- [x] Server toggles used this round and left as found; `Fispur Ansel the
      Grocer.utl` (new, written for the AutoBuySell gate) and the round-3/4
      `.utl` profiles are left in `%LOCALAPPDATA%\acdream\vtank`.

## Pending (owed before the port can be called fully live-gated)

Updated after round 5 (2026-09-17). Rows closed in round 5 are gone; every row
below carries its current blocker.

| Feature | Blocker | Verdict |
|---|---|---|
| Chat filters: the 30 rules other than `MonsterDeaths` and `TradeBuffBotSpam` | still needs a character that can miss, be evaded, be hit, fizzle, resist, salvage and use comps, plus an NPC/vendor to talk. Round 5 removed the last suspected cause of the combat half: the character was CLOAKED all along, and with `@cloak off` + `@attackable on` acknowledged by the server (`Monsters will attack you normally.`) eight hostile Tusker Guards still did not land a single attack in 85 s | PENDING |
| Auto buy/sell: an actual purchase or sale | round 5 narrowed this to the server. The commands now reach the wire and report honestly: `/mt vendor addbuyp a` + `buy` on the Holtburg grocer's one stock item (`Pack`, `costs 88p (you have 21,678p)`) and `addsellp taper` + `sell` each answer `<{Mag-Tools}>: Vendor transaction failed.`, the pyreal count is unchanged at 21,678p, and the host logs `InventoryServerSaveFailed err=0x0 rolledBack=False` after each. AutoBuySell with a `<vendor name>.utl` profile picks a sell item unattended, issues the pair and deactivates on the failure. Needs a vendor/item ACE will actually trade | PENDING |
| Auto trade accept | the A9 host wires `Objects` into `HeadlessAutomationSurface`, so round 4's defect 13 no longer applies. Five round-5 attempts still could not stage an item: after `@teleto +Horan` the graphical client sits on a white loading screen for over 2.5 minutes while the probe's `wait world-visible` returns immediately, so `/mt trade addp` and `accept` execute into a dead window (proved by `No inventory item found named: lockpick` arriving from that window). The trade window itself opens every time. Needs a route that waits on the destination world being drawn, or a partner that comes to the character | PENDING |
| Auto looting: a CHEST | round 5 got the chest in reach (`@create chestthievesden 8` spawned all eight in the player's own cell, 2 m away) and it still does not open -- by the plugin's `Use` (no refusal, nothing opens in 32 s) or by the client's own selection + `UseSelected` with `Chest` as the live selection. Because both paths behave identically this is not the plugin/host Use class; needs a chest that is openable for this character | PENDING |
| Inventory packer: items land in their profile-assigned pack | `/mt autopack` runs (`Auto Pack - Started.` / `Completed.`) but the main-pack grid is pixel-identical before and after. Whether the profile-matched items were already in their assigned pack could not be settled -- `@ci prismatictaper` is not a valid weenie, so a fresh loose taper could not be minted | PENDING |
| Hotkey: Pack Inventory (Ctrl+P) | two SendKeys deliveries, the second targeted at the client's own process id and confirmed sent, produced no packer run while `/mt autopack` in the same build does. No `plugin-hotkeys.json` override exists for `openac.magtools:pack-inventory`, so the registered default chord is the untested half | PENDING |
| Inventory logger: id data that survives a session | round 5 closed the capture half -- 15 records with real int/double/bool/string appraisal values, and the second session correctly skips the request line. What is owed is carry-over: 13 of those 15 records came back `false` with an empty value block after the very next session (defect 15), and only 15 of the 60 ident-worthy items were identified in a 4.5-minute session (about one per 20 s) | PENDING |
| Tinkering auto-confirm | the plugin's `Items.Apply(source, target)` reaches the server (ACE answers `The material is not complete!`), but `@ci materialiron` mints a salvage item with no structure, so the type-5 crafting confirmation the feature answers can never be raised. Needs a complete salvage bag | PENDING |
| Idle automation: heart carver / shattered-key fixer / key deringer | `Intricate Carving Tool` still not creatable: `@ci intricatecarvingtool` is not a valid weenie and `@ci 42979` creates a `Core Plating Integrator` | PENDING |
| Mana auto recharge | the trigger is ACE's `Your <item> is low on Mana.`, which `Player_Tick` only emits for an item that actively burns mana. The character's equipped items do not burn: drained to 2 of 294 with `@givemana`, the value was unchanged many minutes later, so the warning is never emitted | PENDING |
| One-touch heal with a healing KIT | the hotkey itself is proven (bound via `plugin-hotkeys.json`, Ctrl+J applied a `Heal`-named food item four times). The kit branch needs a `HealingKit`-class item, and no healing-kit weenie name resolves on this ACE build | PENDING |
| Log out on death | blocked on the same thing as the combat rows, now with cloaking ruled out: nothing attacks this character, so it never dies and `IEvents.LocalPlayerDied` has still not been observed to fire. The P10 audit of `src/OpenAC.MagTools/Macros/LogOutOnDeath.cs` still stands | PENDING |
| Combat tracker `Dmg Rcvd` / `Dmg Givn` cells | rows, KB's and the attack counter all populate (round 5: `All 3`, three named rows, `Attacks 3 (100%)`); the two damage cells are still unprovoked for the reason above | PENDING |

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

## P10 review round (2026-09-16, no live gate -- code review findings)

A dual-lens code review of the P10 fix round (defects 8/9/10) surfaced nine
further findings against the SAME files, addressed here as a local fix round
(no live client session; all findings and fixes are covered by fail-first
unit tests). Fixed at `af98176` (MEDIUM-4), `19ebb25` (MEDIUM-6), `13918f6`
(LOW-8), and `59df03a` (HIGH-1/HIGH-2/HIGH-3/MEDIUM-5/LOW-9/MEDIUM-7):

- **HIGH-1** `_lastOwnedSnapshot` only refreshed at startup and at id-wait
  completion, so `Stop()`'s logoff dump wrote the login-time inventory,
  missing anything looted/bought/tinkered mid-session. Fixed: refreshed at
  the top of `Dump()` whenever non-empty, and every second by the
  HIGH-3 poll for the rest of the session.
- **HIGH-2** `Stop()` wrote `Export([])` over an existing file when nothing
  was ever captured (empty character, or teardown before the pack streamed
  in). Fixed: `Stop()` skips the dump when the snapshot is still empty.
- **HIGH-3** the startup wait subscribed `Events.Tick` directly, running a
  full `CaptureOwnedItems()` walk every frame -- breaking the "TickScheduler
  is the plugin's only clock" rule. Fixed: `Start()` now takes the plugin's
  `TickScheduler` and polls at 1 Hz, bounded at 60 polls (~60s) before
  giving up.
- **MEDIUM-4** `WorldObjectSorter`'s "directly mine" test used
  `ContainerObjectId == myObjectId`, which is 0 for every equipped item on
  this host, making the equipped-ordering block unreachable. Fixed: also
  treats `WielderObjectId == myObjectId` as directly mine.
- **MEDIUM-5** `OnObjectChanged`'s per-item identify path required
  `ContainerObjectId == Character.ObjectId`, so an item equipped mid-session
  (host-shaped: `WielderObjectId` is the player, not `ContainerObjectId`)
  was never id-requested. Fixed: accepts `WielderObjectId` too, H7 ordering
  preserved.
- **MEDIUM-6** the inventory-search page called `Items.Use(0)` for any
  equipped search result (same host-shape cause). Fixed: skips the
  container-open when the item is equipped by the player.
- **MEDIUM-7** `docs/deviations.md` rows added for the deferred/polled
  startup dump and the `anyMissing == false` immediate-dump branch.
- **LOW-8** the give-up branch printed "N items lacked id data" even when
  the clipboard write failed, contradicting the paired "Clipboard is
  unavailable" message. Fixed: suppressed when the write failed.
- **LOW-9** while `_waitingForIdData`, `OnObjectChanged` returned before the
  per-item Identify path ran, so an item arriving during the wait was never
  requested and the wait could hang. Fixed: falls through to the per-item
  path (H7-ordered) instead of returning.

Owed: a live re-gate of the inventory logger, exporter, and worn-equipment
sort order against a real host, since this round's evidence is unit tests
only (no game client was launched for this round per the coordinator's
instruction).

## P10 re-review round (2026-09-16, no live gate -- follow-up code review findings)

The P10 review round's nine findings were confirmed fixed; a re-review of the
SAME fix pass found five further issues, all in `InventoryLogger.cs` (plus
one test-only finding). Fixed at `33b9c69`:

- **MEDIUM-A** the 1 Hz snapshot poll unconditionally replaced
  `_lastOwnedSnapshot` on any non-empty capture, risking a partial/stale
  refresh near logoff. Checked the host's actual Logoff-vs-teardown ordering
  under `OpenAcRoot` (see the commit message and the code comment on
  `OnSnapshotPoll` for the exact `LiveSessionController.cs`/
  `RuntimeGenerationReset.cs` line citations): teardown is atomic in both
  observed logoff paths, so a genuinely partial capture is never observable,
  but a poll can still land in the gap between `IsInWorld` flipping false and
  this plugin's own `Stop()` running. Fixed: the refresh now skips while
  `ICharacterInfo.IsInWorld` is false.
- **MEDIUM-B** after the 60-poll startup give-up, a later non-empty poll
  still refreshed the snapshot and `Stop()` still dumped it, contradicting
  the give-up comment and the deviations row. Fixed: give-up now disposes
  the poll outright.
- **LOW-C** `_lastOwnedSnapshot = items.ToArray()` was a redundant second
  copy every second (`CaptureOwnedItems` already returns a fresh list per
  call). Fixed: assigns directly; the test fake was also corrected to
  return a fresh copy (it previously returned a live, mutable list,
  diverging from the real host's contract).
- **LOW-D** the startup identify loop didn't record ids in `_requestedIds`,
  causing a duplicate `Identify` on a later `Created` for the same item.
  Fixed.
- **LOW-E** the give-up test's final asserts could never fail. Replaced with
  a behavioral check that a post-give-up arrival is never picked up.

Still owed: the same live re-gate noted after the P10 round.
