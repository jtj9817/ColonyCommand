using RimWorld;
using Verse;

namespace ColonyCommand
{
    /// <summary>The one status chip that can be active (spec §3.2: mutually exclusive, <c>None</c> = no status filter).</summary>
    public enum StatusFilter
    {
        None,
        Drafted,
        Injured,
        Idle,
    }

    /// <summary>Spec §4.2. <c>Vanilla</c> is the default and leaves the bar's own order alone.</summary>
    public enum SortKey
    {
        Vanilla,
        Name,
        Mood,
        Skill,
    }

    /// <summary>
    /// Everything the roster pipeline needs to know about what the user asked for, as plain values: a function argument
    /// for <see cref="RosterView"/>, so the pipeline never reads global state.
    /// </summary>
    public struct Criteria
    {
        /// <summary>Trimmed; empty = no name criterion.</summary>
        public string Query;
        public StatusFilter Status;
        public SkillDef Skill;
        /// <summary>Minimum skill level, or <see cref="ViewState.AnyLevel"/> (the skill criterion is off).</summary>
        public int MinLevel;
        public SortKey Sort;
        /// <summary>Name: Z-A. Mood and skill: high to low (see <see cref="RosterView"/>).</summary>
        public bool Descending;

        public bool HasQuery => !string.IsNullOrEmpty(Query);

        /// <summary>A skill is chosen and a minimum level is set (a chosen skill alone only feeds the skill sort).</summary>
        public bool HasSkillCriterion => Skill != null && MinLevel != ViewState.AnyLevel;

        /// <summary>Trimmed query non-empty, or a status, or a skill criterion. Not the search widget's own "Active" flag.</summary>
        public bool FilterActive => HasQuery || Status != StatusFilter.None || HasSkillCriterion;

        /// <summary>Skill sorting needs a chosen skill; without one it is the vanilla order.</summary>
        public SortKey EffectiveSort => Sort == SortKey.Skill && Skill == null ? SortKey.Vanilla : Sort;

        /// <summary>Nothing to filter and the vanilla order: the bar must stay completely untouched.</summary>
        public bool IsIdentity => !FilterActive && EffectiveSort == SortKey.Vanilla;
    }

    /// <summary>
    /// The user's current search, filter and sort: static, memory only (spec §5: never serialized, cleared on reload).
    /// Every change bumps <see cref="Version"/>, which is what tells <see cref="BarAdapter"/> to rebuild the view.
    /// A static outlives a load, so <see cref="SyncGame"/> (called every frame by the adapter) resets it when
    /// <see cref="Current.Game"/> is a different game from the last one seen.
    /// </summary>
    public static class ViewState
    {
        public const int AnyLevel = -1;

        private static string query = "", trimmed = "";
        private static StatusFilter status;
        private static SkillDef skill;
        private static int minLevel = AnyLevel;
        private static SortKey sort;
        private static bool descending;
        private static Game lastGame;

        /// <summary>Raised on every change; a changed value forces a rebuild even if the bar still looks like ours.</summary>
        public static int Version { get; private set; }

        /// <summary>What the search field holds (untrimmed, as typed).</summary>
        public static string Query
        {
            get => query;
            set
            {
                value = value ?? "";
                if (value == query)
                    return;
                query = value;
                trimmed = value.Trim();
                Version++;
            }
        }

        public static StatusFilter Status
        {
            get => status;
            set
            {
                if (value == status)
                    return;
                status = value;
                Version++;
            }
        }

        public static SkillDef Skill
        {
            get => skill;
            set
            {
                if (value == skill)
                    return;
                skill = value;
                if (skill == null && sort == SortKey.Skill)
                    sort = SortKey.Vanilla;   // the skill sort is only available while a skill is chosen
                Version++;
            }
        }

        /// <summary>Minimum level for the chosen skill, or <see cref="AnyLevel"/>.</summary>
        public static int MinLevel
        {
            get => minLevel;
            set
            {
                if (value == minLevel)
                    return;
                minLevel = value;
                Version++;
            }
        }

        public static SortKey Sort
        {
            get => sort;
            set
            {
                if (value == sort)
                    return;
                sort = value;
                Version++;
            }
        }

        public static bool Descending
        {
            get => descending;
            set
            {
                if (value == descending)
                    return;
                descending = value;
                Version++;
            }
        }

        /// <summary>The current values as a plain argument for <see cref="RosterView"/>.</summary>
        public static Criteria Snapshot => new Criteria
        {
            Query = trimmed,
            Status = status,
            Skill = skill,
            MinLevel = minLevel,
            Sort = sort,
            Descending = descending,
        };

        /// <summary>True when anything differs from the defaults (the Reset control is useful).</summary>
        public static bool AnythingSet => query.Length > 0 || status != StatusFilter.None || skill != null
            || minLevel != AnyLevel || sort != SortKey.Vanilla || descending;

        /// <summary>Back to the defaults: no query, no status, no skill, vanilla order. Bumps the version if anything changed.</summary>
        public static void Reset()
        {
            if (!AnythingSet)
                return;
            query = trimmed = "";
            status = StatusFilter.None;
            skill = null;
            minLevel = AnyLevel;
            sort = SortKey.Vanilla;
            descending = false;
            Version++;
        }

        /// <summary>
        /// Resets everything when the game is a different one from the last call (new game, a load from the menu or in
        /// game, quit to the main menu where it is null), and bumps the version so the bar rebuilds from a fresh vanilla
        /// cache. Compared by reference. A map change does not reset.
        /// </summary>
        public static void SyncGame()
        {
            Game game = Current.Game;
            if (ReferenceEquals(game, lastGame))
                return;
            lastGame = game;
            Reset();
            Version++;
        }
    }
}
