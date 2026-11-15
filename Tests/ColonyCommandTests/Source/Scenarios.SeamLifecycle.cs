using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ColonyCommand;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace ColonyCommandTests
{
    public static partial class Scenarios
    {
        private static int Windows() => Find.WindowStack.Windows.Count(w => w is ColonyCommandWindow);

        private static ColonyCommandWindow TheWindow()
        {
            if (Windows() != 1)
                throw new InvalidOperationException($"{Windows()} Colony Command windows are open, expected 1");
            return Find.WindowStack.Windows.OfType<ColonyCommandWindow>().Single();
        }

        // Precondition: a game is being played. Trigger: close the window; save and go to the main menu; load again.
        // Failure mode: no window or a duplicate, a window that is not reopened after it is closed or after a load, a window
        // (or a second Bootstrap) alive on the main menu.
        private static void SeamBootstrap()
        {
            var s = new Scenario("Seam bootstrap", 600000);
            ColonyCommandWindow first = null;
            s.ExpectWithin("the bootstrap opened exactly one window", 20, () => Windows() == 1, () => $"{Windows()} windows")
                .Do("close it", () =>
                {
                    first = TheWindow();
                    first.Close(false);
                })
                .ExpectWithin("a new window is open again, exactly one", 20, () => Windows() == 1 && TheWindow() != first, () => $"{Windows()} windows")
                .Do("exactly one Bootstrap object", () => CheckOneBootstrap())
                .Do("save, then go to the main menu and load that save again", () =>
                {
                    GameDataSaveLoader.SaveGame(Scenario.SaveName);
                    MenuProbe.Begin(Scenario.SaveName);
                    GenScene.GoToMainMenu();
                })
                .ExpectWithin("the game is back from the main menu", 180, () => MenuProbe.Finished, () => $"{MenuProbe.Frames} menu frames")
                .Expect("speed restored", Scenario.Resume)
                .ExpectWithin("the new game has exactly one window", 20, () => Windows() == 1, () => $"{Windows()} windows")
                .Do("the main menu never had the window, and there is still one Bootstrap", () =>
                {
                    if (MenuProbe.Frames < 40)
                        throw new InvalidOperationException($"only {MenuProbe.Frames} main-menu frames observed");
                    if (MenuProbe.WindowsSeen != 0)
                        throw new InvalidOperationException($"{MenuProbe.WindowsSeen} window-frames on the main menu");
                    CheckOneBootstrap();
                })
                .Start();
        }

        private static void CheckOneBootstrap()
        {
            int count = UnityEngine.Object.FindObjectsOfType<Bootstrap>().Length;
            if (count != 1)
                throw new InvalidOperationException($"{count} Bootstrap objects exist, expected 1");
        }

        // Precondition: view in place, window open. Trigger: save the game.
        // Failure mode: the mod writes anything of its own into the save (a component, a scribed field, a def reference).
        // The save's header lists every active mod by packageId, as vanilla does for all mods: the check reads the header
        // as a control (it must see the id there) and looks for the mod only in the game body.
        private static void SeamNoSaveData()
        {
            var s = new Scenario("Seam no save data", 600000);
            Roster r = Prelude(s, 6);
            ViewOnSteps(s, r);
            s.Do("save", () => GameDataSaveLoader.SaveGame(Scenario.SaveName))
                .Do("the save holds nothing of the mod outside the mod list", () =>
                {
                    string text = File.ReadAllText(GenFilePaths.FilePathForSavedGame(Scenario.SaveName));
                    int end = text.IndexOf("</meta>", StringComparison.Ordinal);
                    if (end < 0)
                        throw new InvalidOperationException("no </meta> in the save: the check cannot tell header from body");
                    string header = text.Substring(0, end), body = text.Substring(end);
                    if (header.IndexOf(CC.PackageId, StringComparison.OrdinalIgnoreCase) < 0)
                        throw new InvalidOperationException("control: the save header does not list the mod, so this check cannot see mod ids");
                    int at = body.IndexOf("colonycommand", StringComparison.OrdinalIgnoreCase);
                    if (at >= 0)
                        throw new InvalidOperationException(
                            $"the save body mentions the mod at {at}: ...{body.Substring(Math.Max(0, at - 60), Math.Min(140, body.Length - Math.Max(0, at - 60)))}...");
                    r.Notes.Add($"save {text.Length / 1024} KiB, header lists the mod, body has no trace");
                })
                .Start();
        }

        // Precondition: view in place. Trigger, twice: (1) a game update removed the private member the seam reflects on,
        // simulated by resolving against a name that does not exist; (2) an exception inside the apply, simulated by
        // putting a FieldInfo of another type where the scale field is, then dirtying the bar so a rewrite runs.
        // Failure mode: the seam keeps touching the bar after its prerequisite is gone, logs an error every frame, leaves
        // the bar showing a half-written view, or fails silently without its one error.
        private static void SeamFailClosed()
        {
            var s = new Scenario("Seam fail closed", 600000);
            Roster r = Prelude(s, 6);
            s.OnFinish(() =>
            {
                ErrorCapture.Stop();
                BarAdapter.Resolve();   // leave the seam usable for whatever runs next
            });
            ViewOnSteps(s, r);

            s.Do("1: the member is gone: resolving against a missing name", () =>
                {
                    ErrorCapture.Start();
                    typeof(BarAdapter).GetMethod("Resolve", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
                        null, new[] { typeof(string) }, null).Invoke(null, new object[] { "noSuchMember" });
                })
                .ExpectWithin("1: the bar was handed back to vanilla", 20, () => VanillaBack(r), () => r.Last)
                .Do("1: switched off, one report logged", () => AssertDisabledQuietly("noSuchMember"))
                .Do("1: switching the view on again changes nothing", () =>
                {
                    BarProbe.Start();
                    ViewState.Query = ViewState.Query + " ";   // the same criteria, requested again (the version changes)
                })
                .WaitFrames(40)
                .Do("1: still vanilla, still one report", () =>
                {
                    AssertDraws("seam off after a missing member", r.VanillaText, 20, ignoreDirty: true);
                    AssertDisabledQuietly("noSuchMember");
                })
                .Do("2: the member is back", () =>
                {
                    ErrorCapture.Stop();
                    BarAdapter.Resolve();
                    if (!BarAdapter.Enabled)
                        throw new InvalidOperationException("Resolve() did not re-enable the seam");
                })
                .ExpectWithin("2: the view is back in place", 20, () => ViewInPlace(r), () => r.Last)
                .Do("2: the apply fails inside (the scale setter throws), the bar is dirtied", () =>
                {
                    ErrorCapture.Start();
                    typeof(BarAdapter).GetField("scaleField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        .SetValue(null, typeof(DrawRecord).GetField(nameof(DrawRecord.frame)));
                    Bar.MarkColonistsDirty();
                })
                .ExpectWithin("2: the seam switched itself off and the bar is vanilla", 20,
                    () => !BarAdapter.Enabled && VanillaBack(r), () => $"enabled={BarAdapter.Enabled}; {r.Last}")
                .Do("2: one report, naming the failure type", () =>
                {
                    AssertDisabledQuietly("ArgumentException");
                    BarProbe.Start();
                })
                .WaitFrames(40)
                .Do("2: still vanilla, still one report", () =>
                {
                    AssertDraws("seam off after an exception", r.VanillaText, 20, ignoreDirty: true);
                    AssertDisabledQuietly("ArgumentException");
                })
                .Start();
        }

        private static void AssertDisabledQuietly(string mustMention)
        {
            if (BarAdapter.Enabled)
                throw new InvalidOperationException("the seam is still enabled");
            if (BarAdapter.Applied)
                throw new InvalidOperationException("the seam is switched off but still reports a written view");
            if (ErrorCapture.Captured.Count != 1)
                throw new InvalidOperationException($"{ErrorCapture.Captured.Count} errors logged, expected exactly 1: {string.Join(" | ", ErrorCapture.Captured)}");
            if (!ErrorCapture.Captured[0].Contains(mustMention))
                throw new InvalidOperationException($"the error does not mention '{mustMention}': {ErrorCapture.Captured[0]}");
        }

        // Precondition: view in place. Trigger: switch to the world view and back; then a caravan of two colonists (a second
        // bar group whose entries have no map), in the map and in the world view.
        // Failure mode: an exception or a wrong draw in the world view, group renumbering or group frames broken by a
        // second group, a caravan member reachable when hidden or unreachable when shown.
        private static void SeamTopology()
        {
            var s = new Scenario("Seam topology", 600000);
            Roster r = Prelude(s, 6);
            ViewOnSteps(s, r);
            WorldViewSteps(s, r, "map group only");

            Caravan caravan = null;
            var members = new List<Pawn>();
            s.OnFinish(() =>
            {
                if (caravan != null && !caravan.Destroyed)
                    Find.WorldObjects.Remove(caravan);
                foreach (Pawn p in members.Where(p => !p.Destroyed))
                    p.Destroy();
            });
            s.Do("form a caravan of two colonists on the home tile", () =>
            {
                for (int i = 0; i < 2; i++)
                {
                    Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    p.Name = new NameTriple("Probe", $"Caravan{i}", "Fixture");
                    members.Add(p);
                }
                caravan = CaravanMaker.MakeCaravan(members, Faction.OfPlayer, Find.CurrentMap.Tile, addToWorldPawnsIfNotAlready: true);
            });
            CaptureSteps(s, r, 8);   // 6 on the map, 2 in the caravan
            ViewOnSteps(s, r);
            s.Do("two groups: the caravan group survived, groups renumbered", () =>
            {
                if (!r.Expected.Contains(",1:"))
                    throw new InvalidOperationException($"expected a second group in [{r.Expected}]");
                HitSweep(r);
            });
            WorldViewSteps(s, r, "with the caravan group");
            s.Start();
        }

        private static void WorldViewSteps(Scenario s, Roster r, string label)
        {
            s.Do($"{label}: switch to the world view", () =>
                {
                    BarProbe.Start();
                    CameraJumper.TryShowWorld();
                })
                .ExpectWithin($"{label}: the world view is up", 20, () => WorldRendererUtility.WorldSelected)
                .ExpectWithin($"{label}: the bar was drawn in the world view", 30, () => BarProbe.Repaints >= 15, () => $"{BarProbe.Repaints} repaints")
                .Do($"{label}: every draw, in both views, showed exactly the view", () =>
                {
                    AssertDraws($"world view, {label}", r.Expected, 15);
                    if (!ViewInPlace(r))
                        throw new InvalidOperationException($"view not in place: {r.Last}");
                })
                .Do($"{label}: back to the map", () => CameraJumper.TryHideWorld())
                .ExpectWithin($"{label}: the map is showing again", 20, () => !WorldRendererUtility.WorldSelected);
        }

        // Precondition: view in place. Trigger: a second player map with two colonists, the camera moves there and back.
        // Failure mode: entries of another map (map != current map), an empty-map placeholder entry, or a group per map
        // break the view; the view does not follow the camera.
        private static void SeamMapSwitch()
        {
            var s = new Scenario("Seam map switch", 600000);
            Roster r = Prelude(s, 6);
            ViewOnSteps(s, r);
            Map home = null, second = null;
            Settlement settlement = null;
            var members = new List<Pawn>();
            s.OnFinish(() =>
            {
                if (home != null && !home.Disposed && Current.Game.CurrentMap != home)
                    Current.Game.CurrentMap = home;
                foreach (Pawn p in members.Where(p => !p.Destroyed))
                    p.Destroy();
                if (settlement != null && !settlement.Destroyed)
                    Find.WorldObjects.Remove(settlement);
            });
            s.Do("settle a second home map and spawn two colonists on it", () =>
            {
                home = Find.CurrentMap;
                PlanetTile tile = TileFinder.RandomSettlementTileFor(Faction.OfPlayer, mustBeAutoChoosable: false,
                    extraValidator: t => !Find.WorldObjects.AnyWorldObjectAt(t));
                settlement = SettleUtility.AddNewHome(tile, Faction.OfPlayer);
                second = GetOrGenerateMapUtility.GetOrGenerateMap(tile, new IntVec3(60, 1, 60), null);
                for (int i = 0; i < 2; i++)
                {
                    Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    p.Name = new NameTriple("Probe", $"Second{i}", "Fixture");
                    GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(second.Center, second, 8), second);
                    members.Add(p);
                }
            });
            CaptureSteps(s, r, 8);
            ViewOnSteps(s, r);
            s.Do("two groups, one per map; the second map's colonists are kept", () =>
                {
                    if (!r.Expected.Contains(",1:"))
                        throw new InvalidOperationException($"expected a second group in [{r.Expected}]");
                    HitSweep(r);
                })
                .Do("switch the camera to the second map", () =>
                {
                    BarProbe.Start();
                    CameraJumper.TryJump(second.Center, second);
                })
                .ExpectWithin("the second map is the current map", 20, () => Find.CurrentMap == second)
                .ExpectWithin("20 repaints on the second map", 30, () => BarProbe.Repaints >= 20, () => $"{BarProbe.Repaints} repaints")
                .Do("the view held on the second map", () =>
                {
                    AssertDraws("second map", r.Expected, 20);
                    if (!ViewInPlace(r))
                        throw new InvalidOperationException($"view not in place: {r.Last}");
                })
                .Do("switch back to the home map", () =>
                {
                    BarProbe.Start();
                    CameraJumper.TryJump(home.Center, home);
                })
                .ExpectWithin("the home map is the current map", 20, () => Find.CurrentMap == home)
                .ExpectWithin("20 repaints on the home map", 30, () => BarProbe.Repaints >= 20, () => $"{BarProbe.Repaints} repaints")
                .Do("the view held after switching back", () => AssertDraws("home map again", r.Expected, 20))
                .Start();
        }
    }
}
