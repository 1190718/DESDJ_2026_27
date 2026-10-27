using UnityEngine;

namespace MistIsles.Core
{
    /// <summary>Finds and caches the active object tagged "Player" (the PlayerArmature).</summary>
    public static class PlayerLocator
    {
        private static Transform cached;
        private static Health cachedHealth;

        public static Transform Player
        {
            get
            {
                if (cached == null || !cached.gameObject.activeInHierarchy)
                {
                    GameObject go = GameObject.FindGameObjectWithTag("Player");
                    cached = go != null ? go.transform : null;
                    cachedHealth = null;
                }
                return cached;
            }
        }

        public static Health Health
        {
            get
            {
                if (cachedHealth == null && Player != null)
                    cachedHealth = Player.GetComponent<Health>();
                return cachedHealth;
            }
        }

        public static bool IsAlive => Player != null && (Health == null || !Health.IsDead);

        /// <summary>Roughly the player's chest, used for line-of-sight checks.</summary>
        public static Vector3 AimPoint => Player != null ? Player.position + Vector3.up * 1.1f : Vector3.zero;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cached = null;
            cachedHealth = null;
        }
    }
}
