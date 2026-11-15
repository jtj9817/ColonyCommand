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

The source images are local, ignored run artifacts in `.devlog/screens/final/`; the selected copies here are intended to remain available with the repository. The project README shows only the empty-state and compact-panel captures; the animations below cover the rest.

## Animated demonstrations

The six animations in [`animations/`](animations/) are **real screen recordings**, not reconstructions. They were captured
on 2026-11-15 from one run of the mod's own automated test scenarios in the isolated test instance (private Xvfb display,
800 × 800, software rendering). The mouse clicks on the panel's controls and the keystrokes in the search field are real
input that the scenarios send with `xdotool` to that display. The one exception is choosing an *option* inside an open
dropdown (Mining, 0+, Name, Mood and so on): the scenario runs that option's action in code, as a click would, so the
pointer stays on the button that opened the menu and never travels to the list. The run was `tools/autotest.py run` of six
exactly named scenarios: build `fd5b6d3.20261115T094851Z`, **6/6 PASS, 0 new errors** (see
[the test log](../docs/test-runs.md)). The test harness loads a separate test-only mod and Harmony; the released mod
requires neither.

| Animation | Scenario | What is real input, and what the test sets up |
| --- | --- | --- |
| [Search by name](animations/search-by-name.gif) | Search field | Real click in the field, then `quill` typed at 120 ms per letter. The test staged six fixture colonists next to the map's three |
| [Status filters](animations/status-filters.gif) | Chip clicks | Real clicks on Drafted (and a second one that turns it off), Injured and Idle. The test put the name token `fx` in the field to isolate its six fixtures |
| [Inspired filter](animations/inspired-filter.gif) | Inspired filter | Real clicks on Inspired, twice. The test staged ten colonists, gave three an inspiration, and set the token `fxinsp` |
| [Skill filter](animations/skill-filter.gif) | Skill filter | Real clicks on the Skill and Level buttons, which open the dropdowns; the options (Mining, 0+, 5+, 10+, 15+, Any) are chosen in code. The test set the token `fx` |
| [Sort menu](animations/sort-menu.gif) | Sort controls | Real clicks on the Sort button and the direction button; the options (Name, Mood, Melee skill, Vanilla order) are chosen in code. No token: all nine colonists |
| [Reset](animations/reset.gif) | Reset control | One real click on Reset. The test had set the query, Injured, Mining 5+ and a Mood sort in code beforehand |

The fixtures are test colonists (Quill, Zan, Brisk, Lan, BZfxsolo, one corpse) plus the map's own three; their skills,
injuries and drafted state come from the scenario's staging. A clip is a crop of the top-right corner of the 800 × 800
window (560 px wide, 270 to 420 px tall) enlarged to 800 px.

### How they were made

The raw recording and the scripts are local, ignored artifacts in `.devlog/recordings/` (`demo-20261115.mkv`, `record.sh`,
`build_gif.py`, `all.sh`). In short:

1. With the test instance running, the pointer was set to an arrow (`xsetroot -display :90 -cursor_name left_ptr`) and the
   harness display was recorded losslessly enough to cut from, while the six scenarios ran:

   ```sh
   ffmpeg -f x11grab -framerate 25 -video_size 560x560 -draw_mouse 1 -i :90.0+240,0 \
     -c:v libx264 -preset ultrafast -crf 6 -pix_fmt yuv444p demo.mkv &
   tools/autotest.py run "Search field, Chip clicks, Inspired filter, Skill filter, Sort controls, Reset control"
   ```

2. Each clip was cut out of that one recording, cropped, and its static stretches dropped (see below):

   ```sh
   ffmpeg -ss START -to END -i demo.mkv -vf "crop=560:270:0:0,mpdecimate=hi=96:lo=48:frac=0.2" -vsync vfr frames/%05d.png
   ```

3. The kept frames were encoded with a per-clip palette:

   ```sh
   ffmpeg -f concat -safe 0 -i frames/list.txt -vsync vfr -loop 0 \
     -vf 'scale=800:-2:flags=lanczos,split[a][b];[a]palettegen=max_colors=96:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=3:diff_mode=rectangle' \
     clip.gif
   ```

| Clip | Window in the recording (s) |
| --- | --- |
| Search by name | 18.55 to 20.7 |
| Status filters | 23.45 to 42.9 |
| Inspired filter | 46.4 to 63.3 |
| Skill filter | 81.3 to 132.8 |
| Sort menu | 134.6 to 156.2 |
| Reset | 157.03 to 160.6, without 157.55 to 159.6 |

### What was edited, and what was not

- **Time is compressed.** The scenarios wait on the game between steps (the Status filters window is 19 s long, the Inspired
  window 17 s, the Skill window 51 s). `mpdecimate` removed the frames where nothing changed, and every remaining pause is
  capped at 1.1 s, with 1.5 s held on the last frame. Typing and clicks keep their real pacing; the pauses between them
  do not represent real interaction timing.
- **One cut.** The Reset clip leaves out 157.55 to 159.6 s, a drag test the scenario runs on a portrait. Nothing else is cut.
- **No state was generated or composited.** The frames are the game's own output, cropped and scaled. The pointer is the
  X11 arrow drawn by `ffmpeg`, not RimWorld's cursor.
- A first pilot run of `Chip clicks` alone (1/1 PASS) was used to tune the capture and is not part of any clip.
