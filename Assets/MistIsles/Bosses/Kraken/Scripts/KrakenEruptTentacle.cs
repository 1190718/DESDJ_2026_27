using System.Collections;
using UnityEngine;

namespace MistIsles.Bosses.Kraken
{
    /// <summary>Phase 3 attack: a warning ripple appears under the player, then a tentacle bursts out of the ground.</summary>
    public class KrakenEruptTentacle : MonoBehaviour
    {
        public Animator animator;
        public GameObject warningRipple;
        public float warningTime = 1.0f;
        public float knockUp = 6f;

        KrakenBoss boss;
        int damage;
        Transform[] bones;
        Renderer[] renderers;

        public void Init(KrakenBoss owner, int dmg)
        {
            boss = owner;
            damage = dmg;
            StartCoroutine(Run());
        }

        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            bones = KrakenGeometry.FindChain(transform, "T_ER_", 8);
            renderers = animator != null ? animator.GetComponentsInChildren<Renderer>() : new Renderer[0];
        }

        IEnumerator Run()
        {
            SetVisible(false);
            if (animator != null) animator.Play("Erupt_Hidden", 0, 0f);
            if (warningRipple != null) warningRipple.SetActive(true);
            float t = 0f;
            Vector3 rippleScale = warningRipple != null ? warningRipple.transform.localScale : Vector3.one;
            while (t < warningTime)
            {
                if (warningRipple != null)
                    warningRipple.transform.localScale = rippleScale * (0.8f + 0.25f * Mathf.Abs(Mathf.Sin(t * 9f)));
                t += Time.deltaTime;
                yield return null;
            }
            if (warningRipple != null) warningRipple.SetActive(false);
            SetVisible(true);
            if (animator != null) animator.Play("Erupt", 0, 0f);

            bool hit = false;
            t = 0f;
            float scale = Mathf.Abs(transform.lossyScale.x);
            while (t < KrakenAnimData.Erupt_Length)
            {
                if (!hit && boss != null && t >= KrakenAnimData.Erupt_HitStart && t <= KrakenAnimData.Erupt_HitEnd && boss.player != null)
                {
                    if (KrakenGeometry.ChainHitsPlayer(bones, KrakenAnimData.EruptTentacleRadii, scale, KrakenGeometry.GetPlayerBody(boss.player)))
                    {
                        hit = true;
                        Vector3 away = boss.player.position - transform.position;
                        away.y = 0f;
                        boss.DamagePlayer(damage, transform.position, away.normalized + Vector3.up * (knockUp / Mathf.Max(0.1f, boss.knockback)), "Erupt");
                    }
                }
                t += Time.deltaTime;
                yield return null;
            }
            Destroy(gameObject);
        }

        void SetVisible(bool v)
        {
            foreach (var r in renderers) if (r != null) r.enabled = v;
        }
    }
}
