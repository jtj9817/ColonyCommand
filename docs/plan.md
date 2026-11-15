# Colony Command — implementation plan

Source spec: `Colony_Command_Specs.docx` (release 1). Progress tracking: [`TODO.md`](../TODO.md).
All engine facts below were read from decompiled RimWorld **1.6.4871 rev598** `Assembly-CSharp`
(`tools/vanilla.sh init` → `.tools/decomp/`). Anything not yet proven in game is marked **[verify]**.

## 1. Source verification (spec §5.3)

### 1.1 How the vanilla colonist bar works

| Concern | 1.6 code path | Notes |
| --- | --- | --- |
| Owner | `Find.ColonistBar` → `UIRoot_Play.mapUI.colonistBar` (`MapInterface`) | One instance per `UIRoot_Play`, i.e. per loaded game: no stale state survives a reload |
| Roster | `ColonistBar.CheckRecacheEntries()` (private), run lazily from the `Entries` getter when `entriesDirty` | Per map (home maps first, then `uniqueID`): `FreeColonists` + `ColonySubhumansControllable` + non-desiccated colonist corpses (on map and carried); then player caravans. Each map/caravan is a **group**; an empty map gets a `pawn == null` placeholder entry. Order inside a group: `PlayerPawnsDisplayOrderUtility.Sort` (`playerSettings.displayOrder`) |
| Layout | `ColonistBarDrawLocsFinder.CalculateDrawLocs(outDrawLocs, out scale, groupsCount)` | Reads `Find.ColonistBar.Entries`; bar width = `UI.screenWidth − 520` (260 px free each side), top margin 21 px, shrinks scale and wraps to ≤ 3 rows |
| Draw + hit test | `ColonistBar.ColonistBarOnGUI()`: iterates `cachedDrawLocs`, indexes `Entries[i]`; `drawer.HandleClicks` / `drawer.DrawColonist` | Skips `EventType.Layout`. Called from `MapInterface.MapInterfaceOnGUI_BeforeMainTabs` and `WorldInterface.WorldInterfaceOnGUI` |
| Invalidation | `MarkColonistsDirty()` — spawn/despawn (`Thing`), `Pawn` changes, `MapPawns`, corpses, caravans, `PlaySettings.showColonistBar`, resolution change (`WindowStack`), map add/remove (`Game`), `Reorder` | Mostly from ticks (`Game.UpdatePlay`), not from OnGUI |
| Other consumers of `Entries` | `TryGetEntryAt` (targeter, clicks), `GetColonistsInOrder` (next/prev-colonist hotkeys, `SelectorUtility.SortInColonistBarOrder`), `ColonistsOrCorpsesInScreenRect` (drag-box) | They follow whatever the list holds |
| Drag reorder | `Entry.reorderAction` → `ColonistBar.Reorder` **writes `playerSettings.displayOrder`** | Must be neutralised while a filter/sort is active (spec: never mutate game data) |

### 1.2 Zero-dependency integration path (spec §5.1 decision gate) — **proven in game** (Phase 1, 2026-11-15, §1.5)

Key fact: the public `Entries` getter returns the **private `cachedEntries` list by reference**, and `DrawLocs`
returns `cachedDrawLocs` by reference. Both are mutable from outside without Harmony. Plan:

1. Force a recache by reading `Entries` (vanilla roster, vanilla order).
2. If no criteria are active and sort = vanilla → touch nothing (zero interference).
3. Otherwise rebuild `cachedEntries` in place as the filtered + sorted view: drop non-matching pawns,
   drop groups left empty and renumber groups contiguously (an empty group would distort
   `CalculateDrawLocs`), keep vanilla `pawn == null` empty-map entries untouched only when no filter is active,
   and give every view entry a **no-op `reorderAction`** so dragging can't write `displayOrder`.
4. Recompute layout with our own `new ColonistBarDrawLocsFinder()` into `DrawLocs` and write the private
   `cachedScale` via `System.Reflection` (one `FieldInfo`, resolved once at startup; fail closed — feature
   disabled with one log error — if the field is missing). `cachedReorderableGroups` only ever needs
   `Count ≥ entries`, which shrinking guarantees.

