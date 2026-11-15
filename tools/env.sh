# Shared paths and helpers, sourced by tools/*.sh. Override any path via the environment.
# tools/autotest.py reads the same variables with the same defaults; keep the two in step.
#
# Adopting this toolkit in a mod repo: copy tools/ there and set the three MOD_* values below
# (or always export them). Unset values fail loudly with "set MOD_NAME and MOD_ID" so a copy
# can never silently act on the wrong mod. See docs/tooling.md in rimworld-modding-center.

die() { echo "error: $*" >&2; exit 1; }

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HERE="$REPO/tools"

# --- the mod this toolkit serves (edit when adopting) ---------------------------------------------------------
MOD_NAME="${MOD_NAME:-ColonyCommand}"   # assembly/directory/display name, e.g. Quadrunner or QueenBee
MOD_ID="${MOD_ID:-josh9817.colonycommand}"       # packageId from About.xml, e.g. josh9817.quadrunner
MOD_LOOP="${MOD_LOOP:-autotest}" # autotest: the agent drives an isolated test instance (tools/autotest.py)
                                 # user: the user plays every in-game step (log tools: tools/qblog.sh)
[[ "$MOD_NAME" != SET-ME && "$MOD_ID" != SET-ME ]] || die "set MOD_NAME and MOD_ID in $HERE/env.sh (see docs/tooling.md)"
MOD_TAG="${MOD_TAG:-[$MOD_NAME]}"   # the mod's Player.log prefix, e.g. [Quadrunner]
MOD_DEF_PREFIX="${MOD_DEF_PREFIX:-}" # your defNames' prefix, e.g. QR_ (read by the removal probe; empty skips that check)

# --- paths ----------------------------------------------------------------------------------------------------
DEVLOG="${MOD_DEVLOG:-$REPO/.devlog}"   # runs, baseline, last build id, test profile (git-ignored)
LOCAL_TOOLS="$REPO/.tools"              # ilspycmd, decompiled source (git-ignored)

RW_DIR="${RW_DIR:-$HOME/.steam/debian-installation/steamapps/common/RimWorld}"
# In the autotest loop RW_DATA is the TEST INSTANCE's profile (-savedatafolder), never the user's.
# In the user loop it is the user's own profile, which every tool only ever reads.
USER_RW_DATA="${USER_RW_DATA:-$HOME/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios}"
if [[ "$MOD_LOOP" == autotest ]]; then RW_DATA="${RW_DATA:-$DEVLOG/rwdata}"; else RW_DATA="${RW_DATA:-$USER_RW_DATA}"; fi
RW_LOG="$RW_DATA/Player.log"
RW_PREV_LOG="$RW_DATA/Player-prev.log"
RW_EXPECTED_VERSION="${RW_EXPECTED_VERSION:-1.6.}"    # Version.txt prefix this project is built against
HARMONY_DIR="${HARMONY_DIR:-$RW_DIR/../../workshop/content/294100/2009463077}"

# Test-instance tuning (autotest loop; read by autotest.py — keep both in step)
MOD_MAP_SIZE="${MOD_MAP_SIZE:-200}"        # quicktest map size (empty: vanilla 250)
MOD_PKG="${MOD_PKG:-$REPO/dist/$MOD_NAME}" # packaged release copy for autotest.py pkg-run (tools/package.sh)
MOD_SCREEN="${MOD_SCREEN:-800x800}"        # test instance window (software rendering: smaller is cheaper). Vanilla hides the
                                           # colonist bar below 800x500 (ColonistBar.Visible); this repo needs 800 px of height: the
                                           # whole filter panel (right column, under the Learning Helper) needs ~290 px, above the
                                           # alerts (~140 px with a dozen colonists) and the weather/letters block, which starts
                                           # ~285 px above the bottom edge
MOD_RENDER_THREADS="${MOD_RENDER_THREADS:-4}"  # llvmpipe threads, LP_NUM_THREADS (empty: one per core)
# Test-only packageIds loaded after the mod (comma-separated); a change restarts cold. Convention: a repo-local test mod
# Tests/<MOD_NAME>Tests with packageId <MOD_ID>tests is loaded by default (Harmony allowed there; the shipped assembly
# never has it); autotest.py links it into Mods/. autotest.py applies the same default when run without env.sh.
if [[ -d "$REPO/Tests/${MOD_NAME}Tests" ]]; then MOD_EXTRA_MODS="${MOD_EXTRA_MODS:-${MOD_ID}tests}"; else MOD_EXTRA_MODS="${MOD_EXTRA_MODS:-}"; fi
MOD_DEPS="${MOD_DEPS:-}"                   # required packageIds loaded before the mod, baseline included (comma-separated)

# tools/autotest.py reads these from the environment, also when a shell tool (status.sh) calls it.
export MOD_NAME MOD_ID MOD_TAG MOD_DEF_PREFIX MOD_MAP_SIZE MOD_PKG MOD_SCREEN MOD_RENDER_THREADS MOD_EXTRA_MODS MOD_DEPS

mkdir -p "$DEVLOG"

git_() { git -c safe.directory="$REPO" -C "$REPO" "$@"; }
# Test instance: the RimWorldLinux started with our -savedatafolder. User's game: any other RimWorldLinux.
test_pid() { pgrep -f -- "-savedatafolder=$RW_DATA" | head -1 || true; }
user_game_pid() { pgrep -x RimWorldLinux | grep -vxF "$(test_pid)" | head -1 || true; }
# The game this repo's loop interacts with: the test instance (autotest) or the user's game (user).
game_pid() { if [[ "$MOD_LOOP" == autotest ]]; then test_pid; else pgrep -x RimWorldLinux || true; fi; }
