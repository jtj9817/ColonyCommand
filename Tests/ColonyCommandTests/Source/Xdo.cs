using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ColonyCommandTests
{
    /// <summary>
    /// Real mouse input through xdotool, for the one thing the API can't prove: that the vanilla input path (hit test,
    /// click, drag) behaves with the view in place. Guarded so it can only ever reach the harness's private Xvfb
    /// display (tools/autotest.py picks :90 to :139), never a real desktop or the user's game.
    /// </summary>
    internal static class Xdo
    {
        private static readonly Regex HarnessDisplay = new Regex(@"^:(9\d|1[0-3]\d)$");

        public static string Display => Environment.GetEnvironmentVariable("DISPLAY") ?? "";

        /// <summary>Starts xdotool with these arguments and returns at once; the caller polls HasExited.</summary>
        public static Process Start(string arguments)
        {
            if (!AutoTest.Enabled || !HarnessDisplay.IsMatch(Display))
                throw new InvalidOperationException(
                    $"refusing to send input: DISPLAY '{Display}' is not the harness's virtual display (autotest run: {AutoTest.Enabled})");
            return Process.Start(new ProcessStartInfo("xdotool", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
        }

        /// <summary>Throws with xdotool's own message when it failed.</summary>
        public static void Check(Process process)
        {
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"xdotool exited {process.ExitCode}: {process.StandardError.ReadToEnd().Trim()}");
        }
    }
}
