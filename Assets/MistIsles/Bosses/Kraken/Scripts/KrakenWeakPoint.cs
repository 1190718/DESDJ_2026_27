using UnityEngine;

namespace MistIsles.Bosses.Kraken
{
    /// <summary>
    /// The glowing crystals on the kraken's head. Only takes damage while the kraken is stunned (by default).
    /// Hook it up to the sword in any of these ways:
    ///  1. The sword has a trigger collider tagged 'weaponTag' -> damage = boss.defaultWeaponDamage.
    ///  2. The sword script calls GetComponentInParent&lt;KrakenWeakPoint&gt;()?.TakeDamage(dmg) on what it hit.
    ///  3. The sword script uses SendMessage("TakeDamage", intDamage) or SendMessage("ApplyDamage", floatDamage).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class KrakenWeakPoint : MonoBehaviour
    {
        public KrakenBoss boss;
        [Tooltip("Tag of the sword/weapon collider. Leave empty to only accept TakeDamage calls.")]
        public string weaponTag = "PlayerWeapon";

        void Awake()
        {
            if (boss == null) boss = GetComponentInParent<KrakenBoss>();
        }

        public void TakeDamage(int amount)
        {
            if (boss != null) boss.HandleWeakPointHit(amount);
        }

        public void ApplyDamage(float amount)
        {
            TakeDamage(Mathf.RoundToInt(amount));
        }

        void OnTriggerEnter(Collider other)
        {
            if (boss == null || string.IsNullOrEmpty(weaponTag)) return;
            if (other.gameObject.tag == weaponTag) boss.HandleWeakPointHit(boss.defaultWeaponDamage);
        }
    }
}
