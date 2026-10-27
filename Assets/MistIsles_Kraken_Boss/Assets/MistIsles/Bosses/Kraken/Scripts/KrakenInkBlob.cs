using UnityEngine;

namespace MistIsles.Bosses.Kraken
{
    /// <summary>Ink projectile. Lands where the player was; dissolves in shallow water, leaves a puddle on land.</summary>
    public class KrakenInkBlob : MonoBehaviour
    {
        public float spinSpeed = 420f;
        [Tooltip("Hit radius while flying.")]
        public float directHitRadius = 0.7f;
        [Tooltip("Splash radius on landing.")]
        public float splashRadius = 1.8f;

        KrakenBoss boss;
        Vector3 start, end, spinAxis;
        float duration, arc, t;
        int damage, puddleDamage;
        bool launched, hitPlayer;

        public void Launch(KrakenBoss owner, Vector3 target, float flightTime, float arcHeight, int dmg, int puddleDmg)
        {
            boss = owner;
            start = transform.position;
            end = target;
            duration = Mathf.Max(0.1f, flightTime);
            arc = arcHeight;
            damage = dmg;
            puddleDamage = puddleDmg;
            spinAxis = Random.onUnitSphere;
            launched = true;
        }

        void Update()
        {
            if (!launched) return;
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            transform.position = Vector3.Lerp(start, end, u) + Vector3.up * (4f * arc * u * (1f - u));
            transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
            float s = 1f + 0.12f * Mathf.Sin(t * 25f);
            transform.localScale = new Vector3(s, 2f - s, s);

            bool protectedByWater = boss != null && boss.waterProtectsFromGrabAndInk && boss.PlayerInWater;
            if (!hitPlayer && boss != null && !protectedByWater && boss.PlayerWithin(transform.position, directHitRadius))
            {
                hitPlayer = true;
                boss.DamagePlayer(damage, transform.position, (end - start).normalized + Vector3.up * 0.3f, "Ink");
            }
            if (u >= 1f) Land();
        }

        void Land()
        {
            launched = false;
            if (KrakenWaterZone.AnyContainsXZ(end))
            {
                if (boss != null) boss.NotifyInkDissolved(end);
            }
            else if (boss != null)
            {
                bool protectedByWater = boss.waterProtectsFromGrabAndInk && boss.PlayerInWater;
                if (!hitPlayer && !protectedByWater && boss.PlayerWithin(end, splashRadius))
                    boss.DamagePlayer(damage, end, Vector3.up * 0.3f, "Ink");
                if (boss.inkPuddlePrefab != null)
                {
                    var p = Instantiate(boss.inkPuddlePrefab, end + Vector3.up * 0.02f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    p.Init(boss, puddleDamage);
                }
            }
            Destroy(gameObject);
        }
    }
}
