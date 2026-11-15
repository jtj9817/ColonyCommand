# Colony Command visual assets

These assets document the RimWorld 1.6, Core-only, UI-only mod. Colony Command adds a name search, status chips (Drafted, Injured, Idle and Inspired), a skill filter and per-group sorting to the vanilla colonist bar. The panel sits in the right column under the Learning Helper and folds into a compact search and Filters menu when space is limited. Filters change the displayed roster; they do not edit colonists or persist UI state in saves.

## Gameplay screenshots

The screenshots are **actual game captures**, not generated mockups. They are selected from the 99 images in the isolated Phase 7 autotest run documented in [the test log](../docs/test-runs.md): 2026-11-15 07:36, build `1041c2b-dirty.20261115T073325Z` (the final Phase 7 code, later committed as `9ff99d6` and `f02844e`). The run reported 11/11 scenarios passing and zero new errors. These are 400 × 400 copies from the capture set; they are downsampled from the 800 × 800 test window, so small text is not always legible. The test harness loads a separate test-only mod and Harmony; the released mod does not require either.

| Asset | Scenario / visible state | Original frame |
| --- | --- | --- |
| [Panel overview](screenshots/01-panel-overview.png) | Expanded panel with status counts and vanilla sort | `0010.png` |
| [Inspired before](screenshots/02-inspired-before.png) | Inspired filter scenario: ten fixture colonists before selecting Inspired | `0024.png` |
| [Inspired selected](screenshots/03-inspired-filter.png) | Inspired filter scenario: three matching colonists | `0033.png` |
| [Empty search](screenshots/04-empty-search.png) | Empty state: no matches and Reset available | `0041.png` |
| [Compact panel](screenshots/05-compact-panel.png) | Controls placement: the expanded Learning Helper leaves room for the folded controls | `0051.png` |
| [Skill filter](screenshots/06-skill-filter.png) | Skill filter: Social with a 10+ threshold | `0069.png` |
| [Sort menu](screenshots/07-sort-menu.png) | Sort controls: ordering options | `0086.png` |

The source images are local, ignored run artifacts in `.devlog/screens/final/`; the selected copies here are intended to remain available with the repository.

## Animated demonstrations

[Inspired filter](animations/inspired-filter.gif) and [skill threshold](animations/skill-filter.gif) are **reconstructions from sampled screenshots**, not continuous screen recordings. `ffmpeg` assembled consecutive images from the same Phase 7 run at an illustrative two frames per second. Original capture intervals were not retained, so playback speed and pauses do not represent real interaction timing. The source frames were `0024–0039` and `0060–0083`, respectively. No visual states were generated or invented.

For example, the Inspired animation was assembled from the local source captures with:

```sh
ffmpeg -framerate 2 -start_number 24 -i .devlog/screens/final/%04d.png -frames:v 16 \
  -filter_complex '[0:v]split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a' \
  -loop 0 assets/animations/inspired-filter.gif
```

A fresh capture attempt could not start in this environment: the isolated harness reported that Steam was not running, and the desktop display was unavailable. The screenshots and GIFs therefore reuse the recorded Phase 7 run; they do not constitute a new test run or the separate Core-only and display-scale user checks in `TODO.md`.
