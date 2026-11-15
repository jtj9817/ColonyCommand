# Repository Guidelines

## Project Overview

**Colony Command** — a RimWorld **1.6**, Core-only, UI-only C# mod that adds name search, status/skill filters
and sorting to the vanilla top colonist bar. Spec: `Colony_Command_Specs.docx`. Plan and progress: `TODO.md`.

Hard constraints (from the spec): no DLC requirements, no other mods required at runtime, **no save data**
(no `ExposeData`, no GameComponent/MapComponent/WorldComponent, no Defs), never mutates pawns, work, jobs or
`playerSettings.displayOrder`.

## Tooling

`tools/` is adopted from `/mnt/0B8533211952FCF2/rimworld-modding-center` (commit `0542d72`); `docs/playbook.md`,
`docs/testing.md`, `docs/gotchas.md`, `docs/tooling.md` and `templates/handoff-TEMPLATE.md` are snapshots of the
same commit. The center is the source of truth for general tooling fixes (porting rule: fix there, port here).

- Mount is root-owned: always `git -c safe.directory="$PWD" ...`.
- `tools/vanilla.sh init` once, then `tools/vanilla.sh grep <regex>` — look up vanilla source before guessing.
- Build: `tools/build.sh`. Tests: `tools/autotest.py run "<exact scenario names>"`.


## Testing scope (strict rule — no exceptions beyond the one named here)

- **No full sequences.** Every test run names the exact scenarios the current change needs:
  `tools/autotest.py run "<exact scenario names>"`. Never `run` or `pkg-run` with an empty filter, never the
  in-game "Run all", never every scenario in sequence "to check everything". After a fix, rerun only the
  scenarios that failed plus any whose code path the fix touched.
- **No scope creep.** Test only what the current change or plan target touches. Don't add scenarios, fixtures or
  assertions for features outside the target, and don't fix or test unrelated problems found along the way:
  note them in `TODO.md` and ask the user.
- **Well-defined state only.** A scenario that runs builds its own fixtures from a known start, states its
  precondition, its trigger and its expected outcome, asserts on its own fixture targets (never global map
  state), and removes its fixtures. It never depends on what an earlier scenario left behind.
- **Every scenario targets a failure mode.** Each run scenario names the way the change could break (wrong
  result, exception, null/despawned/dead target, missing def, save/load round trip, removal) and asserts either
  the correct result or a graceful failure (no exception, no new red error, a clean fallback). A scenario that
  catches nothing the change could break is not run.
- **Record why.** Each `docs/test-runs.md` row lists the scenarios run and the failure mode each one covers.
- **Sole exception: the release gate.** A full "Run all" on the packaged copy (`tools/autotest.py pkg-run`) is
  allowed only as the release gate, started by the user or with their explicit approval for that release.