**When to apply (ordering).** No non-saved native callback runs right before `ColonistBarOnGUI` (GameComponent/
MapComponent OnGUI do, but both are **serialized into the save** — `Game.ExposeData` scribes `components` with
`LookMode.Deep` — so they are ruled out). What the decompile and the Phase 1 runs established:

- Per frame, Unity runs every `Update` first: `Root_Play.Update` calls `base.Update()` (which runs
  `UIRootUpdate` → `WindowsUpdate` → `Window.WindowUpdate`) **and then** `Current.Game.UpdatePlay()`, the ticks that
  call `MarkColonistsDirty`. So `WindowUpdate` runs *before* the ticks and cannot repair a same-frame dirty.
- `UIRoot_Play.UIRootOnGUI` draws the bar (`WorldInterfaceOnGUI`, `MapInterfaceOnGUI_BeforeMainTabs`) and only
  afterwards `windows.WindowStackOnGUI()`, which calls `Window.ExtraOnGUI()` on **every** OnGUI event including `Layout`.
- **Primary hook: `Bootstrap.LateUpdate()`.** It runs after every `Update` of the frame (so after the ticks) and
  before any OnGUI event, so the bar is never drawn or clicked in its vanilla state after a tick-driven dirty.
- **Safety net: `ColonyCommandWindow.ExtraOnGUI()` and `WindowUpdate()`** repair a dirty raised *during* OnGUI (UI code,
  a drag), before the next event reaches the bar. Phase 1 showed `ExtraOnGUI` alone (the Layout event that precedes the
  real events) is also sufficient, so the two hooks are independently enough and redundant by design.
- Re-application is cheap when nothing changed, with no flags to keep in sync: every entry of our view carries one shared
  static no-op `reorderAction`; vanilla builds fresh per-entry closures on each recache, so "the list still carries our
  delegate" means "nothing rebuilt it". This also survives the whole bar being replaced by a load. Phase 2 adds a
  criteria version and a ~0.25 s real-time refresh for live state (drafted/injured/idle/mood/skill).
- Switching the view on starts from a fresh vanilla cache (`MarkColonistsDirty` then read `Entries`), which also resets
  vanilla's `cachedReorderableGroups`; switching it off only marks the bar dirty and vanilla restores itself.

**No Harmony fallback (user decision Q2).** If the [verify] items fail, the feature is reduced instead (to be
agreed with the user), never patched with Harmony.

### 1.3 Controls: a vanilla Window, nothing saved

- `ColonyCommandWindow : Window` — `layer = WindowLayer.GameUI` (as `MainTabWindow`), no background/shadow,
  `preventCameraMotion = false`, `closeOnCancel = false`, `closeOnClickedOutside = false`,
  `focusWhenOpened = false`, `soundAppear = null`, `absorbInputAroundWindow = false`, and **`closeOnAccept = false`**
  (it defaults to true: Enter would close the window). Windows live in `UIRoot.windows`, which is never saved, so
  **no save data** by construction.
- Search field: vanilla `QuickSearchWidget` (magnifier, placeholder, clear ×, Esc unfocuses). Overriding
  `Window.CommonSearchWidget` makes `WindowStack.AnySearchWidgetFocused` true while typing, and
  `KeyBindingDef.KeyDownEvent` then suppresses **all** game hotkeys — vanilla's own mechanism.
- Opening the window: a `MonoBehaviour` (`Bootstrap`) on a `DontDestroyOnLoad` GameObject checks in `Update()` that
  `Current.ProgramState == Playing`, no long event is running and `Find.UIRoot is UIRoot_Play`, and adds the window if it
  is not open. Never saved. **Correction:** it is created from a `[StaticConstructorOnStartup]` class, *not* the `Mod`
  constructor as first planned — `LoadAllPlayData` (which creates the `Mod` classes) is queued with
  `doAsynchronously: true`, a loader thread where Unity objects cannot be created, whereas `CallAll` for static
  constructors is queued synchronously (`Root.cs`).
