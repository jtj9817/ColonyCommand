using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyCommand
{
    /// <summary>
    /// A Unity object on a DontDestroyOnLoad GameObject, so it is never part of a save. It keeps the window open while
    /// a game is being played (gone on the main menu, reopened after every load) and applies the bar view each frame
    /// after the game's ticks.
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        private void Update()
        {
            EnsureWindow();
        }

        /// <summary>
        /// LateUpdate runs after every Update of the frame, so after the ticks that mark the colonist bar dirty, and
        /// before any OnGUI event of the frame: the bar is never drawn or clicked in its vanilla state.
        /// </summary>
        private void LateUpdate()
        {
            BarAdapter.Apply();
        }

        private static void EnsureWindow()
        {
            if (Current.ProgramState != ProgramState.Playing || LongEventHandler.ShouldWaitForEvent
                || !(Find.UIRoot is UIRoot_Play))
                return;
            WindowStack windows = Find.WindowStack;
            if (windows != null && !windows.IsOpen<ColonyCommandWindow>())
                windows.Add(new ColonyCommandWindow());
        }
    }
}
