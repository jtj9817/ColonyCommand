# TODO — Colony Command (release 1)

Plan and verified engine facts: [`docs/plan.md`](docs/plan.md). Spec: `Colony_Command_Specs.docx`.
A box is ticked only when proven (build, scenario run named in `docs/test-runs.md`, or user check).

## Decisions (user, 2026-11-14)

- [x] **Q1 Test driver** — separate **test-only mod** (`Tests/ColonyCommandTests/`: adapted `AutoTest.cs` +
      scenarios + fixtures, references Harmony and the main assembly), loaded only by autotest runs via
      `MOD_EXTRA_MODS`. The shipped `ColonyCommand.dll` never references Harmony.
- [x] **Q2 Integration** — **native only**. No Harmony in the mod under any circumstances; if the Phase 1
      proof fails, reduce the feature instead (and ask how).
- [x] **Q3 Injured** — `health.hediffSet.HasNaturallyHealingInjury()` (any non-permanent injury).
- [x] **Q4 Idle** — exactly the bar's idle icon condition (`ColonistBarColonistDrawer.DrawIcons`): the idle icon is the
      last branch of a chain and shows only when none of these apply, in order: dead, mental state, medical bed,
      asleep, flee/cower, attacking; then `mindState.IsIdle && GenDate.DaysPassed >= 1`. "Not drafted" is **not** a
      term: a drafted colonist is excluded through `lastJobTag` (`DraftedOrder`). See open point 4 below.
- [x] **Q5 Tooling port** — **do not port** the tooling changes of commit `c8a34b4` (`build.sh`, `package.sh`,
      `autotest.py`, `env.sh`: repo-local test mod `Tests/<MOD_NAME>Tests`, `MOD_EXTRA_MODS` default, 800x500 window)
      to `/mnt/0B8533211952FCF2/rimworld-modding-center`. They stay local to this repo, which therefore intentionally
      differs from the center snapshot at `0542d72`. Do not raise it again or modify the center from here. (user, 2026-11-14)
- [x] **Phase 2, open point 1 — dead colonists** — corpse entries (`pawn.Dead`) are **never** Injured, Drafted, Idle or
      skill-qualified: every status and skill predicate guards `!pawn.Dead` first, so a corpse matches **name search
      only** and sorts last for mood and skill. Why: `HasNaturallyHealingInjury()` has no `Dead` check and a dead
      colonist keeps their wounds, so the literal Q3 would count corpses; the bar draws no status icons for the dead
      (`ColonistBarColonistDrawer.DrawIcons`). Narrows Q3 to living pawns only; Q3 is otherwise unchanged. (user, 2026-11-14)
- [x] **Phase 2, open point 2 — the probe view** — **keep** `BarAdapter.ProbeView` / `ProbeKeeps` and the debug action
      "Toggle probe view" through Phase 2 as an internal test hook; the `Seam *` scenarios keep running against it.
      Delete the probe, the debug action and the probe-based `ExpectedView` oracle in **one dedicated commit**, only
      after `Bar integrity` covers the same failure modes (flash after a dirty, hit test, drag no-op) and
      `Seam view`, `Seam dirty`, `Seam fail closed` have been rerun against the real view. (user, 2026-11-14)
- [x] **Phase 2, open point 3 — placeholder (`pawn == null`) entries** — a placeholder is a pawnless entry: it passes
      only when **no criterion is active**, so it is kept in an identity or sort-only view (the entry set must equal
      vanilla's) and dropped under any filter; a group left with no entry vanishes and the rest are renumbered. "Filter
      active" means trimmed query non-empty, **or** a status set, **or** a skill set (not `QuickSearchWidget.filter.Active`,
      which is true for a whitespace-only query). Zero results = no pawn entries left, which drives the FR-04 empty state.
      Cost: while filtering, the group-frame click to a colonist-less map is gone; Reset restores it. Must be covered:
      `Name search` no-match case drawing a bar with 0 entries and 0 draw locations. (user, 2026-11-14)
- [x] **Phase 2, open point 4 — Idle chain vs the old Q4 wording** — implement the bar's chain exactly as vanilla has it
      (`ColonistBarColonistDrawer.DrawIcons` `:337`) and add **no** "not drafted" term; a drafted colonist is excluded by
      `lastJobTag == DraftedOrder` (`Pawn_DraftController` setter ends the job; the Humanlike think tree tags
      `JobGiver_Orders` as `DraftedOrder`). The Q4 line above is corrected. `Status chips` uses the bar's own draw as the
      oracle: a test-mod Harmony hook on `DrawIcons` (current pawn) and the private `DrawIcon(Texture2D, ...)` records
      whether `Icon_Idle` was drawn for each fixture pawn, compared with `Predicates.Idle`. Fixtures: time paused after
      staging; `DebugSetTicksGame` past day 1; controls with the day forced back to 0 (icon absent, predicate false) and
      `lastJobTag = Idle` then `drafter.Drafted = true` (icon gone, predicate false). (user, 2026-11-14)
