using System;
using System.IO;
using System.Text;
using RimWorld;
using Verse;

namespace LearnToSurvive
{
    public enum LogLevel
    {
        Off = 0,
        Summary = 1,
        Decisions = 2,
        Verbose = 3
    }

    public static class LTSLog
    {
        private static StreamWriter logWriter;
        private static readonly object writeLock = new object();
        private static long bytesWritten;
        private const long MaxFileSize = 10 * 1024 * 1024; // 10MB
        private const int MaxRotatedFiles = 3;

        public static void Initialize()
        {
            if (LTSSettings.logLevel == LogLevel.Off) return;
            try
            {
                string logDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "LearnToSurvive");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "ColonistAI_Decisions.log");
                RotateIfNeeded(logPath);
                logWriter = new StreamWriter(logPath, true, Encoding.UTF8);
                logWriter.AutoFlush = true;
                bytesWritten = new FileInfo(logPath).Length;
                Log.Message("[LearnToSurvive] Decision log initialized at: " + logPath);
            }
            catch (Exception ex)
            {
                Log.Warning("[LearnToSurvive] Failed to initialize decision log: " + ex.Message);
            }
        }

        public static void Shutdown()
        {
            lock (writeLock)
            {
                if (logWriter != null)
                {
                    logWriter.Flush();
                    logWriter.Close();
                    logWriter = null;
                }
            }
        }

        private static void RotateIfNeeded(string path)
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length < MaxFileSize) return;

            for (int i = MaxRotatedFiles - 1; i >= 1; i--)
            {
                string src = path + "." + i;
                string dst = path + "." + (i + 1);
                if (File.Exists(dst)) File.Delete(dst);
                if (File.Exists(src)) File.Move(src, dst);
            }
            string first = path + ".1";
            if (File.Exists(first)) File.Delete(first);
            File.Move(path, first);
        }

        private static void WriteLine(string line)
        {
            if (logWriter == null) return;
            lock (writeLock)
            {
                try
                {
                    logWriter.WriteLine(line);
                    bytesWritten += Encoding.UTF8.GetByteCount(line) + 2;
                    if (bytesWritten >= MaxFileSize)
                    {
                        logWriter.Close();
                        string logDir = Path.Combine(GenFilePaths.SaveDataFolderPath, "LearnToSurvive");
                        string logPath = Path.Combine(logDir, "ColonistAI_Decisions.log");
                        RotateIfNeeded(logPath);
                        logWriter = new StreamWriter(logPath, false, Encoding.UTF8);
                        logWriter.AutoFlush = true;
                        bytesWritten = 0;
                    }
                }
                catch (Exception) { }
            }
        }

        public static void LevelUp(Pawn pawn, StatType stat, int oldLevel, int newLevel, string abilityName)
        {
            // Always log level-ups regardless of log level setting
            string prefix;
            switch (stat)
            {
                case StatType.WorkAwareness: prefix = "[LTS-Work]"; break;
                case StatType.SelfPreservation: prefix = "[LTS-Self]"; break;
                case StatType.CombatInstinct: prefix = "[LTS-Combat]"; break;
                case StatType.PathMemory: prefix = "[LTS-Path]"; break;
                case StatType.HaulingSense: prefix = "[LTS-Haul]"; break;
                default: prefix = "[LTS]"; break;
            }
            Log.Message($"{prefix} {pawn.LabelShort}: LEVEL UP {stat} {oldLevel} -> {newLevel} ({abilityName})");
            string msg = string.Format("[ColonistAI] [TICK:{0}] [PAWN:{1}] [LEVEL_UP] {2} {3} -> {4} | New: {5}",
                Find.TickManager.TicksGame, pawn.LabelShort, stat, oldLevel, newLevel, abilityName);
            WriteLine(msg);
            if (LTSSettings.showLevelUpMessages)
            {
                Messages.Message(
                    "LTS_LevelUpMessage".Translate(pawn.LabelShort, IntelligenceData.GetStatLabel(stat), newLevel),
                    pawn, MessageTypeDefOf.PositiveEvent, false);
            }
        }

        public static void Decision(Pawn pawn, StatType stat, int level, string decisionType,
            string context, string action, string reason)
        {
            if (LTSSettings.logLevel < LogLevel.Decisions) return;
            string msg = string.Format(
                "[ColonistAI] [TICK:{0}] [PAWN:{1}] [STAT:{2}:{3}] Decision:{4} | Context:{5} | Action:{6} | Reason:{7}",
                Find.TickManager.TicksGame, pawn.LabelShort, stat, level,
                decisionType, context, action, reason);
            WriteLine(msg);
        }

        public static void XPGain(Pawn pawn, StatType stat, float baseXP, float totalXP, float progress, int level)
        {
            if (LTSSettings.logLevel < LogLevel.Verbose) return;
            string msg = string.Format(
                "[ColonistAI] [TICK:{0}] [PAWN:{1}] [XP:{2}] Base:{3:F1} Total:{4:F1} Progress:{5:P0} Level:{6}",
                Find.TickManager.TicksGame, pawn.LabelShort, stat,
                baseXP, totalXP, progress, level);
            WriteLine(msg);
        }

        public static void Info(string message)
        {
            if (LTSSettings.logLevel < LogLevel.Summary) return;
            WriteLine("[ColonistAI] [INFO] " + message);
        }

        public static void Warn(string message)
        {
            Log.Warning("[LearnToSurvive] " + message);
            WriteLine("[ColonistAI] [WARN] " + message);
        }

        public static void Error(string message, Exception ex = null)
        {
            string full = "[LearnToSurvive] " + message;
            if (ex != null) full += "\n" + ex;
            Log.Error(full);
            WriteLine("[ColonistAI] [ERROR] " + full);
        }
    }
}
