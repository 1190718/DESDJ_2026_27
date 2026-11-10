using System.Collections;
using MistIsles.Core;
using StarterAssets;
using UnityEngine;

namespace MistIsles.Player
{
    /// <summary>
    /// Fixes two problems of the Starter Assets controller on our islands, without editing the package:
    ///
    /// 1. Grounding. The controller only counts as grounded when a small sphere under the
    ///    pelvis touches the Default layer. The ocean is on the Water layer and, on rocky edges,
    ///    the sphere hangs in the air while the capsule still rests on the rim. The controller then
    ///    thinks it is falling: the fall animation flickers, jumping is disabled and gravity keeps
    ///    accumulating (the package never clamps it), so the character jerks and slides at edges.
    ///    Here the water counts as ground and a capsule resting on anything (CharacterController
    ///    "Below" contact) also counts, with a short grace time for tiny bumps.
    ///
    /// 2. Climbing. The capsule can only step up 0.25 m, so leaving the water only worked where
    ///    the shore was almost flat. When the player keeps pushing against a ledge that has
    ///    walkable ground on top and room to stand, the character vaults onto it. Ledges can be
    ///    higher when climbing out of the water than on land.
    ///
    /// Runs after ThirdPersonController so its grounded value is the one used next frame.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerGroundingAssist : MonoBehaviour
    {
        private const int WaterLayer = 4;

        [Header("Grounding")]
        [Tooltip("Seconds the player still counts as grounded after losing contact (small bumps, rock seams).")]
        [SerializeField] private float coyoteTime = 0.12f;
        [Tooltip("Step height in metres (Starter Assets default is 0.25).")]
        [SerializeField] private float stepOffset = 0.4f;
        [SerializeField] private float slopeLimit = 50f;

        [Header("Ledge climb")]
        [Tooltip("Highest ledge the player can climb when standing in the water.")]
        [SerializeField] private float maxClimbFromWater = 1.9f;
        [Tooltip("Also climb ledges on land. Off by default: on rocky shores it fired again and again and the player seemed to teleport.")]
        [SerializeField] private bool climbOnLand = false;
        [Tooltip("Highest ledge the player can climb on land (only if Climb On Land is on).")]
        [SerializeField] private float maxClimbOnLand = 1.1f;
        [Tooltip("Seconds of pushing straight against a ledge before climbing.")]
        [SerializeField] private float pushTime = 0.3f;
        [Tooltip("Seconds after a climb before another one can start.")]
        [SerializeField] private float climbCooldown = 1f;
        [SerializeField] private float climbSecondsPerMetre = 0.22f;

        private ThirdPersonController tpc;
        private StarterAssetsInputs input;
        private CharacterController controller;
        private Animator animator;
        private Health health;
        private float lastGroundedTime = float.NegativeInfinity;
        private float pushing;
        private bool climbing;
        private float nextClimb;

        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int FreeFallId = Animator.StringToHash("FreeFall");

        public bool IsClimbing => climbing;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            tpc = GetComponent<ThirdPersonController>();
            input = GetComponent<StarterAssetsInputs>();
            health = GetComponent<Health>();
            animator = GetComponentInChildren<Animator>();

            controller.stepOffset = stepOffset;
            controller.slopeLimit = slopeLimit;
            if (tpc != null)
                tpc.GroundLayers |= (1 << WaterLayer) | 1; // Default + Water
        }

        private float Scale => Mathf.Max(0.01f, transform.lossyScale.y);
        private float Radius => controller.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
        private float Height => controller.height * Scale;

        private void Update()
        {
            if (tpc == null || climbing || MistIsles.World.RaftTravel.PlayerAboard)
                return;

            // ---- 1. grounding
            bool grounded = tpc.Grounded || controller.isGrounded;
            if (grounded)
                lastGroundedTime = Time.time;
            else if (Time.time - lastGroundedTime <= coyoteTime && controller.velocity.y < 0.5f)
                grounded = true; // grace time, but never while rising from a jump
            if (grounded && !tpc.Grounded)
            {
                tpc.Grounded = true;
                if (animator != null)
                    animator.SetBool(GroundedId, true);
            }

            // ---- 2. ledge climb
            if (GamePause.IsPaused || (health != null && health.IsDead) || !grounded || input == null || input.move.sqrMagnitude < 0.25f)
            {
                pushing = 0f;
                return;
            }
            Vector3 v = controller.velocity;
            v.y = 0f;
            if (Time.time < nextClimb || v.magnitude > tpc.MoveSpeed * 0.3f || !FindLedge(out Vector3 top))
            {
                pushing = 0f;
                return;
            }
            pushing += Time.deltaTime;
            if (pushing >= pushTime)
            {
                pushing = 0f;
                StartCoroutine(Climb(top));
            }
        }

