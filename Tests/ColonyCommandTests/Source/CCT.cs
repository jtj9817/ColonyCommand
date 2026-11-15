using Verse;

namespace ColonyCommandTests
{
    /// <summary>
    /// Test-driver logging. The tag differs from the main mod's "[ColonyCommand]" on purpose: tools/autotest.py's
    /// baseline run loads this mod WITHOUT the main mod and refuses a log that contains "[ColonyCommand]".
    /// </summary>
    public static class CCT
    {
        public const string MainPackageId = "josh9817.colonycommand";
        public const string Tag = "[ColonyCommandTests]";

        public static void Info(string message) => Verse.Log.Message($"{Tag} {message}");

        /// <summary>A red error: tools/autotest.py counts it as a new error (the tag is not the baseline's).</summary>
        public static void Error(string message) => Verse.Log.Error($"{Tag} ERROR: {message}");
    }
}
