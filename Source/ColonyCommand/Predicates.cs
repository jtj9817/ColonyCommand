using System;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace ColonyCommand
{
    /// <summary>
    /// What the filters and sorts ask of one pawn. Read-only: nothing here changes a pawn. A dead colonist (the bar keeps
    /// a corpse entry) is never Drafted, Injured or Idle, has no skill and no mood, and only matches a name search
    /// (TODO.md, Phase 2 open point 1).
    /// </summary>
    public static class Predicates
    {
        /// <summary>The name the bar shows, untruncated (the bar cuts it to the portrait width; matching and sorting must not).</summary>
        public static string DisplayName(Pawn pawn) => pawn.LabelShortCap;

        /// <summary>
        /// FR-01: case-insensitive substring match of an already trimmed query against the displayed name, the
        /// constituent name parts (first, nick, last, or a single name) and the full name. An empty query matches all.
        /// </summary>
        public static bool NameMatches(Pawn pawn, string trimmedQuery)
        {
            if (string.IsNullOrEmpty(trimmedQuery))
                return true;
            if (Contains(pawn.LabelShort, trimmedQuery))
                return true;
            Name name = pawn.Name;
            if (name == null)
                return false;
            if (name is NameTriple triple)
                return Contains(triple.First, trimmedQuery) || Contains(triple.Nick, trimmedQuery) || Contains(triple.Last, trimmedQuery)
                    || Contains(triple.ToStringFull, trimmedQuery);
            if (name is NameSingle single)
                return Contains(single.Name, trimmedQuery) || Contains(single.ToStringFull, trimmedQuery);
            return Contains(name.ToStringShort, trimmedQuery) || Contains(name.ToStringFull, trimmedQuery);
        }

        private static bool Contains(string text, string part) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Spec §3.2: currently drafted in the game state.</summary>
        public static bool Drafted(Pawn pawn) => !pawn.Dead && pawn.Drafted;

        /// <summary>Q3 (user decision): any injury that can heal naturally (<c>HediffSet.HasNaturallyHealingInjury</c>), so not a scar and not a non-injury hediff.</summary>
        public static bool Injured(Pawn pawn) => !pawn.Dead && pawn.health?.hediffSet != null && pawn.health.hediffSet.HasNaturallyHealingInjury();

        /// <summary>
        /// Q4 (user decision): exactly the bar's idle icon, the last branch of the chain in
        /// <c>ColonistBarColonistDrawer.DrawIcons</c>. The icon shows only when none of the higher-priority states apply:
        /// dead, mental state, medical bed, asleep, fleeing, attacking. Then <c>mindState.IsIdle</c> and a game day has
        /// passed. There is no "not drafted" term here because the bar has none: a drafted colonist is excluded through
        /// <c>lastJobTag</c> (<c>DraftedOrder</c>).
        /// </summary>
        public static bool Idle(Pawn pawn)
        {
            if (pawn.Dead)
                return false;
            Job job = pawn.CurJob;
            bool attacking = false;
            if (job != null)
            {
                JobDef def = job.def;
                if (def == JobDefOf.AttackMelee || def == JobDefOf.AttackStatic)
                    attacking = true;
                else if (def == JobDefOf.Wait_Combat && pawn.stances?.curStance is Stance_Busy busy && busy.focusTarg.IsValid)
                    attacking = true;
            }
            if (pawn.InAggroMentalState || pawn.InMentalState)
                return false;
            Building_Bed bed = pawn.CurrentBed();
            if (bed != null && bed.Medical)
                return false;
            if (job != null && pawn.jobs?.curDriver != null && pawn.jobs.curDriver.asleep)
                return false;
            if (pawn.GetCaravan() != null && pawn.needs?.rest?.Resting == true)
                return false;
            if (job != null && job.def == JobDefOf.FleeAndCower)
                return false;
            if (attacking)
                return false;
            return pawn.mindState != null && pawn.mindState.IsIdle && GenDate.DaysPassed >= 1;
        }

        /// <summary>The pawn's current level in <paramref name="skill"/>, or -1 when unavailable (no skills, dead, or the skill is totally disabled for them).</summary>
        public static int SkillLevel(Pawn pawn, SkillDef skill)
        {
            if (skill == null || pawn.Dead || pawn.skills == null)
                return -1;
            // Not GetSkill: it logs an error when the pawn has no record for the def (a skill added by another mod).
            foreach (SkillRecord record in pawn.skills.skills)
            {
                if (record.def == skill)
                    return record.TotallyDisabled ? -1 : record.Level;
            }
            return -1;
        }

        /// <summary>Current mood as the bar reads it (0..1), or NaN when unavailable (dead, or a pawn without a mood need).</summary>
        public static float Mood(Pawn pawn)
        {
            if (pawn.Dead)
                return float.NaN;
            Need_Mood mood = pawn.needs?.mood;
            return mood == null ? float.NaN : mood.CurLevelPercentage;
        }
    }
}