        private bool InWater()
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
                return hit.collider.gameObject.layer == WaterLayer || hit.collider.gameObject.name == "Ocean";
            return false;
        }

        /// <summary>Wall in front, walkable top within reach, and room for the capsule up there.</summary>
        private bool FindLedge(out Vector3 top)
        {
            top = Vector3.zero;
            float r = Radius;
            float h = Height;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            bool inWater = InWater();
            if (!inWater && !climbOnLand)
                return false;
            float maxClimb = inWater ? maxClimbFromWater : maxClimbOnLand;

            // wall at knee height in front of us
            Vector3 knee = transform.position + Vector3.up * (stepOffset + 0.1f);
            if (!Physics.SphereCast(knee, r * 0.9f, fwd, out RaycastHit wall, 0.5f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            if (IsMine(wall.collider) || wall.normal.y > 0.7f || wall.collider.GetComponentInParent<Health>() != null)
                return false;
            // only when facing the wall, not when brushing along it
            Vector3 into = -wall.normal;
            into.y = 0f;
            if (into.sqrMagnitude < 0.01f || Vector3.Dot(into.normalized, fwd) < 0.7f)
                return false;

            // walkable surface on top of it
            float probeUp = maxClimb + 0.3f;
            Vector3 probe = transform.position + fwd * (wall.distance + r + 0.35f) + Vector3.up * probeUp;
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit ground, probeUp + 0.1f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            if (IsMine(ground.collider) || ground.normal.y < 0.7f || ground.collider.gameObject.layer == WaterLayer)
                return false;
            float rise = ground.point.y - transform.position.y;
            if (rise <= stepOffset * 0.8f || rise > maxClimb)
                return false;

            // room to stand (ignore our own collider)
            Vector3 bottom = ground.point + Vector3.up * (r + 0.05f);
            Vector3 head = ground.point + Vector3.up * Mathf.Max(r + 0.06f, h - r);
            foreach (Collider c in Physics.OverlapCapsule(bottom, head, r * 0.9f, ~0, QueryTriggerInteraction.Ignore))
                if (!IsMine(c))
                    return false;
            // nothing above our own head on the way up
            if (Physics.Raycast(transform.position + Vector3.up * (h - 0.05f), Vector3.up, out RaycastHit ceiling, rise + 0.1f, ~0, QueryTriggerInteraction.Ignore) && !IsMine(ceiling.collider))
                return false;

            top = ground.point;
            return true;
        }

        private bool IsMine(Collider c) => c.transform == transform || c.transform.IsChildOf(transform);

        private IEnumerator Climb(Vector3 top)
        {
            climbing = true;
            tpc.enabled = false;
            controller.enabled = false;
            if (input != null)
                input.JumpInput(false);
            if (animator != null)
            {
                animator.SetBool(JumpId, true);
                animator.SetBool(GroundedId, false);
                animator.SetBool(FreeFallId, false);
            }

            Vector3 start = transform.position;
            float rise = Mathf.Max(0.1f, top.y - start.y);
            Vector3 overEdge = new Vector3(start.x, top.y + 0.08f, start.z) + (top - start).WithY(0f) * 0.25f;

            // up along the wall, then forward onto the ledge
            float upTime = 0.1f + rise * climbSecondsPerMetre;
            for (float t = 0f; t < upTime; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(start, overEdge, Mathf.SmoothStep(0f, 1f, t / upTime));
                yield return null;
            }
            Vector3 end = top + Vector3.up * 0.02f;
            const float forwardTime = 0.14f;
            for (float t = 0f; t < forwardTime; t += Time.deltaTime)
            {
                transform.position = Vector3.Lerp(overEdge, end, t / forwardTime);
                yield return null;
            }
            transform.position = end;

            controller.enabled = true;
            tpc.enabled = true;
            tpc.Grounded = true;
            lastGroundedTime = Time.time;
            if (animator != null)
            {
                animator.SetBool(JumpId, false);
                animator.SetBool(GroundedId, true);
            }
            climbing = false;
            nextClimb = Time.time + climbCooldown;
        }

        private void OnDisable()
        {
            if (!climbing)
                return;
            StopAllCoroutines();
            climbing = false;
            if (controller != null)
                controller.enabled = true;
            if (tpc != null)
                tpc.enabled = true;
        }
    }

    internal static class VectorExtensions
    {
        public static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);
    }
}