- Placement (built, `ControlsLayout`, proven Phase 3): the window's rect is **exactly the search field**, so it never covers a
  portrait; the chip row and the empty-state line are drawn in screen space from `ExtraOnGUI`. Everything is placed from the
  bar's own lists on every event: search field right of the portraits (level with the first row) when at least 170 px are free
  before the alert column (`screenWidth - 154 - 8`), otherwise a search icon with the opened field in a row under the chips;
  chips under everything the bar draws, anchored to the **unfiltered** footprint (`BarAdapter.VanillaFootprint`, measured from
  the vanilla layout at each build) and a one-row baseline so they do not move while filtering; hidden below 800x500, with the
  bar off (`showColonistBar`) or the tile picker up.

### 1.4 Predicates and values (spec §3.2, §4) — implemented and proven in Phase 2

| Criterion | Source | Notes |
| --- | --- | --- |
| Drafted | `!pawn.Dead && pawn.Drafted` | A corpse is never drafted |
| Injured | `!pawn.Dead && health.hediffSet.HasNaturallyHealingInjury()` — any non-permanent `Hediff_Injury` (Q3) | A scar (permanent) and a non-injury hediff do not count; the dead are never injured (open point 1) |
| Idle | exactly the bar's idle-icon branch, the last of the chain in `ColonistBarColonistDrawer.DrawIcons`: not dead → not in a mental state → not in a medical bed → not asleep (job, or resting in a caravan) → not fleeing → not attacking → `mindState.IsIdle && GenDate.DaysPassed >= 1` | **No "not drafted" term**: the bar has none; a drafted colonist leaves `lastJobTag == Idle` through the think tree (`DraftedOrder`). The oracle is the bar itself (`IdleProbe`, a test-mod hook on `DrawIcons`/`DrawIcon`): `Status chips` compares predicate and drawn icon for every colonist, with a day-0 control and a real draft |
| Skill ≥ N | the pawn's `SkillRecord.Level` (read from `skills.skills`, never `GetSkill`, which logs an error for a def the pawn lacks); unavailable (-1) when `skills == null`, dead, or `TotallyDisabled` | Unavailable pawns fail any skill criterion (even 0) and sort last. Core-only: `Level` has no aptitude offset |
| Mood | `pawn.needs?.mood?.CurLevelPercentage`; NaN (unavailable) when dead or no mood need | Sorted last in both directions |
| Name | `LabelShortCap` is the displayed name (untruncated); match = `LabelShort`, `NameTriple` `First`/`Nick`/`Last` or `NameSingle.Name`, and `ToStringFull`; trimmed `OrdinalIgnoreCase` substring | Name sort compares `LabelShortCap` with `InvariantCultureIgnoreCase` |

Dead colonists (corpse entries) have `pawn != null`: they match name search only and sort last for mood and skill.
Placeholders (`pawn == null`, a map without colonists) are kept only while no filter is active.

### 1.4a The pipeline as built

`ViewState` (static, memory only: query, status, skill, min level, sort, direction, `Version`) → `Criteria` (a plain struct
snapshot) → `RosterView.Build(vanillaEntries, criteria, output, out RosterCounts)`: per group, name → status → skill →
sort (`List.Sort` with the vanilla index as the last key, so the order is total and deterministic), groups renumbered from
0, groups that lose every entry vanish. `BarAdapter.ApplyCore`: identity (`Criteria.IsIdentity`) leaves the bar untouched
(switching off = `MarkColonistsDirty`); otherwise it rebuilds when the criteria `Version` changed (from a fresh vanilla
cache) or when the list is no longer ours (`IsView`: a vanilla recache), stamps the shared no-op `reorderAction` /
`extraDraggedItemOnGUI`, lays out with its own `ColonistBarDrawLocsFinder`, writes `cachedScale`. `ViewState.SyncGame()`
runs first in every `Apply()` and resets the state when `Current.Game` is a different game. Chip counts
(`BarAdapter.Counts`) are computed from the same vanilla snapshot at each build.

### 1.5 Phase 1 gate: the zero-dependency seam holds (2026-11-15)

