using System;
using System.Collections.Generic;
using System.Linq;
using ColonyCommand;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyCommandTests
{
    // The Inspired chip: ten fixture colonists, three of them inspired, filtered with a real click on the chip.
    public static partial class Scenarios
    {
        private const string InspiredToken = "fxinsp";   // every fixture's last name contains it: a query that isolates the ten

        /// <summary>The ten fixtures that are inspired (by index), chosen so that the first, the last and the middle are not all alike.</summary>
        private static readonly int[] InspiredIndexes = { 1, 4, 8 };

        /// <summary>The independent restatement of "inspired": the pawn's own handler holds an inspiration, and the pawn is alive.</summary>
        private static bool OracleInspired(Pawn p) => !p.Dead && p.mindState?.inspirationHandler?.CurState != null;

        // Precondition: the game paused, no criteria; ten fresh fixture colonists whose names carry the token, three of them
        // inspired through the game's own handler. Trigger: the token isolates the ten; then a real click on the Inspired chip,
        // and a second real click.
        // Failure modes: the chip filters on something else than the inspiration (an inspired colonist lost, or a colonist who
        // is not inspired shown); its count or the All count is wrong (the counts ignore the active status); the click lands
        // on another chip or does not reach the chip (wrong place in the panel); a second click does not turn the filter off;
        // the filter changes the colonists' inspiration (it must only read).
        private static void InspiredFilter()
        {
            var s = new Scenario("Inspired filter", 4000000);
            var r = new Roster();
            var cast = new List<Pawn>();
            string Shown(List<ColonistBar.Entry> entries) =>
                string.Join(",", entries.Where(e => e.pawn != null && cast.Contains(e.pawn)).Select(e => cast.IndexOf(e.pawn)));
            string Want(IEnumerable<int> indexes) =>
                string.Join(",", r.Vanilla.Where(e => e.pawn != null && cast.Contains(e.pawn) && indexes.Contains(cast.IndexOf(e.pawn))).Select(e => cast.IndexOf(e.pawn)));
            bool Settled(string expected)
            {
                if (BarProbe.IsDirty(Bar))
                {
                    r.Last = "the bar is dirty (repair pending)";
                    return false;
                }
                if (BarAdapter.Applied != !ViewState.Snapshot.IsIdentity || BarAdapter.Applied && BarAdapter.AppliedVersion != ViewState.Version)
                {
                    r.Last = $"the bar does not hold the view of the current criteria yet (applied {BarAdapter.Applied}, built for {BarAdapter.AppliedVersion}, now {ViewState.Version})";
                    return false;
                }
                string shown = Shown(BarProbe.CachedEntries(Bar));
                if (shown != expected)
                {
                    r.Last = $"the bar shows cast [{shown}], expected [{expected}]";
                    return false;
                }
                r.Last = "";
                return true;
            }
            s.OnFinish(() =>
                {
                    ViewState.Reset();
                    BarProbe.Stop();
                    Find.Selector.ClearSelection();
                })
                .Summary(() => string.Join("; ", r.Notes.Append(r.Last).Where(x => x.Length > 0)));

            s.Expect("the game is paused: nothing ticks (an inspiration would end) while the state is staged and read", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    return Find.TickManager.CurTimeSpeed == TimeSpeed.Paused;
                })
                .Do("no criteria, then ten colonists, three of them inspired", () =>
                {
                    ViewState.Reset();
                    for (int i = 0; i < 10; i++)
                        cast.Add(Fixtures.SpawnNamed(s, new NameTriple($"Insp{i}", $"Insp{i}", "Fxinsp")));
                    foreach (int i in InspiredIndexes)
                        r.Notes.Add($"colonist {i} inspired: {Fixtures.Inspire(cast[i]).defName}");
                    Messages.Clear();   // a message is a window over the bar and swallows real input beneath it
                    Find.Selector.ClearSelection();
                })
                .ExpectWithin("the vanilla roster holds all ten", 20, () => CaptureVanilla(r, 10) && cast.All(p => r.Vanilla.Any(e => e.pawn == p)), () => r.Last)
                .Do("exactly three of the ten are inspired, read from the pawns' own handlers", () =>
                {
                    var inspired = cast.Where(OracleInspired).Select(p => cast.IndexOf(p)).ToList();
                    if (!inspired.SequenceEqual(InspiredIndexes))
                        throw new InvalidOperationException($"inspired fixtures are [{string.Join(",", inspired)}], expected [{string.Join(",", InspiredIndexes)}]");
                    if (cast.Any(p => p.Drafted))
                        throw new InvalidOperationException("a fixture is drafted: the staging is not what the scenario assumes");
                })
                .Do("the token isolates the ten", () =>
                {
                    BarProbe.Start();
                    ViewState.Query = InspiredToken;
                })
                .ExpectWithin("the bar shows all ten, and the chips count 10 and 3", 20,
                    () => Settled(Want(Enumerable.Range(0, 10))) && BarAdapter.Counts.All == 10 && BarAdapter.Counts.Inspired == 3
                        && TheWindow().ChipLabel(0).EndsWith(" 10") && TheWindow().ChipLabel(4).EndsWith(" 3"),
                    () => $"counts all {BarAdapter.Counts.All}, inspired {BarAdapter.Counts.Inspired}, labels '{TheWindow().ChipLabel(0)}' / '{TheWindow().ChipLabel(4)}'; {r.Last}");

            ClickUntilStateChanges(s, r, "click the Inspired chip", () => Center(TheWindow().ChipRect(4)));
            s.ExpectWithin("the filter is Inspired and the bar shows exactly the three inspired colonists", 20,
                    () => ViewState.Status == StatusFilter.Inspired && Settled(Want(InspiredIndexes)),
                    () => $"status {ViewState.Status}; {r.Last}; chip {TheWindow().ChipRect(4)}, mouse {UI.MousePositionOnUIInverted}; {InputDiagnostics()}")
                .Do("nobody else of the roster is shown (the map's own colonists carry no token), and the counts ignore the status", () =>
                {
                    AssertShownEquals(r, "Inspired", p => Oracle.Name(p, InspiredToken) && OracleInspired(p));
                    if (BarAdapter.Counts.All != 10 || BarAdapter.Counts.Inspired != 3)
                        throw new InvalidOperationException($"counts changed with the status: all {BarAdapter.Counts.All}, inspired {BarAdapter.Counts.Inspired}");
                    r.Notes.Add($"Inspired chip: {Shown(BarProbe.CachedEntries(Bar))} of 10 shown");
                });

            ClickUntilStateChanges(s, r, "click the Inspired chip again", () => Center(TheWindow().ChipRect(4)));
            s.ExpectWithin("a second click turned the filter off: all ten are back", 20,
                    () => ViewState.Status == StatusFilter.None && Settled(Want(Enumerable.Range(0, 10))),
                    () => $"status {ViewState.Status}; {r.Last}")
                .Do("the filter only read: the same three are still inspired", () =>
                {
                    var inspired = cast.Where(OracleInspired).Select(p => cast.IndexOf(p)).ToList();
                    if (!inspired.SequenceEqual(InspiredIndexes))
                        throw new InvalidOperationException($"inspired fixtures are now [{string.Join(",", inspired)}]");
                })
                .Start();
        }
    }
}
