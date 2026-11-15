using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ColonyCommand;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace ColonyCommandTests
{
    /// <summary>
    /// Phase 5 observers. <see cref="Build"/> and <see cref="Count"/> count how often the adapter re-evaluates the live
    /// game (a rebuild of the view, or only the chip counts when nothing is filtered): that is what "throttled to about
    /// 0.25 s" has to be judged on, not on the adapter's own constant.
    /// </summary>
    public static class RefreshProbe
    {
        public static int Build, Count;

        public static void Start() => Build = Count = 0;
    }

    [HarmonyPatch(typeof(RosterView), nameof(RosterView.Build))]
    public static class Patch_RosterView_Build
    {
        public static void Prefix() => RefreshProbe.Build++;
    }

    [HarmonyPatch(typeof(RosterView), nameof(RosterView.Count))]
    public static class Patch_RosterView_Count
    {
        public static void Prefix() => RefreshProbe.Count++;
    }

    /// <summary>
    /// What the mod's own statics keep alive. A static that points at a pawn, a map, a world object or a game other than
    /// the current one is a stale reference: it shows colonists that are gone, and it keeps a whole finished game in
    /// memory. Looks at every static field of the mod's assembly (collections and structs included, a few levels deep),
    /// so it also sees a pawn left behind in a scratch list. Delegates, defs and Unity objects are ignored.
    /// </summary>
    public static class StaleReferences
    {
        private const int MaxDepth = 4, MaxItems = 5000;

        /// <summary>One line per static that holds something it must not. Empty when the mod holds nothing of the game.</summary>
        public static List<string> Scan()
        {
            var problems = new List<string>();
            foreach (Type type in typeof(ViewState).Assembly.GetTypes())
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsLiteral)
                        continue;
                    object value;
                    try
                    {
                        value = field.GetValue(null);
                    }
                    catch (Exception)
                    {
                        continue;   // a static whose initializer cannot run here holds nothing of the game
                    }
                    Inspect($"{type.Name}.{field.Name}", value, problems, 0);
                }
            }
            return problems;
        }

        /// <summary>The number of static fields looked at (a control: the scan really saw the mod).</summary>
        public static int FieldsSeen()
        {
            int n = 0;
            foreach (Type type in typeof(ViewState).Assembly.GetTypes())
                n += type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Length;
            return n;
        }

        private static void Inspect(string path, object value, List<string> problems, int depth)
        {
            if (value == null || depth > MaxDepth)
                return;
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is Delegate || value is Type
                || value is UnityEngine.Object || value is Def)
                return;
            if (value is Game game)
            {
                if (!ReferenceEquals(game, Current.Game))
                    problems.Add($"{path} holds a game that is not the current one (current: {(Current.Game == null ? "none" : "another")})");
                return;
            }
            if (value is Thing || value is Map || value is WorldObject || value is World)
            {
                problems.Add($"{path} holds {type.Name} {value}");
                return;
            }
            if (value is IEnumerable items)
            {
                int n = 0;
                foreach (object item in items)
                {
                    if (n++ >= MaxItems)
                        break;
                    Inspect($"{path}[{n - 1}]", item, problems, depth + 1);
                }
                return;
            }
            if (type.IsValueType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    Inspect($"{path}.{field.Name}", field.GetValue(value), problems, depth + 1);
            }
        }
    }
}
