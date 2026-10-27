using UnityEngine;

namespace MistIsles.Core
{
    /// <summary>Counters shown on the end-of-prototype card and useful for playtest notes.</summary>
    public static class GameStats
    {
        public static float StartTime { get; private set; }
        public static int CrabsDefeated { get; set; }
        public static int Deaths { get; set; }
        public static int WrongAnswers { get; set; }
        public static int ShellsCollected { get; set; }

        public static float PlayTime => Time.time - StartTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void ResetAll()
        {
            StartTime = Time.time;
            CrabsDefeated = 0;
            Deaths = 0;
            WrongAnswers = 0;
            ShellsCollected = 0;
        }

        public static string FormatTime(float seconds)
        {
            int s = Mathf.FloorToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }
    }
}
