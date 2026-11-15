using System.Reflection;
using Verse;

namespace ColonyCommand
{
    /// <summary>Identity and the one logging helper (every line carries the [ColonyCommand] tag).</summary>
    public static class CC
    {
        public const string PackageId = "josh9817.colonycommand";
        public const string Tag = "[ColonyCommand]";

        /// <summary>"1.0.0+&lt;build id&gt;": tools/build.sh stamps the id, tools/autotest.py compares it with .devlog/last-build.</summary>
        public static readonly string BuildId =
            typeof(CC).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        /// <summary>Dev-mode message; players never see it.</summary>
        public static void Info(string message)
        {
            if (Prefs.DevMode)
                Verse.Log.Message($"{Tag} {message}");
        }

        /// <summary>A red error. tools/autotest.py counts every "[ColonyCommand] ERROR" line as a new error.</summary>
        public static void Error(string message) => Verse.Log.Error($"{Tag} ERROR: {message}");

        /// <summary>For anything that could repeat per frame: logged once per <paramref name="key"/>.</summary>
        public static void ErrorOnce(string message, string key) =>
            Verse.Log.ErrorOnce($"{Tag} ERROR: {message}", key.GetHashCode() ^ 0x43430001);
    }
}
