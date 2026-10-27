using UnityEngine;

namespace MistIsles.Bosses.Kraken
{
    /// <summary>Ink puddle left on dry land. Damages the player over time while they stand in it.</summary>
    public class KrakenInkPuddle : MonoBehaviour
    {
        public float lifetime = 6f;
        public float radius = 1.5f;
        public float tickInterval = 0.5f;

        KrakenBoss boss;
        int damagePerTick;
        float age, nextTick;
        Vector3 baseScale;

        public void Init(KrakenBoss owner, int tickDamage)
        {
            boss = owner;
            damagePerTick = tickDamage;
        }

        void Awake()
        {
            baseScale = transform.localScale;
            transform.localScale = baseScale * 0.01f;
        }

        void Update()
        {
            age += Time.deltaTime;
            float grow = Mathf.Clamp01(age / 0.2f);
            float shrink = Mathf.Clamp01((lifetime - age) / 0.6f);
            transform.localScale = baseScale * Mathf.Max(0.01f, grow * shrink);
            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
            if (boss == null || damagePerTick <= 0 || age < nextTick || boss.player == null) return;
            Vector3 d = boss.player.position - transform.position;
            if (Mathf.Abs(d.y) < 1.2f && new Vector2(d.x, d.z).magnitude <= radius * baseScale.x)
            {
                nextTick = age + tickInterval;
                boss.DamagePlayer(damagePerTick, transform.position, Vector3.zero, "InkPuddle", true);
            }
        }
    }
}
