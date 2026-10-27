using System.Collections;
using System.Collections.Generic;
using MistIsles.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MistIsles.Player
{
    /// <summary>
    /// Sword attack: left mouse / F / gamepad West. Plays the "Attack" animator trigger,
    /// waits for the swing to reach its hit frame, then damages every Health in a sphere
    /// in front of the player. Attacking does not move the player, which matters on
    /// Still Shore: you can strike while the creatures stay frozen.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private int damage = 1;
        [SerializeField] private float cooldown = 0.55f;
        [Tooltip("Delay between pressing attack and the hit landing (matches the swing animation).")]
        [SerializeField] private float hitDelay = 0.25f;
        [SerializeField] private float reach = 1.3f;
        [SerializeField] private float hitRadius = 1.1f;
        [SerializeField] private float hitHeight = 0.9f;
        [Tooltip("On attack, turn towards the nearest enemy within this distance.")]
        [SerializeField] private float autoAimRadius = 3.5f;

        private static readonly int AttackId = Animator.StringToHash("Attack");

        private InputAction attackAction;
        private Animator animator;
        private bool hasAttackParameter;
        private Health health;
        private float nextAttack;

        private void Awake()
        {
            health = GetComponent<Health>();
            attackAction = new InputAction("MistIslesAttack", InputActionType.Button);
            attackAction.AddBinding("<Mouse>/leftButton");
            attackAction.AddBinding("<Keyboard>/f");
            attackAction.AddBinding("<Gamepad>/buttonWest");
        }

        private void Start()
        {
            animator = GetComponentInChildren<Animator>();
            hasAttackParameter = false;
            if (animator != null && animator.runtimeAnimatorController != null)
                foreach (AnimatorControllerParameter p in animator.parameters)
                    if (p.nameHash == AttackId)
                        hasAttackParameter = true;
        }

        private void OnEnable() => attackAction.Enable();
        private void OnDisable() => attackAction.Disable();
        private void OnDestroy() => attackAction.Dispose();

        private void Update()
        {
            if (GamePause.IsPaused || health.IsDead)
                return;
            if (attackAction.WasPressedThisFrame() && Time.time >= nextAttack)
                StartCoroutine(Attack());
        }

        private IEnumerator Attack()
        {
            nextAttack = Time.time + cooldown;
            FaceNearestEnemy();
            if (hasAttackParameter)
                animator.SetTrigger(AttackId);
            AudioManager.Play(Sfx.SwordSwing, transform.position);

            yield return new WaitForSeconds(hitDelay);
            if (health.IsDead)
                yield break;

            Vector3 centre = transform.position + Vector3.up * hitHeight + transform.forward * reach;
            var already = new HashSet<Health>();
            foreach (Collider c in Physics.OverlapSphere(centre, hitRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Health target = c.GetComponentInParent<Health>();
                if (target == null || target == health || target.IsDead || !already.Add(target))
                    continue;
                target.TakeDamage(new DamageInfo(damage, c.ClosestPoint(centre), transform.forward, gameObject));
            }
        }

        private void FaceNearestEnemy()
        {
            Health best = null;
            float bestDist = autoAimRadius;
            foreach (Collider c in Physics.OverlapSphere(transform.position, autoAimRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Health h = c.GetComponentInParent<Health>();
                if (h == null || h == health || h.IsDead)
                    continue;
                float d = Vector3.Distance(transform.position, h.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = h;
                }
            }
            if (best == null)
                return;
            Vector3 to = best.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(to);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * hitHeight + transform.forward * reach, hitRadius);
        }
    }
}
