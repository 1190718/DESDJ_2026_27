using System;
using System.Collections.Generic;
using UnityEngine;

namespace MistIsles.Core
{
    /// <summary>
    /// One place that owns Time.timeScale and the cursor. Several screens can pause at
    /// once (quiz, pause menu, shop); the game only resumes when the last one closes.
    /// </summary>
    public static class GamePause
    {
        public const string Quiz = "quiz";
        public const string Menu = "menu";
        public const string Shop = "shop";
        public const string EndCard = "endcard";

        private static readonly HashSet<string> Reasons = new HashSet<string>();

        public static bool IsPaused => Reasons.Count > 0;
        public static event Action<bool> Changed;

        public static bool Has(string reason) => Reasons.Contains(reason);

        public static void Push(string reason)
        {
            bool was = IsPaused;
            Reasons.Add(reason);
            Apply(was);
        }

        public static void Pop(string reason)
        {
            bool was = IsPaused;
            Reasons.Remove(reason);
            Apply(was);
        }

        /// <summary>Drops every pause reason (used when leaving the scene).</summary>
        public static void ClearAll()
        {
            bool was = IsPaused;
            Reasons.Clear();
            Apply(was);
        }

        private static void Apply(bool wasPaused)
        {
            Time.timeScale = IsPaused ? 0f : 1f;
            Cursor.lockState = IsPaused ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = IsPaused;
            if (wasPaused != IsPaused)
                Changed?.Invoke(IsPaused);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Reasons.Clear();
            Changed = null;
        }
    }
}
