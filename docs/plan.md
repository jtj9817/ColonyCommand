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

### 1.2 Zero-dependency integration path (spec §5.1 decision gate) — found, pending in-game proof

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
`LookMode.Deep` — so they are ruled out). The plan uses a vanilla `Window`:

- `Window.ExtraOnGUI()` runs inside `WindowStack.WindowStackOnGUI` on **every** OnGUI event, including the
  `Layout` event that Unity sends before each real event (`Root` keeps `useGUILayout` on). The bar ignores
  `Layout`, so re-applying the view during `Layout` lands just before the bar handles the following
  Repaint/MouseDown. A tick-driven `MarkColonistsDirty` therefore never reaches a visible frame or a click.
  **[verify]** in Phase 1: no one-frame flash of the full roster, clicks hit the right pawn after a dirty.
- `Window.WindowUpdate()` (per frame, from `UIRootUpdate`) re-applies too, as a cheap safety net.
- Re-application is cheap when nothing changed: compare a signature (dirty flag observed via the list
  having been rebuilt, criteria version, a ~0.25 s real-time refresh for live state such as drafted/injured).

**No Harmony fallback (user decision Q2).** If the [verify] items fail, the feature is reduced instead (to be
agreed with the user), never patched with Harmony.

### 1.3 Controls: a vanilla Window, nothing saved

- `ColonyCommandWindow : Window` — `layer = WindowLayer.GameUI` (as `MainTabWindow`), no background/shadow,
  `preventCameraMotion = false`, `closeOnCancel = false`, `closeOnClickedOutside = false`,
  `focusWhenOpened = false`, `soundAppear = null`, `absorbInputAroundWindow = false`. Windows live in
  `UIRoot.windows`, which is never saved, so **no save data** by construction.
- Search field: vanilla `QuickSearchWidget` (magnifier, placeholder, clear ×, Esc unfocuses). Overriding
  `Window.CommonSearchWidget` makes `WindowStack.AnySearchWidgetFocused` true while typing, and
  `KeyBindingDef.KeyDownEvent` then suppresses **all** game hotkeys — vanilla's own mechanism.
- Opening the window: a `MonoBehaviour` on a `DontDestroyOnLoad` GameObject (created from the `Mod`
  constructor, same pattern as the toolkit's `AutoTest.cs`) checks in `Update()` that
  `Current.ProgramState == Playing` and the window is open, and adds it if not. Never saved.
- Placement: rect computed from the bar's own `DrawLocs`/`Size` each frame — search field in the free
  right-hand 260 px beside the bar; chip strip directly under the bar's lowest row. Window rect shrinks to the
  controls only, so it never covers portraits (spec: never steal clicks from visible portraits).

### 1.4 Predicates and values (spec §3.2, §4)

| Criterion | Source | Open question |
| --- | --- | --- |
| Drafted | `pawn.Drafted` | — |
| Injured | `health.hediffSet.HasNaturallyHealingInjury()` — any non-permanent injury (Q3) | — |
| Idle | exactly the bar's idle-icon branch in `ColonistBarColonistDrawer` (Q4): `mindState.IsIdle` (not downed, spawned, `lastJobTag == JobTag.Idle`) + `GenDate.DaysPassed >= 1`, and none of the higher-priority icon states (mental state, drafted/fleeing/attacking) | Re-read the drawer's full branch when implementing |
| Skill ≥ N | `pawn.skills?.GetSkill(def)`; unavailable when `skills == null` or `TotallyDisabled` → excluded from filter, sorted last | — |
| Mood | `pawn.needs?.mood?.CurLevelPercentage`; null (e.g. corpse, subhuman) → sorted last | — |
| Name | `pawn.LabelShort`, `Name` as `NameTriple` (First/Nick/Last) or `NameSingle`; trimmed, `OrdinalIgnoreCase` substring | — |

Dead colonists (corpse entries) have `pawn != null`; they match name search but fail Drafted/Idle/skill;
mood is unavailable.

## 2. Architecture

```
Source/ColonyCommand/
  ColonyCommandMod.cs        Mod subclass; creates the bootstrap GameObject; resolves reflection once
  Bootstrap.cs               MonoBehaviour: ensures the window is open while Playing
  ViewState.cs               static, memory-only UI state (query, status, skill, min level, sort, direction)
  RosterView.cs              PURE: (entries, state) → filtered/sorted entries + chip counts (no Unity/GUI)
  Predicates.cs              PURE-ish: drafted / injured / idle / skill / mood / name over a Pawn
  BarAdapter.cs              the only code touching ColonistBar: recache, apply view, relayout, reflection
  ColonyCommandWindow.cs     controls: QuickSearchWidget, chips, Skill + Sort dropdowns (FloatMenu), empty state
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
| `Name search` | wrong match on case/partial/first-last/whitespace; no-match empty state |
| `Status chips` | wrong predicate or count for Drafted/Injured/Idle; >1 status active |
| `Combined filter` | AND logic broken; All doesn't keep search |
| `Sort and restore` | non-deterministic ties; vanilla order not restored; `displayOrder` mutated |
| `Bar integrity` | DrawLocs/Entries out of sync after a dirty (IndexOutOfRange), hit-test returns a hidden pawn, drag reorder writes `displayOrder` |
| `Live refresh` | draft/injure/kill a fixture colonist → counts and view update without reopening |
| `No save data` | save after use contains no `ColonyCommand` string; reload resets state |
| `Core-only load` | release package loads with Core only, no missing-dependency errors (pkg-run / user check) |

Test code lives in a **separate test-only mod** (`Tests/ColonyCommandTests/`, Q1) loaded via `MOD_EXTRA_MODS`;
it carries the Harmony-using `AutoTest.cs` so the shipped DLL never references Harmony. The Core-only/no-Harmony
load check is user-in-the-loop, since `autotest.py` always loads Harmony.

`RosterView` is pure, so the matching/sort/count logic is asserted in-game via the scenario driver against
fixture pawns rather than a separate unit-test project (no test framework dependency).
Display-size/UI-scale checks (spec §6) are a short **user-in-the-loop** pass with screenshots, not automation.
