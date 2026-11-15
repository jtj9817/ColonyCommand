using System.Collections.Generic;
using System.Linq;
using ColonyCommand;
using RimWorld;
using Verse;

namespace ColonyCommandTests
{
    /// <summary>
    /// Watches the main menu for a scenario that goes there and back. The scenario runner only steps while a map is up,
    /// so this runs from AutoTest.Update (which always runs): it counts the frames spent on the main menu, how many
    /// Colony Command windows were open in them, then loads the given save and reports when the game is back.
    /// </summary>
    public static class MenuProbe
    {
        private const int MenuFrames = 40;

        public static bool Active { get; private set; }
        public static bool Finished { get; private set; }
        public static int Frames { get; private set; }
        public static int WindowsSeen { get; private set; }

        /// <summary>
        /// What the mod's statics held on the main menu (<see cref="StaleReferences.Scan"/>, taken at the first menu frame the
        /// scan runs on), or null while no scan has run. On the menu there is no game, so any held game or pawn is stale.
        /// </summary>
        public static List<string> HeldOnMenu { get; private set; }

        private static bool loading;
        private static string save;

        public static void Begin(string saveName)
        {
            Active = true;
            Finished = false;
            Frames = 0;
            WindowsSeen = 0;
            HeldOnMenu = null;
            loading = false;
            save = saveName;
        }

        public static void Update()
        {
            if (!Active)
                return;
            if (!loading)
            {
                // Right after GoToMainMenu the game is still Playing for a few frames: only count real main-menu frames.
                if (Current.ProgramState != ProgramState.Entry || LongEventHandler.ShouldWaitForEvent)
                    return;
                Frames++;
                WindowsSeen += Find.WindowStack?.Windows.Count(w => w is ColonyCommandWindow) ?? 0;
                if (Frames == MenuFrames / 2)
                    HeldOnMenu = StaleReferences.Scan();   // after the bootstrap's LateUpdate has seen the menu for a while
                if (Frames >= MenuFrames)
                {
                    loading = true;
                    GameDataSaveLoader.LoadGame(save);
                }
            }
            else if (AutoTest.MapReady)
            {
                Finished = true;
                Active = false;
            }
        }
    }
}
