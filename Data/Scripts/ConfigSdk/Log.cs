using System;
using VRage.Utils;

namespace ConfigSdk
{
    // Minimal logger -> SpaceEngineers.log
    public static class Log
    {
        public static string ModName = "Config Manager";

        public static void Error(Exception e) { Write("ERROR: " + e); }
        public static void Error(string message) { Write("ERROR: " + message); }
        public static void Info(string message) { Write(message); }

        static void Write(string text)
        {
            MyLog.Default?.WriteLineAndConsole("[" + ModName + "] " + text);
        }
    }
}
