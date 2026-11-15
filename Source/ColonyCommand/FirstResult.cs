using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonyCommand
{
    /// <summary>
    /// What Enter does in the search field: the same as a plain click on the first result's portrait, by vanilla's own rules
    /// (<c>Selector.SelectUnderMouse</c> in a map, <c>WorldSelector.SelectUnderMouse</c> in the world view), with no new path.
    /// The selection is touched only here, only by that Enter; a filter never changes it.
    /// </summary>
    public static class FirstResult
    {
        /// <summary>The first pawn entry in the bar's order (home map, other maps, caravans; the sort order inside a group), or null.</summary>
        public static Pawn Target()
        {
            List<ColonistBar.Entry> entries = Find.ColonistBar.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].pawn != null)
                    return entries[i].pawn;
            }
            return null;
        }

        /// <summary>The thing a click on this portrait resolves to (<c>ColonistBar.ColonistOrCorpseAt</c>): the corpse of a dead colonist, else the pawn.</summary>
        public static Thing ThingFor(Pawn pawn) =>
            pawn.Dead && pawn.Corpse != null && pawn.Corpse.SpawnedOrAnyParentSpawned ? (Thing)pawn.Corpse : pawn;

        /// <summary>Selects the first result as a click would. False when there is nothing to select (no result, or the target is not on any map).</summary>
        public static bool Select()
        {
            Pawn pawn = Target();
            if (pawn == null)
                return false;
            Thing thing = ThingFor(pawn);
            bool caravanMember = pawn.IsCaravanMember();
            if (WorldRendererUtility.WorldSelected)
            {
                if (caravanMember)
                {
                    Find.WorldSelector.ClearSelection();
                    Find.WorldSelector.Select(pawn.GetCaravan());
                }
                else if (thing.Spawned)
                    CameraJumper.TryJumpAndSelect(thing);
                else
                    CameraJumper.TryJump(thing);
                return true;
            }
            if (caravanMember)
            {
                CameraJumper.TryJumpAndSelect(pawn.GetCaravan());
                return true;
            }
            // Something that is on no map (a world pawn, a destroyed thing) selects nothing, as vanilla's Select would only log an error for it; vanilla would fall through to whatever is under the cursor.
            if (thing.Destroyed || (thing is Pawn selectable && selectable.IsWorldPawn()) || thing.SpawnedParentOrMe == null)
                return false;
            Find.Selector.ClearSelection();
            Find.Selector.Select(thing);   // switches the map and jumps the camera itself when the pawn is on another map
            return true;
        }
    }
}