Every proof below ran in game (RimWorld 1.6.4871 rev598, 800×500, next to the user's game) and is a row in
[`test-runs.md`](test-runs.md); the probe is a test-only Harmony observer (`BarProbe`) that reads what the vanilla bar
actually iterates on each non-Layout OnGUI event without calling its getters (reading `Entries` would itself recache
and hide a flash). The shipped `ColonyCommand.dll` references only `mscorlib`, `UnityEngine.CoreModule` and
`Assembly-CSharp` (verified from its assembly-reference table and by the `Harness wiring` scenario).

| Proof | Scenario | Evidence |
| --- | --- | --- |
| View written, relaid out, restored | `Seam view` | 5 colonists → 3 shown; scale 0.77 → 1.00 (the `cachedScale` write works); 20 of 20 repaints showed exactly the view, then exactly the vanilla roster after switching off |
| No flash of the full roster after a dirty | `Seam dirty` | **Control:** with every apply hook suppressed, 39/39 draws were wrong (the detector sees the flash). `LateUpdate` only: 0/59 wrong. Window hooks only: 0/59 wrong. All hooks, dirtied every frame from `Update` and from OnGUI: 0/59 wrong |
| Click / hit test lands on the visible pawn | `Seam hit test` | Every slot centre resolves to its own colonist; sweeping the whole top band reaches exactly the 3 visible colonists and never the 2 hidden ones; 3 **real** xdotool clicks (probe saw real `MouseDown`s) each selected the right colonist while the bar was dirtied every frame; 0 wrong draws across 31 events |
| Drag reorder is a no-op, `displayOrder` unchanged | `Seam reorder` | **Control:** a vanilla API reorder and a real right-button drag both reached `ColonistBar.Reorder` and rewrote display orders. In the view, the same API calls and the real drag reached it 0 times and every `displayOrder` was unchanged |
| No errors on world view, caravan group, map switch | `Seam topology`, `Seam map switch` | World view drew the view correctly (bar draws there via `WorldInterface`); a caravan group (map-less entries, group frames) renumbered correctly (4 shown, 3 hidden, none reachable); a second generated player map: camera there and back, view held on both, one group per map. 0 new errors |
| No save data | `Seam no save data` | The save header lists the mod by packageId, as vanilla does for every mod (the check reads it as a control); the game body holds no `colonycommand` text (case-insensitive) |
| Bootstrap lifecycle | `Seam bootstrap` | Exactly one window and one `Bootstrap`; closing the window reopens a new one; save → main menu (40 menu frames, 0 windows) → load: back with exactly one window |
| Fails closed | `Seam fail closed` | A missing member and an exception while applying each log exactly **one** error, hand the bar back to vanilla, and stay silent for 40 further frames with the view requested |

Verdict: **gate passed, no reduction of the feature needed**; Q2 (no Harmony in the shipped mod) stands.

Facts the runs added:

- `ColonistBar.Visible` is false below 800×500 (`UI.screenWidth/Height`), so nothing about the bar can be tested in the
  harness's old 640×360 window; this repo's harness defaults to 800×500.
- At 800×500 the bar is 280 px wide (`screenWidth − 520`): 3 portraits fit at scale 1, 5 shrink to ~0.77.
- Real input is testable in the harness through `xdotool` against the private Xvfb (left click selects through
  `Selector.SelectableObjectsUnderMouse` → `ColonistBar.ColonistOrCorpseAt`; the bar's reorder is a **right**-button drag).
- Entries are `struct`s whose `reorderAction` / `extraDraggedItemOnGUI` are fields: assign on a local copy.
- The first draw after switching the view off draws with the bar *dirty*: that is how vanilla restores itself, not a flash.

## 2. Architecture

```
Source/ColonyCommand/
  ColonyCommandMod.cs        empty Mod subclass (Mod constructors run on a loader thread)
  CC.cs                      identity, build id, the one logging helper ([ColonyCommand] tag)
  Startup.cs                 [StaticConstructorOnStartup]: build-id line, resolves reflection once, creates Bootstrap
  Bootstrap.cs               MonoBehaviour: ensures the window is open while Playing; LateUpdate applies the view
  ViewState.cs               static, memory-only UI state (query, status, skill, min level, sort, direction) + Version; SyncGame
  Predicates.cs              drafted / injured / idle (the bar's chain) / skill / mood / name over a Pawn
  RosterView.cs              (entries, Criteria) -> filtered/sorted entries + chip counts; no Unity UI, no Find
  BarAdapter.cs              the only code touching ColonistBar: recache, apply view, relayout, reflection, 0.25 s refresh
  ControlsLayout.cs          PURE placement of the controls from the bar's footprint (search field/icon, chip row, empty state)
  FirstResult.cs             what Enter selects: a click on the first result's portrait, by vanilla's own paths
  ColonyCommandWindow.cs     the window (rect = the search field) + chip row, Skill/Level/Sort/direction/Reset, empty state, menus
Languages/English/Keyed/ColonyCommand.xml
About/About.xml               packageId josh9817.colonycommand, supportedVersions 1.6, no dependencies
```

Pipeline (spec §4.3): `Entries` (vanilla) → name match → status predicate → skill criterion → stable sort
within each group (tie: vanilla index) → write view → relayout → render. Chip counts = eligible + name-matching
pawns satisfying each chip, ignoring the active status/skill (spec §3.2).

Sorting is **per group** (per map / caravan): groups are what the bar lays out, and mixing maps would break
group frames and click-to-jump.

## 3. Testing (YAGNI/KISS, strict-scope rule in AGENTS.md)

Fixtures: **colonists only** — spawned on the quicktest map with set names, drafted state, an injury hediff,
skill levels, a mood offset (thought/need level). No rooms, power, items or animals.

| Scenario | Failure mode it targets |
| --- | --- |
| `Harness wiring` (P0) | test mod loads before the main assembly exists; the shipped assembly gains a Harmony reference |
| `Seam view` / `Seam dirty` / `Seam hit test` / `Seam reorder` / `Seam topology` / `Seam map switch` / `Seam no save data` / `Seam bootstrap` / `Seam fail closed` (P1, passed) | the seam's gate proofs, one failure mode each: see §1.5 |
| `Name search` (P2, passed) | wrong match on case/partial/first-nick-last-single/full name/whitespace; a dead colonist unfindable; no-match leaving a broken bar; names or selection changed |
| `Status chips` (P2, passed) | wrong predicate or count for Drafted/Injured/Idle; Idle disagreeing with the bar's own icon; >1 status active |
| `Combined filter` (P2, passed) | AND logic broken; skill minimum wrong; All doesn't keep search |
| `Sort and restore` (P2, passed) | non-deterministic ties; unavailable values not last; vanilla order not restored; `displayOrder` mutated |
| `Bar integrity` (P2, passed) | DrawLocs/Entries out of sync after a dirty (IndexOutOfRange), hit-test returns a hidden pawn, drag reorder writes `displayOrder` |
| `View reset on load` (P2, passed) | the static `ViewState` survives a load: a hidden roster in a fresh game behind a blank search field (covers the "reload resets state" half of `No save data`) |
| `Search field` / `Search Enter` / `Enter across maps` / `Chip clicks` / `Empty state` / `Controls placement` (P3, passed) | the controls: see `TODO.md` Phase 3 and `test-runs.md` 03:22 |
| `Live refresh` | draft/injure/kill a fixture colonist → counts and view update without reopening |
| `No save data` | save after use contains no `ColonyCommand` string; reload resets state |
| `Core-only load` | release package loads with Core only, no missing-dependency errors (pkg-run / user check) |

Test code lives in a **separate test-only mod** (`Tests/ColonyCommandTests/`, Q1) loaded via `MOD_EXTRA_MODS`;
it carries the Harmony-using `AutoTest.cs` so the shipped DLL never references Harmony. The Core-only/no-Harmony
load check is user-in-the-loop, since `autotest.py` always loads Harmony.

`RosterView` is pure, so the matching/sort/count logic is asserted in-game via the scenario driver against
fixture pawns rather than a separate unit-test project (no test framework dependency).
Display-size/UI-scale checks (spec §6) are a short **user-in-the-loop** pass with screenshots, not automation.
