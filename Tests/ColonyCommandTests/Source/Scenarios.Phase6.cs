using System;
using System.IO;
using System.Linq;
using ColonyCommand;
using RimWorld;
using Verse;

namespace ColonyCommandTests
{
    // Phase 6: acceptance. The mod is UI-only and saves nothing, so "prepare removal" has nothing to strip: the scenario proves
    // that by saving after every part of the mod was used, and hands the save to `autotest.py removal-check`, which loads it
    // in a process WITHOUT the mod (and without this test mod) and checks the colony survives.
    public static partial class Scenarios
    {
        /// <summary>The name tools/autotest.py's removal-check loads: <c>REMOVAL_SAVE = f"{MOD_SLUG}_removal"</c>.</summary>
        private const string RemovalSave = "colonycommand_removal";

        // Precondition: the cast staged and paused; a view in place that uses every part of the state (name query, a skill and
        // a minimum level, a mood sort in its non-default direction), the window open. Trigger: save the game under the removal
        // name. Failure modes: the mod writes anything of its own into the save (a component, a scribed field, a def or type
        // reference), which would break a save once the mod is removed; the control (the header lists the mod, as vanilla
        // does for every mod) is missing, so the body check could not see a mod id at all; the vanilla colonists the
        // no-mod load must still find are not the ones written. Reload resets the state: `View reset on load`.
        private static void NoSaveDataPrepareRemoval()
        {
            var s = new Scenario("No save data - Prepare removal", 2000000);
            var r = new Roster();
            Cast c = Stage(s, r);
            ViewStep(s, r, c, "use every part of the state: token, skill >= 0, mood sort high to low", () =>
                {
                    ViewState.Query = Token;
                    ViewState.Skill = c.Skill;
                    ViewState.MinLevel = 0;
                    ViewState.Sort = SortKey.Mood;
                    ViewState.Descending = true;
                }, () => c.Ordered("A", "CD", "B", "E"));
            s.Do("the window is open", () => TheWindow())
                .Do("save under the removal name; the save holds nothing of the mod outside the mod list", () =>
                {
                    GameDataSaveLoader.SaveGame(RemovalSave);
                    string text = File.ReadAllText(GenFilePaths.FilePathForSavedGame(RemovalSave));
                    int end = text.IndexOf("</meta>", StringComparison.Ordinal);
                    if (end < 0)
                        throw new InvalidOperationException("no </meta> in the save: the check cannot tell header from body");
                    string header = text.Substring(0, end), body = text.Substring(end);
                    if (header.IndexOf(CC.PackageId, StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("control: the save header does not list the mod, so this check cannot see mod ids");
                    foreach (string needle in new[] { "colonycommand", "ViewState", "BarAdapter", "RosterView" })
                    {
                        int at = body.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                        if (at >= 0)
                            throw new InvalidOperationException(
                                $"the save body mentions '{needle}' at {at}: ...{body.Substring(Math.Max(0, at - 60), Math.Min(140, body.Length - Math.Max(0, at - 60)))}...");
                    }
                    r.Notes.Add($"save {text.Length / 1024} KiB, header lists the mod, body has no trace");
                })
                .Do("write the vanilla content the no-mod load must still find", () =>
                {
                    if (AutoTest.Dir == null)
                        throw new InvalidOperationException("no harness control directory: run this scenario through tools/autotest.py");
                    string[] ids = c.Living.Select(p => p.ThingID).Append(c.F.Corpse.ThingID).ToArray();
                    File.WriteAllLines(Path.Combine(AutoTest.Dir, "removal-expected.txt"), ids);
                    r.Notes.Add($"expected after removal: {ids.Length} things ({string.Join(", ", ids)})");
                })
                .Start();
        }
    }
}
