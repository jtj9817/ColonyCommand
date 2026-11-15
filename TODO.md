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

Proof: `docs/test-runs.md` rows 2026-11-15 02:12–02:16 (clean build `080d033`: 11/11 PASS; mutation checks 02:12–02:13).

- [x] `ViewState` (memory only, `SyncGame` reset on a new game, `Version` counter, `Criteria` snapshot) and `Predicates`
      (name, drafted, injured, idle chain, skill, mood) — `Name search`, `Status chips` (idle oracle = the bar's own icon),
      `Combined filter`, `View reset on load`
- [x] `RosterView`: filter pipeline, per-group stable sort (tie → vanilla index, both directions, unavailable last), chip
      counts — `Sort and restore`, `Status chips`
- [x] `BarAdapter` uses `RosterView`; identity view when nothing is active — `Name search` (whitespace-only query and
      back = the untouched vanilla bar), `Sort and restore`, `Bar integrity` (a real drag on the vanilla bar still reorders)
- [x] Test fixtures: colonists only (names incl. a `NameSingle`, drafted, injury and scar, skill levels, mood, job tag,
      a dead colonist) — `Fixtures.cs`, the `Cast` in `Scenarios.Phase2.cs`
- [x] Scenarios `Name search`, `Status chips`, `Combined filter`, `Sort and restore`, `Bar integrity`,
      `View reset on load` (each with a stated failure mode; mutation-checked: case-sensitive match, flipped tie order
      and ordinal name sort were each caught)
- [x] Delete the probe (`BarAdapter.ProbeView`/`ProbeKeeps`, the debug action, the probe-based oracle) in one dedicated
      commit after migrating the `Seam *` scenarios to the real view (decision "open point 2" above) — commit `04f371f`;
      run 2026-11-15 02:20: all nine `Seam *` scenarios and the six Phase 2 scenarios 15/15 PASS against the real view

Defaults taken in Phase 2 (record; change only if the user says so):

- **Name match** covers the displayed name (`LabelShort`), `First`/`Nick`/`Last` or a single name, **and the full name**
  (`ToStringFull`, so "first last" typed with a space matches). Trim, `OrdinalIgnoreCase` substring.
- **Skill criterion** = a chosen skill **and** a minimum level; level `Any` (`ViewState.AnyLevel`) switches the criterion
  off, so choosing a skill alone only feeds the skill sort and hides nobody. Level 0 still excludes pawns for whom the
  skill is unavailable (`TotallyDisabled`, no skills, dead). The All chip drops status and the minimum level and keeps the
  query and the chosen skill (`ViewState.ClearStatusAndSkill`).
- **Skill sort without a skill** is the vanilla order (`Criteria.EffectiveSort`); clearing the skill resets a skill sort.
- **Sort direction** is one flag: name Z-A, or high-to-low for mood and skill. Missing values are last in both directions;
  ties always go back to the vanilla position, whichever the direction.

## Phase 3 — Search field and filter strip UI (spec §2)

Proof: `docs/test-runs.md` row 2026-11-15 03:22 (clean build `f11e93f`, 21/21 PASS); development notes in the 02:31-03:19 rows.

- [x] `QuickSearchWidget` beside the bar; `CommonSearchWidget` override (hotkeys suppressed while typing); Esc unfocuses;
      × clears query only; Enter selects the first visible pawn (normal selection semantics) — `Search field`,
      `Search Enter`, `Enter across maps` (second map, caravan member, world view; Phase 2 open point 5)
- [x] Chip row under the bar: All / Drafted / Injured / Idle with live counts; one active; click again toggles off — `Chip
      clicks` (real clicks; the row does not move while filtering)
- [x] "No matching colonists" empty state with Reset — `Empty state` (real typing, real Reset click)
- [x] Keyed translations (`Languages/English/Keyed/ColonyCommand.xml`; they render in game: the screenshots and the
      scenarios use them). Whether `rwdefs.py lint` reads Keyed files stays unverified
- [x] Responsive: below 800x500 nothing is placed and nothing takes input (`Controls placement`: pure layout + the bar
      switched off); the search collapses to an icon that opens the field in a row under the chips when the free span beside
      the bar (right limit = screen width - 154 px alert column) is under 170 px — **this is the normal case at 800x500**
      and is what every real-input scenario runs in; the chips fold into one "Filters..." button when they do not fit
      (geometry proven by 1344 synthetic cases; the menu itself is not exercised in game because four chips always fit at
      800 px — user-in-the-loop, Phase 6); nothing overlaps a portrait, label, group frame, the alert column or another
      control (`Controls placement`: 4032 synthetic cases + real bars of 3-24 colonists and two groups)

Defaults taken in Phase 3 (record; change only if the user says so):

- **Where the search goes:** the spec puts it "right of the portrait area when width permits". Vanilla's alert column is 154
  px wide at the right edge and can grow to the top of the screen, and the bar leaves only 260 px per side, so beside a full bar
  there are ~90 px. The field is inline only when >= 170 px are free (screens from ~960 px with a narrow bar); otherwise a
  search icon stands in and its opened field is a row under the chips. A field holding a query is never hidden.
- **The chip row is anchored to the unfiltered bar** (its footprint measured from the vanilla layout at every build) and
  never sits higher than under a one-row bar at scale 1 with a weapon icon (y ~119): filtering changes the portrait size and
  row count, and a row that followed them would move out from under the cursor. Cost: a gap under small bars.
- **Chip counts and the view are re-evaluated every 0.25 s real time** (`BarAdapter.RefreshSeconds`), so the counts follow the
  game. This is Phase 5's throttle, done early because the chips need it; Phase 5's boxes stay open until `Live refresh`
  proves them.
- **Enter in a focused field with a non-blank query** selects the first result; afterwards vanilla's own windows (the inspect
  pane, a map change) take the keyboard focus out of the field.
- **The mod never touches the bar while a game loads** (`BarAdapter.GameReady`); a transient exception there used to switch
  the seam off for the whole session.

Open observation (not a mod defect as far as known): in the harness a real click sometimes never reaches the window stack
(`ExtraOnGUI`) although the game's OnGUI root saw it; real-input scenarios retry the focusing click (attempts are in the
run summaries). Worth a look in the user-in-the-loop pass: does a real mouse ever miss the search icon or a chip?

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
