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
        [SerializeField] private int damage = 10;
        [SerializeField] private float cooldown = 0.55f;
        [Tooltip("Delay between pressing attack and the hit landing (matches the swing animation).")]
        [SerializeField] private float hitDelay = 0.25f;
        [SerializeField] private float reach = 1.8f;
        [SerializeField] private float hitRadius = 1.5f;
        [SerializeField] private float hitHeight = 0.9f;
        [Tooltip("On attack, turn towards the nearest enemy within this distance.")]
        [SerializeField] private float autoAimRadius = 4.5f;

        [Header("Slash effect")]
        [Tooltip("Unlit transparent vertex-colour material for the air slash (built by the setup tool).")]
        [SerializeField] private Material slashMaterial;
        [Tooltip("Seconds after pressing attack when the slash appears (a little before the hit).")]
        [SerializeField] private float slashDelay = 0.14f;

        private static readonly int AttackId = Animator.StringToHash("Attack");

        private InputAction attackAction;
        private Animator animator;
        private bool hasAttackParameter;
        private Health health;
        private float nextAttack;
        private bool slashReverse;

        private void Awake()
        {
            health = GetComponent<Health>();
            attackAction = new InputAction("MistIslesAttack", InputActionType.Button);
            attackAction.AddBinding("<Mouse>/leftButton");
            attackAction.AddBinding("<Keyboard>/f");
            attackAction.AddBinding("<Gamepad>/buttonWest");

            // Scenes set up before the grounding fix get it automatically (no need to re-run the setup).
            if (GetComponent<StarterAssets.ThirdPersonController>() != null && GetComponent<PlayerGroundingAssist>() == null)
                gameObject.AddComponent<PlayerGroundingAssist>();
            if (GetComponent<PlayerBoundary>() == null)
                gameObject.AddComponent<PlayerBoundary>();
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
            if (GamePause.IsPaused || health.IsDead || MistIsles.World.RaftTravel.PlayerAboard)
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

            // the visible "air mark": alternates direction and tilt so combos read as separate cuts
            yield return new WaitForSeconds(slashDelay);
            slashReverse = !slashReverse;
            float tilt = (slashReverse ? -1f : 1f) * Random.Range(12f, 28f);
            SwordSlash.Spawn(transform, slashMaterial, hitHeight + 0.15f, reach + hitRadius * 0.45f, tilt, slashReverse);

            yield return new WaitForSeconds(Mathf.Max(0f, hitDelay - slashDelay));
            if (health.IsDead)
                yield break;

            Vector3 centre = transform.position + Vector3.up * hitHeight + transform.forward * reach;
            var already = new HashSet<Health>();
            foreach (Collider c in Physics.OverlapSphere(centre, hitRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Health target = c.GetComponentInParent<Health>();
                if (target == null || target.IsDead || IsSelf(target, c) || !already.Add(target))
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
                if (h == null || h.IsDead || IsSelf(h, c))
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

        /// <summary>
        /// True for anything that belongs to the player: its own Health, colliders on the
        /// character (CharacterController, sword, children) and any other object tagged Player.
        /// </summary>
        private bool IsSelf(Health target, Collider hit)
        {
            if (target == health || target == PlayerLocator.Health)
                return true;
            if (hit.transform.IsChildOf(transform) || target.transform.IsChildOf(transform) || transform.IsChildOf(target.transform))
                return true;
            return target.CompareTag("Player");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * hitHeight + transform.forward * reach, hitRadius);
        }
    }
}
