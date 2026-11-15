using LudeonTK;

namespace ColonyCommand
{
    /// <summary>Debug actions → Colony Command. Dev mode only; Phase 1 only (replaced by the real controls later).</summary>
    public static class DebugActions
    {
        [DebugAction("Colony Command", "Toggle probe view (hide every other colonist)", allowedGameStates = AllowedGameStates.Playing)]
        private static void ToggleProbeView()
        {
            BarAdapter.ProbeView = !BarAdapter.ProbeView;
            CC.Info($"probe view {(BarAdapter.ProbeView ? "on" : "off")}");
        }
    }
}
