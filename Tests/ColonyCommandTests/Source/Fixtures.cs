using RimWorld;
using Verse;

namespace ColonyCommandTests
{
    /// <summary>Colonists-only fixtures (AGENTS.md: no rooms, power, items or animals).</summary>
    internal static class Fixtures
    {
        /// <summary>
        /// Makes sure the current map has at least <paramref name="total"/> free colonists by spawning named, generated
        /// ones near the center. The scenario destroys what it spawned when it ends.
        /// </summary>
        public static void EnsureColonists(Scenario scenario, int total)
        {
            Map map = Find.CurrentMap;
            for (int i = map.mapPawns.FreeColonists.Count; i < total; i++)
            {
                Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                pawn.Name = new NameTriple("Probe", $"Probe{i}", "Fixture");
                GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 10), map);
                scenario.Track(pawn);
            }
        }
    }
}