- [x] **Phase 2/3, open point 5 — Enter selection across maps and caravans** — Enter does what a plain click on the
      first result's portrait does, per vanilla branch. Target = first entry with `pawn != null` in view order (home map,
      other maps by id, caravans; sort order inside a group). Gate: search focused **and** trimmed query non-empty, else
      nothing; Enter consumed with `Event.Use()` when it acts. Thing = the spawned/held corpse for a dead colonist, else
      the pawn. Map view: caravan member -> `CameraJumper.TryJumpAndSelect(caravan)`; anyone else spawned or held by a
      spawned thing -> `Selector.ClearSelection(); Selector.Select(thing)` (`SelectInternal` itself switches the map and
      jumps the camera when the pawn is on another map); an unspawned target selects nothing (vanilla's fall-through to
      the map object under the cursor is a quirk we do not copy). World view: as `WorldSelector.SelectUnderMouse`
      (caravan member -> caravan; spawned -> `TryJumpAndSelect(thing)`; else `TryJump(thing)`). No camera jump on the
      current map (that is the double-click). Selection is never touched otherwise. Phase 3 scenarios: first result on
      the home map, on the second map, caravan member (reuse the Phase 1 map/caravan helpers). (user, 2026-11-14)
- [x] **Phase 2, open point 6 — reset on load** — `ViewState.SyncGame()` is called first in `BarAdapter.Apply()`
      (before its "not Playing" early return) and compares `Current.Game` **by reference** with the last game seen; any
      difference (new game, load from the menu or in game, quit to menu where it is `null`) resets query, status, skill,
      min level, sort and direction and bumps the criteria version, so the bar rebuilds from a fresh vanilla cache. A map
      change does not reset (Phase 5). The Phase 3 search widget lives in the window instance (a new window opens after
      every load), never in a static. New scenario `View reset on load` (added to Phase 2): set a query, a status and a sort,
      `SaveAndReload`, assert `ViewState` at defaults, bar vanilla, entries equal the vanilla set. Failure mode: the
      static survives a load (a hidden roster in a fresh game behind a blank search field). It also covers the "reload
      resets state" half of the plan's `No save data` row. (user, 2026-11-14)

Defaults taken unless told otherwise: sorting is per group (map/caravan); next/prev-colonist hotkeys and
drag-box selection follow the filtered view (they read the same list); drag-reorder disabled while a
filter/sort is active; skill presets Any/0/5/10/15; packageId `josh9817.colonycommand`.

## Phase 0 — Repo bootstrap

- [x] git init, adopt modding-center `tools/` + docs, `.gitignore`, `AGENTS.md` (testing rule) — first commit
- [x] Formal plan (`docs/plan.md`) and this TODO
- [x] Resolve Q1–Q4
- [x] Decompiled source in `.tools/decomp/` (copied from social-graphs-mod-rw, 1.6.4871 rev598 = installed game;
      the modding center has none — `.tools/` is git-ignored there)
- [x] Mod skeleton: `About/About.xml` (1.6, no dependencies), `Source/ColonyCommand/ColonyCommand.csproj`
      (`net472`, `Krafs.Rimworld.Ref 1.6.4871`, no Harmony reference), empty `Mod` class; `tools/build.sh` passes
      (build + lint clean; the DLL's assembly references are `mscorlib` and `Assembly-CSharp` only, read with
      `System.Reflection.Metadata`)
- [x] Test-only mod skeleton `Tests/ColonyCommandTests/` (adapted `AutoTest.cs`; About.xml declares Harmony as a
      dependency so it loads even if unused) wired through
      `MOD_EXTRA_MODS`; check how `autotest.py`/`build.sh` must build it (tooling change → port rule)
      (proven by run `Harness wiring`, `docs/test-runs.md` 2026-11-15 00:58. `build.sh` now also builds
      `Tests/<MOD_NAME>Tests`, `package.sh` skips it, `autotest.py` links repo-local test mods into `Mods/`,
      `MOD_EXTRA_MODS` defaults to `<MOD_ID>tests`, `MOD_SCREEN` to 800x500; local to this repo, see Q5)
- [x] `docs/test-runs.md`; `tools/autotest.py baseline` (6 vanilla-noise signatures, no `[ColonyCommand]` in the log)

## Phase 1 — Prove the zero-dependency seam (spec §6.1 step 1) — go/no-go gate

All proofs: `docs/test-runs.md` rows 2026-11-15 01:08–01:15; evidence and numbers in `docs/plan.md` §1.5.

- [x] `Bootstrap` MonoBehaviour opens an empty `ColonyCommandWindow` while Playing; gone on main menu; reopened after load
      (`Seam bootstrap`: one window and one Bootstrap; closed window reopens; save → main menu, 40 frames, 0 windows →
      load → one window. Created from a `[StaticConstructorOnStartup]` class, not the `Mod` constructor: that runs on a
      loader thread)
