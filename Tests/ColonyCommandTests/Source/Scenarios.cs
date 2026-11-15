using System;
using System.Collections.Generic;
using System.Linq;
using ColonyCommand;
using Verse;

namespace ColonyCommandTests
{
    /// <summary>Every scenario, in list order. Each setup builds what it needs on the current (quicktest) map.</summary>
    public static partial class Scenarios
    {
        public const string TestsPackageId = "josh9817.colonycommandtests";

        public static readonly List<(string name, Action setUp)> All = new List<(string, Action)>
        {
            ("Harness wiring", HarnessWiring),
            ("Seam bootstrap", SeamBootstrap),
            ("Seam view", SeamView),
            ("Seam dirty", SeamDirty),
            ("Seam hit test", SeamHitTest),
            ("Seam reorder", SeamReorder),
            ("Seam topology", SeamTopology),
            ("Seam map switch", SeamMapSwitch),
            ("Seam no save data", SeamNoSaveData),
            ("Seam fail closed", SeamFailClosed),
            ("Name search", NameSearch),
            ("Status chips", StatusChips),
            ("Combined filter", CombinedFilter),
            ("Sort and restore", SortAndRestore),
            ("Bar integrity", BarIntegrity),
            ("View reset on load", ViewResetOnLoad),
            ("Search field", SearchField),
            ("Search Enter", SearchEnter),
            ("Chip clicks", ChipClicks),
            ("Empty state", EmptyState),
            ("Enter across maps", EnterAcrossMaps),
            ("Controls placement", ControlsPlacement),
            ("Skill filter", SkillFilter),
            ("Sort controls", SortControls),
            ("Reset control", ResetControl),
            ("Live refresh", LiveRefresh),
            ("Live refresh across views", LiveRefreshAcrossViews),
            ("No save data - Prepare removal", NoSaveDataPrepareRemoval),
        };

        /// <summary>
        /// Runs the scenarios named by the comma-separated parts of <paramref name="filter"/>, then calls
        /// <paramref name="done"/> with the PASS count and verdicts. A part that is a whole scenario name
        /// (case-insensitive) selects exactly that scenario; any other part selects the names containing it.
        /// Routine runs name exact scenarios (AGENTS.md: no full sequences); an empty filter runs everything and is
        /// reserved for the user-authorized release gate.
        /// </summary>
        public static void RunAll(string filter, bool testSaveLoad, Action<int, List<string>> done)
        {
            string[] parts = (filter ?? "").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            bool Selected(string name) => parts.Any(p => All.Any(s => string.Equals(s.name, p, StringComparison.OrdinalIgnoreCase))
                ? string.Equals(name, p, StringComparison.OrdinalIgnoreCase)
                : name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
            Scenario.RunAll(All.Where(s => parts.Length == 0 || Selected(s.name)), testSaveLoad, done);
        }

        // Failure modes: the test mod loads before the main assembly exists (it then can't bind to it), or the shipped
        // assembly gains a Harmony reference (user decision Q2: Core-only, never Harmony, not even as a fallback).
        // Precondition: the quicktest map is up. Trigger: none, the state is checked as loaded.
        private static void HarnessWiring()
        {
            new Scenario("Harness wiring", timeoutTicks: 600)
                .Expect("the main mod is running and loads before the test mod", () =>
                {
                    List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;
                    int main = mods.FindIndex(m => string.Equals(m.PackageId, CC.PackageId, StringComparison.OrdinalIgnoreCase));
                    int tests = mods.FindIndex(m => string.Equals(m.PackageId, TestsPackageId, StringComparison.OrdinalIgnoreCase));
                    if (main < 0 || tests < 0)
                        throw new InvalidOperationException($"not running: main {main}, tests {tests}");
                    if (main > tests)
                        throw new InvalidOperationException($"load order: main {main} is after tests {tests}");
                    return true;
                })
                .Expect("the shipped assembly references no Harmony", () =>
                {
                    string[] harmony = typeof(CC).Assembly.GetReferencedAssemblies().Select(a => a.Name)
                        .Where(n => n.IndexOf("Harmony", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                    if (harmony.Length > 0)
                        throw new InvalidOperationException($"ColonyCommand.dll references {string.Join(", ", harmony)}");
                    return true;
                })
                .Start();
        }
    }
}
