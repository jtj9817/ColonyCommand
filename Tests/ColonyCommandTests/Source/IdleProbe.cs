using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyCommandTests
{
    /// <summary>
    /// The oracle for <c>Predicates.Idle</c> (user decision Q4: "exactly the bar's idle-icon condition"): records, for
    /// every colonist the vanilla bar draws, whether it drew the idle icon for them. Test-only Harmony observer of
    /// <c>ColonistBarColonistDrawer.DrawIcons</c> (the current pawn) and its private <c>DrawIcon</c> (which texture).
    /// Only pawns the bar actually draws get a record, so the roster must be in the vanilla (identity) view.
    /// </summary>
    public static class IdleProbe
    {
        private static readonly Texture2D IdleTexture =
            (Texture2D)AccessTools.Field(typeof(ColonistBarColonistDrawer), "Icon_Idle").GetValue(null);

        public static bool Recording;

        /// <summary>Pawn → the latest repaint drew the idle icon for it.</summary>
        public static readonly Dictionary<Pawn, bool> Drawn = new Dictionary<Pawn, bool>();

        /// <summary>Repaints in which the bar drew icons for at least one pawn (the oracle sees the bar).</summary>
        public static int IconPasses;

        internal static Pawn Current;

        public static void Start()
        {
            Drawn.Clear();
            IconPasses = 0;
            Current = null;
            Recording = true;
        }

        public static void Stop()
        {
            Recording = false;
            Current = null;
        }

        internal static bool IsIdleIcon(Texture2D icon) => icon != null && icon == IdleTexture;
    }

    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "DrawIcons")]
    public static class Patch_DrawIcons
    {
        public static void Prefix(Pawn colonist)
        {
            if (!IdleProbe.Recording)
                return;
            IdleProbe.Current = colonist;
            IdleProbe.Drawn[colonist] = false;   // the bar handled this pawn; true only if the idle icon follows
            IdleProbe.IconPasses++;
        }

        public static void Postfix() => IdleProbe.Current = null;
    }

    [HarmonyPatch(typeof(ColonistBarColonistDrawer), "DrawIcon")]
    public static class Patch_DrawIcon
    {
        public static void Prefix(Texture2D icon)
        {
            if (IdleProbe.Recording && IdleProbe.Current != null && IdleProbe.IsIdleIcon(icon))
                IdleProbe.Drawn[IdleProbe.Current] = true;
        }
    }
}
