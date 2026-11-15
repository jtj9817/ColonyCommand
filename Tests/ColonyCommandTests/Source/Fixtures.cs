using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

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

        /// <summary>A generated free colonist with exactly this name, spawned near the center; destroyed when the scenario ends.</summary>
        public static Pawn SpawnNamed(Scenario scenario, Name name)
        {
            Map map = Find.CurrentMap;
            Pawn pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            pawn.Name = name;
            GenSpawn.Spawn(pawn, CellFinder.RandomClosewalkCellNear(map.Center, map, 10), map);
            scenario.Track(pawn);
            return pawn;
        }

        /// <summary>A current injury: a cut on the torso (a Hediff_Injury that heals naturally) unless <paramref name="permanent"/> (a scar).</summary>
        public static void Injure(Pawn pawn, bool permanent = false)
        {
            Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, pawn.RaceProps.body.corePart);
            wound.Severity = 1f;
            pawn.health.AddHediff(wound);
            if (permanent)
            {
                HediffComp_GetsPermanent comp = wound.TryGetComp<HediffComp_GetsPermanent>();
                if (comp == null)
                    throw new InvalidOperationException("a cut has no permanence component here: cannot stage a scar");
                comp.IsPermanent = true;
            }
        }

        public static void Draft(Pawn pawn) => pawn.drafter.Drafted = true;

        public static void SetSkill(Pawn pawn, SkillDef skill, int level) => pawn.skills.GetSkill(skill).Level = level;

        /// <summary>Sets the mood need directly (0..1). Nothing ticks while the scenario holds the game paused, so it stays.</summary>
        public static void SetMood(Pawn pawn, float percentage) => pawn.needs.mood.CurLevelPercentage = percentage;

        /// <summary>
        /// What the think tree would leave behind: the tag the bar's idle rule reads (<c>Pawn_MindState.IsIdle</c>).
        /// Staging it directly keeps a paused game deterministic; one scenario step proves the real draft path sets it.
        /// </summary>
        public static void SetJobTag(Pawn pawn, JobTag tag) => pawn.mindState.lastJobTag = tag;

        /// <summary>A skill every one of these pawns can use (not totally disabled), or null when there is none.</summary>
        public static SkillDef CommonSkill(IEnumerable<Pawn> pawns)
        {
            List<Pawn> list = pawns.ToList();
            return DefDatabase<SkillDef>.AllDefsListForReading.FirstOrDefault(def =>
                list.All(p => p.skills != null && !p.skills.GetSkill(def).TotallyDisabled));
        }
    }
}