- [x] `BarAdapter`: resolve `cachedScale` by reflection (fail closed + one log error); apply a hard-coded
      "hide every other colonist" view in `ExtraOnGUI`/`WindowUpdate`; relayout via own `ColonistBarDrawLocsFinder`
      (`Seam view`: scale 0.77 → 1.00, exact view then exact vanilla restore; `Seam fail closed`: missing member and an
      exception each log exactly one error and hand the bar back. **Deviation:** the primary hook is
      `Bootstrap.LateUpdate`; `ExtraOnGUI`/`WindowUpdate` are the safety net — `WindowUpdate` runs before the ticks)
- [x] Prove: no full-roster flash after a dirty; click/hit-test lands on the visible pawn; drag reorder is a no-op;
      `displayOrder` unchanged; no errors on map switch / world view / caravan group
      (`Seam dirty`: control 39/39 wrong, hooks 0/59; `Seam hit test`: real clicks; `Seam reorder`: control reaches
      `Reorder`, view 0 calls; `Seam topology`: world view + caravan group; `Seam map switch`: second player map)
- [x] Prove no save data: saved file contains no `ColonyCommand` text
      (`Seam no save data`: the header lists the mod by packageId, as vanilla does for every mod, and is read as a
      control; the game body has no `colonycommand` text)
- [x] **Gate:** if any proof fails → stop and ask the user how to reduce the feature (no Harmony, Q2)
      — **passed, no proof failed; no reduction needed.** The shipped assembly still references no Harmony.

**Next:** Phase 2 (`ViewState`, `Predicates`, `RosterView`; the probe view in `BarAdapter` is replaced by the real one,
and the `Seam *` scenarios stay as the seam's regression set).
Hand-off for Phases 2–4 (verified engine facts, spec map, plan of attack, open points):
[`docs/handoff/20261115-0124-phases-2-to-4.md`](docs/handoff/20261115-0124-phases-2-to-4.md).
Its six open points are **decided** (user, 2026-11-14): see "Phase 2, open point 1–6" under Decisions above; where the
hand-off's suggestion differs, the Decisions entry wins.

## Phase 2 — Roster snapshot and predicates (spec §3, §4)

- [ ] `ViewState` (memory only) and `Predicates` (name, drafted, injured, idle, skill, mood)
- [ ] `RosterView`: filter pipeline, per-group stable sort (tie → vanilla index), chip counts
- [ ] `BarAdapter` uses `RosterView`; identity view when nothing is active
- [ ] Test fixtures: colonists only (names, drafted, injury, skills, mood offset)
- [ ] Scenarios `Name search`, `Status chips`, `Combined filter`, `Sort and restore`, `Bar integrity`,
      `View reset on load` (added with open point 6)

## Phase 3 — Search field and filter strip UI (spec §2)

- [ ] `QuickSearchWidget` right of the bar; `CommonSearchWidget` override (hotkeys suppressed while typing);
      Esc unfocuses; × clears query only; Enter selects first visible pawn (normal selection semantics)
- [ ] Chip row under the bar's lowest row: All / Drafted / Injured / Idle with counts; one active; click again toggles off
- [ ] "No matching colonists" empty state with reset
- [ ] Keyed translations (`Languages/English/Keyed`)
- [ ] Responsive: collapse search to an icon and chips into a Filters menu when space is short; never overlap portraits or alerts

## Phase 4 — Skill filter, sorting, reset (spec §4)

- [ ] Skill dropdown (FloatMenu over `DefDatabase<SkillDef>`) + min level presets Any/0/5/10/15
- [ ] Sort dropdown: Vanilla / Name / Mood / Selected skill, with direction; Skill sort only when a skill is chosen
- [ ] Reset control (clears everything back to vanilla view)

## Phase 5 — Live refresh and lifecycle (spec §4.4)

- [ ] Throttled re-evaluation (~0.25 s real time) for drafted/injured/idle/mood/skill changes
- [ ] Roster mutation (join, death, leave map), map switch, world view, main menu → no stale pawns
- [ ] Scenario `Live refresh`

## Phase 6 — Acceptance and release (spec §6)

- [ ] Scenario `No save data` + `tools/autotest.py removal-check`
- [ ] Package (`tools/package.sh`); Core-only load check of the package with **no Harmony and no DLC** —
      user-in-the-loop, because `autotest.py` always loads Harmony
- [ ] User-in-the-loop: 1280×720, 1920×1080, ultrawide × UI scale 100/125/150 % — screenshots
- [ ] Error log clean; large-colony responsiveness spot check
- [ ] Release gate (user-approved)

## Noted, out of scope (spec §6.2)

Passion filter, work groups, pinned colonists, overview panel, custom shortcuts, compatibility patches.
