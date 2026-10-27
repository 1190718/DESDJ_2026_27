using System;
using UnityEngine;

namespace MistIsles.Core
{
    /// <summary>Everything a hit carries: how much, where, from which direction and who caused it.</summary>
    public readonly struct DamageInfo
    {
        public readonly int Amount;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;
        public readonly GameObject Source;

        public DamageInfo(int amount, Vector3 point, Vector3 direction, GameObject source)
        {
            Amount = amount;
            Point = point;
            Direction = direction;
            Source = source;
        }
    }

    /// <summary>
    /// Hit points shared by the player, the Mist Crabs and the Shore Guardian.
    /// Other components react through the events instead of polling.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 3;
        [Tooltip("Seconds after a hit during which further hits are ignored.")]
        [SerializeField, Min(0f)] private float invulnerableAfterHit = 0f;

        private float lastHitTime = float.NegativeInfinity;

        public int Max => maxHealth;
        public int Current { get; private set; }
        public bool IsDead => Current <= 0;

        /// <summary>Optional gate: return false to block the hit (used by the guardian's armour).</summary>
        public Func<DamageInfo, bool> DamageFilter;

        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> Blocked;
        public event Action Died;
        /// <summary>(current, max)</summary>
        public event Action<int, int> Changed;

        private void Awake()
        {
            Current = maxHealth;
        }

        /// <summary>Applies damage. Returns true if the hit landed.</summary>
        public bool TakeDamage(DamageInfo info)
        {
            if (IsDead || info.Amount <= 0)
                return false;
            if (Time.time < lastHitTime + invulnerableAfterHit)
                return false;
            if (DamageFilter != null && !DamageFilter(info))
            {
                Blocked?.Invoke(info);
                return false;
            }

            lastHitTime = Time.time;
            Current = Mathf.Max(0, Current - info.Amount);
            Changed?.Invoke(Current, maxHealth);
            Damaged?.Invoke(info);
            if (Current == 0)
                Died?.Invoke();
            return true;
        }

        public void Heal(int amount)
        {
            if (IsDead || amount <= 0)
                return;
            Current = Mathf.Min(maxHealth, Current + amount);
            Changed?.Invoke(Current, maxHealth);
        }

        public void SetMax(int newMax, bool refill)
        {
            maxHealth = Mathf.Max(1, newMax);
            Current = refill ? maxHealth : Mathf.Min(Current, maxHealth);
            Changed?.Invoke(Current, maxHealth);
        }

        public void ResetHealth()
        {
            Current = maxHealth;
            lastHitTime = float.NegativeInfinity;
            Changed?.Invoke(Current, maxHealth);
        }
    }
}
