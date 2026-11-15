using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace ColonyCommand
{
    /// <summary>
    /// Chip counts (spec §3.2): eligible, name-matching pawns satisfying each chip, counted before the active status and
    /// skill criteria are applied. Placeholder entries (a map without colonists) are not pawns and are never counted.
    /// </summary>
    public struct RosterCounts
    {
        /// <summary>Pawn entries in the vanilla bar (the eligible population), name match or not.</summary>
        public int Eligible;
        /// <summary>Eligible pawns matching the name query: the count behind the "All" chip.</summary>
        public int All;
        public int Drafted, Injured, Idle;
    }

    /// <summary>
    /// The pipeline of spec §4.3: vanilla entries → name → status → skill → stable sort, per group. A function of its
    /// arguments: no <c>GUI</c>, no <c>Widgets</c>, no <c>Find</c>, nothing is written to the game (it reads pawns
    /// through <see cref="Predicates"/> only). The caller owns the lists; only module-private scratch is kept here.
    /// </summary>
    public static class RosterView
    {
        private struct Row
        {
            public ColonistBar.Entry Entry;
            /// <summary>Position in the vanilla entry list: the tie-break, and unique, so the order is total and deterministic.</summary>
            public int Index;
            public string Name;
            public float Mood;
            public int Skill;
        }

        private static readonly List<Row> rows = new List<Row>();
        private static readonly Comparison<Row> compareRows = CompareRows;
        private static SortKey sortKey;
        private static bool sortDescending;

        /// <summary>
        /// Fills <paramref name="output"/> (cleared first) with the entries that pass, sorted inside each group, and returns
        /// the number of groups. Groups that lose every entry vanish and the rest are renumbered from 0 (the bar's layout
        /// indexes per-group arrays by group id). A placeholder (<c>pawn == null</c>) passes only while no filter is
        /// active, so an identity or sort-only view holds exactly vanilla's entries. Entries keep vanilla's order within
        /// a group unless a sort is set; ties always go back to the vanilla position, whatever the direction.
        /// </summary>
        public static int Build(List<ColonistBar.Entry> vanilla, Criteria criteria, List<ColonistBar.Entry> output, out RosterCounts counts)
        {
            counts = default;
            output.Clear();
            SortKey sort = criteria.EffectiveSort;
            bool filtering = criteria.FilterActive;
            bool needName = sort == SortKey.Name;
            bool needMood = sort == SortKey.Mood;
            bool needSkill = criteria.Skill != null && (criteria.HasSkillCriterion || sort == SortKey.Skill);
            int groups = 0;
            int i = 0;
            while (i < vanilla.Count)
            {
                int group = vanilla[i].group;
                rows.Clear();
                for (; i < vanilla.Count && vanilla[i].group == group; i++)
                {
                    ColonistBar.Entry entry = vanilla[i];
                    Pawn pawn = entry.pawn;
                    if (pawn == null)
                    {
                        if (!filtering)
                            rows.Add(new Row { Entry = entry, Index = i });
                        continue;
                    }
                    counts.Eligible++;
                    if (!Predicates.NameMatches(pawn, criteria.Query))
                        continue;
                    counts.All++;
                    bool drafted = Predicates.Drafted(pawn), injured = Predicates.Injured(pawn), idle = Predicates.Idle(pawn);
                    if (drafted)
                        counts.Drafted++;
                    if (injured)
                        counts.Injured++;
                    if (idle)
                        counts.Idle++;
                    if (!StatusPasses(criteria.Status, drafted, injured, idle))
                        continue;
                    int skill = needSkill ? Predicates.SkillLevel(pawn, criteria.Skill) : 0;
                    if (criteria.HasSkillCriterion && (skill < 0 || skill < criteria.MinLevel))
                        continue;
                    rows.Add(new Row
                    {
                        Entry = entry,
                        Index = i,
                        Name = needName ? Predicates.DisplayName(pawn) : null,
                        Mood = needMood ? Predicates.Mood(pawn) : float.NaN,
                        Skill = skill,
                    });
                }
                if (rows.Count == 0)
                    continue;
                if (sort != SortKey.Vanilla && rows.Count > 1)
                {
                    sortKey = sort;
                    sortDescending = criteria.Descending;
                    rows.Sort(compareRows);   // unstable on its own: CompareRows ends on the unique vanilla index
                }
                for (int r = 0; r < rows.Count; r++)
                {
                    ColonistBar.Entry entry = rows[r].Entry;
                    entry.group = groups;
                    output.Add(entry);
                }
                groups++;
            }
            rows.Clear();   // keep no pawn references between calls
            return groups;
        }

        private static bool StatusPasses(StatusFilter status, bool drafted, bool injured, bool idle)
        {
            switch (status)
            {
                case StatusFilter.Drafted: return drafted;
                case StatusFilter.Injured: return injured;
                case StatusFilter.Idle: return idle;
                default: return true;
            }
        }

        private static int CompareRows(Row a, Row b)
        {
            int result = 0;
            switch (sortKey)
            {
                case SortKey.Name:
                    result = string.Compare(a.Name, b.Name, StringComparison.InvariantCultureIgnoreCase);
                    if (sortDescending)
                        result = -result;
                    break;
                case SortKey.Mood:
                    result = CompareMissingLast(float.IsNaN(a.Mood), float.IsNaN(b.Mood), a.Mood.CompareTo(b.Mood));
                    break;
                case SortKey.Skill:
                    result = CompareMissingLast(a.Skill < 0, b.Skill < 0, a.Skill.CompareTo(b.Skill));
                    break;
            }
            return result != 0 ? result : a.Index.CompareTo(b.Index);
        }

        /// <summary>Values compare low to high (high to low when descending); a missing value is last in both directions.</summary>
        private static int CompareMissingLast(bool aMissing, bool bMissing, int byValue)
        {
            if (aMissing || bMissing)
                return aMissing == bMissing ? 0 : aMissing ? 1 : -1;
            return sortDescending ? -byValue : byValue;
        }
    }
}
