using UnityEngine;
using Verse;

namespace ColonyCommand
{
    /// <summary>Runs on the main thread once all defs and textures are loaded (StaticConstructorOnStartup).</summary>
    [StaticConstructorOnStartup]
    public static class Startup
    {
        static Startup()
        {
            // Always printed: tools/autotest.py and tools/status.sh compare it with .devlog/last-build.
            Verse.Log.Message($"{CC.Tag} loaded build {CC.BuildId}");
            BarAdapter.Resolve();
            // Unity objects can only be created on the main thread, which this is (Mod constructors run on a loader thread).
            var go = new GameObject("ColonyCommand.Bootstrap");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Bootstrap>();
        }
    }
}
