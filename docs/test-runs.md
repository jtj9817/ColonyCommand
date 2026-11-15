# Test runs

> Every automated test run, newest first: build, the exact scenarios (with the failure mode each one covers), verdict,
> and what the log showed.

| | |
| --- | --- |
| **Source** | `tools/autotest.py run` / `baseline` output (a ready-made `row:` line); full logs in git-ignored `.devlog/runs/` |
| **Rule** | AGENTS.md "Testing scope": named scenarios only, each with a failure mode; the user's game is never touched |
| **Environment** | RimWorld 1.6.4871 rev598, test instance on private Xvfb `:90`, 800×500, next to the user's open game |

One row per run, **newest first**. The `Scenarios` column is `name — failure mode it targets`.

| Run (UTC) | Target | Build | Scenarios | Result | Finding | Next |
| --- | --- | --- | --- | --- | --- | --- |
| 2026-11-15 00:58 | P0 test-only mod wiring | c8a34b4.20261115T005826Z | **Harness wiring** — the test mod loads before the main assembly exists (cannot bind to it), or the shipped `ColonyCommand.dll` gains a Harmony reference (Q2) | 1/1 PASS, 0 new errors (baseline 6 signatures) | Cold start to `ready` in 15 s at 800×500 (21 fps). Both mods running, main before tests; `ColonyCommand.dll` references no Harmony. Harness accepted build id and mod root. User's game (pid 3576609) untouched | Phase 1 seam |
| 2026-11-15 00:57–00:59 | P0 baseline (no `ColonyCommand`; Core + Harmony + `josh9817.colonycommandtests`) | n/a | none (error-signature baseline, 121 s) | 6 signatures recorded | Vanilla/engine noise only: allocator bucket stats, breakpad handler, libcurl. The test mod loaded without the main mod, logged `main mod not active: test driver idle` and nothing else (log holds no `[ColonyCommand]`, which `baseline` requires) | Wiring run |
