using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace ColonyCommandTests
{
    [StaticConstructorOnStartup]
    public static class TestStartup
    {
        static TestStartup()
        {
            // The harness's baseline run loads this mod WITHOUT the main mod. Then nothing here may touch the main
            // assembly (Mono resolves it when a method that uses it is first compiled), so stay idle and quiet.
            if (!ModsConfig.IsActive(CCT.MainPackageId))
            {
                Verse.Log.Message($"{CCT.Tag} main mod not active: test driver idle");
                return;
            }
            Boot();
        }

        // Kept out of the static constructor on purpose: only this method references the main assembly.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Boot()
        {
            new Harmony("josh9817.colonycommandtests").PatchAll();
            AutoTest.Init();
        }
    }
}
