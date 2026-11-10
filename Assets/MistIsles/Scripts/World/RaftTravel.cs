using System.Collections;
using MistIsles.Core;
using MistIsles.Player;
using MistIsles.UI;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MistIsles.World
{
    /// <summary>
    /// A raft the player boards and steers across the sea.
    ///
    /// Hidden until the island is cleared; then it surfaces on open water by the shore.
    ///   Docked  ── E near it ──▶  Aboard  ── E near any shore ──▶  Docked (where you left it)
    ///
    /// While aboard, WASD / left stick steer relative to the camera (same feel as walking): the
    /// raft turns towards the input and moves forward. It only moves over open water; land stops it.
    /// The player stands on the deck: the character controller keeps running (so the camera
    /// can still be rotated), and after it has moved each frame the player is put back on the
    /// deck (execution order 200, before the camera's LateUpdate). Walking/jump animation is
    /// cancelled, so the character just stands on the raft.
    ///
    /// Replaces the earlier "press E and fade to the next island" version, which could drop the
    /// player in open sea when the landing point was badly placed.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class RaftTravel : MonoBehaviour
    {
        private const int WaterLayer = 4;

        [SerializeField] private Transform destination;
        [SerializeField] private string destinationName = "Whispering Pines";
        [SerializeField] private string homeName = "Still Shore";
        [SerializeField] private GameObject visual;
        [SerializeField] private float interactRadius = 4.5f;

        [Header("Sailing")]
        [SerializeField] private float maxSpeed = 7f;
        [SerializeField] private float acceleration = 4f;
        [SerializeField] private float turnSpeed = 80f;
        [Tooltip("Height of the deck above the water, where the player stands. Negative = measured from the model.")]
        [SerializeField] private float deckHeight = -1f;
        [Tooltip("Lay the model flat automatically if it is imported standing on its side.")]
        [SerializeField] private bool autoLevelModel = true;
        [Tooltip("How far around the raft to look for land to step onto.")]
        [SerializeField] private float shoreReach = 5f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int JumpId = Animator.StringToHash("Jump");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int FreeFallId = Animator.StringToHash("FreeFall");

        /// <summary>True while the player is on a raft (combat and ledge climbing are off).</summary>
        public static bool PlayerAboard { get; private set; }

        private InputAction interact;
        private bool shown;
        private bool aboard;
        private string prompt;
        private float speed;
        private float waterY;
        private bool atFarShore;
        private Vector3? shoreSpot;

        private Transform player;
        private CharacterController playerController;
        private ThirdPersonController tpc;
        private StarterAssetsInputs inputs;
        private Animator playerAnimator;

        // The model is parented under a pivot created at runtime: the pivot moves, turns and bobs
        // on the water, the model keeps its own (import) rotation underneath it.
        private Transform pivot;
        private float halfLength = 1.8f, halfWidth = 1.2f, deck = 0.3f;

        public bool AtFarShore => atFarShore;
        public bool Aboard => aboard;
        public string DestinationName => destinationName;

        public void Configure(Transform target, string targetName, GameObject raftVisual, string originName = "Still Shore")
        {
            destination = target;
            destinationName = targetName;
            homeName = originName;
            visual = raftVisual;
        }

        private void Awake()
        {
            interact = new InputAction("MistIslesInteract", InputActionType.Button);
            interact.AddBinding("<Keyboard>/e");
            interact.AddBinding("<Gamepad>/buttonNorth");

            if (visual != null)
            {
                Vector3 toSea = destination != null ? destination.position - transform.position : transform.forward;
                toSea.y = 0f;
                if (toSea.sqrMagnitude < 0.01f)
                    toSea = Vector3.forward;
                GameObject ocean = GameObject.Find("Ocean");
                waterY = ocean != null ? ocean.transform.position.y : visual.transform.position.y - 0.15f;
                SetUpPivot(toSea.normalized);
                // The setup puts the raft 3 m past the last walkable point, which can still be rock.
                if (FindWaterSpot(transform.position, toSea, out Vector3 spot))
                    pivot.position = spot;
            }
            Hide();
        }

        private void OnEnable() => interact.Enable();
        private void OnDisable()
        {
            interact.Disable();
            if (aboard)
                PlayerAboard = false;
        }
        private void OnDestroy() => interact.Dispose();

        /// <summary>
        /// Creates the pivot on the water, puts the model under it, lays the model flat if it
        /// came in standing up (the flattest of 5 orientations wins, only if clearly flatter),
        /// centres it on the pivot and measures length, width and deck height for the water tests.
        /// </summary>
        private void SetUpPivot(Vector3 heading)
        {
            pivot = new GameObject("MistIsles_RaftPivot").transform;
            pivot.SetParent(transform, true);
            pivot.SetPositionAndRotation(new Vector3(visual.transform.position.x, waterY + 0.1f, visual.transform.position.z), Quaternion.LookRotation(heading));
            visual.transform.SetParent(pivot, true);

            if (autoLevelModel)
            {
                Quaternion original = visual.transform.localRotation;
                Quaternion best = original;
                float bestHeight = ModelBounds().size.y;
                float originalHeight = bestHeight;
                foreach (Vector3 e in new[] { new Vector3(-90f, 0f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0f, 0f, -90f) })
                {
                    visual.transform.localRotation = Quaternion.Euler(e) * original;
                    float h = ModelBounds().size.y;
                    if (h < bestHeight)
                    {
                        bestHeight = h;
                        best = visual.transform.localRotation;
                    }
                }
                visual.transform.localRotation = bestHeight < originalHeight * 0.7f ? best : original;
            }

            // centre the model on the pivot, bottom slightly under the water line
            Bounds b = ModelBounds();
            Vector3 offset = pivot.position - new Vector3(b.center.x, b.min.y + 0.12f, b.center.z);
            visual.transform.position += offset;

            b = ModelBounds();
            Vector3 size = pivot.InverseTransformVector(b.size);
            halfLength = Mathf.Clamp(Mathf.Abs(size.z) * 0.5f, 0.8f, 4f);
            halfWidth = Mathf.Clamp(Mathf.Abs(size.x) * 0.5f, 0.6f, 3f);
            deck = deckHeight >= 0f ? deckHeight : Mathf.Clamp(b.max.y - pivot.position.y, 0.15f, 0.8f);
            // the model's own colliders would block the player standing on the deck
            foreach (Collider c in visual.GetComponentsInChildren<Collider>(true))
                c.enabled = false;
        }

        private Bounds ModelBounds()
        {
            Renderer[] rs = visual.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0)
                return new Bounds(visual.transform.position, Vector3.one);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs)
                b.Encapsulate(r.bounds);
            return b;
        }

        public void Hide()
        {
            shown = false;
            if (visual != null)
                visual.SetActive(false);
        }

        public void Show()
        {
            if (shown)
                return;
            shown = true;
            if (visual != null)
                StartCoroutine(FloatIn());
        }

        private IEnumerator FloatIn()
        {
            visual.SetActive(true);
            Vector3 target = pivot.position;
            Vector3 start = target + Vector3.down * 1.5f;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                pivot.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / 2.5f));
                yield return null;
            }
            pivot.position = target;
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            if (!shown || visual == null || pivot == null)
                return;
            if (aboard)
                Sail();
            else
                Docked();
        }

        private void Docked()
        {
            Bob(0f);
            Transform p = PlayerLocator.Player;
            bool near = p != null && !GamePause.IsPaused && PlayerLocator.IsAlive
                        && Vector3.Distance(Flat(p.position), Flat(pivot.position)) <= interactRadius;
            SetPrompt(near ? "Press E to board the raft" : null);
            if (near && interact.WasPressedThisFrame())
                Board(p);
        }

        private void Board(Transform p)
        {
            player = p;
            playerController = p.GetComponent<CharacterController>();
            tpc = p.GetComponent<ThirdPersonController>();
            inputs = p.GetComponent<StarterAssetsInputs>();
            playerAnimator = p.GetComponentInChildren<Animator>();
            aboard = true;
            PlayerAboard = true;
            speed = 0f;
            AudioManager.Play(Sfx.RaftSplash, pivot.position);
            PlaceOnDeck();
            string to = atFarShore ? homeName : destinationName;
            GameHUD.Toast($"WASD to steer the raft towards {to}. Press E near a shore to step off.", GameHUD.Neutral, 5f);
        }

        private void Sail()
        {
            if (player == null)
            {
                aboard = false;
                PlayerAboard = false;
                return;
            }
            if (GamePause.IsPaused)
            {
                PlaceOnDeck();
                return;
            }

            // steering, relative to the camera like walking
            Vector2 move = inputs != null ? inputs.move : Vector2.zero;
            float targetSpeed = 0f;
            if (move.sqrMagnitude > 0.01f)
            {
                Transform cam = Camera.main != null ? Camera.main.transform : player;
                Vector3 fwd = cam.forward;
                fwd.y = 0f;
                fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 wanted = (fwd * move.y + right * move.x).normalized;
                Quaternion look = Quaternion.LookRotation(wanted);
                pivot.rotation = Quaternion.RotateTowards(Quaternion.Euler(0f, pivot.eulerAngles.y, 0f), look, turnSpeed * Time.deltaTime);
                float facing = Mathf.Max(0f, Vector3.Dot(pivot.forward, wanted));
                targetSpeed = maxSpeed * Mathf.Clamp01(move.magnitude) * facing;
            }
            else
                pivot.rotation = Quaternion.Euler(0f, pivot.eulerAngles.y, 0f);
            speed = Mathf.MoveTowards(speed, targetSpeed, acceleration * Time.deltaTime);

            Vector3 next = pivot.position + pivot.forward * (speed * Time.deltaTime);
            if (speed > 0.01f && !OpenWater(next, pivot.forward))
            {
                speed = 0f; // ran aground: stop at the shore
            }
            else
                pivot.position = new Vector3(next.x, waterY + 0.1f, next.z);

            Bob(speed);
            PlaceOnDeck();

            shoreSpot = FindShore();
            SetPrompt(shoreSpot.HasValue ? "Press E to step ashore" : null);
            if (shoreSpot.HasValue && interact.WasPressedThisFrame())
                Disembark(shoreSpot.Value);
        }

        private void Disembark(Vector3 at)
        {
            aboard = false;
            PlayerAboard = false;
            speed = 0f;
            SetPrompt(null);
            AudioManager.Play(Sfx.RaftSplash, pivot.position);

            Vector3 face = Flat(at) - Flat(pivot.position);
            Quaternion rot = face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face.normalized) : player.rotation;
            PlayerRespawn mover = player.GetComponent<PlayerRespawn>();
            if (mover != null)
            {
                mover.Teleport(at, rot);
                mover.SetFallback(at, rot);
            }
            else
                player.SetPositionAndRotation(at, rot);

            // which side of the crossing are we on (for the objective text)
            float toHome = Vector3.Distance(Flat(at), Flat(transform.position));
            float toDest = destination != null ? Vector3.Distance(Flat(at), Flat(destination.position)) : float.MaxValue;
            bool arrived = toDest < toHome;
            if (arrived != atFarShore)
                GameHUD.Banner(arrived ? destinationName : homeName, arrived ? "Uncharted shore. Your raft waits on the water." : "Back home");
            atFarShore = arrived;
            player = null;
        }

        /// <summary>Stand the player on the deck after the character controller has run this frame.</summary>
        private void PlaceOnDeck()
        {
            Vector3 standAt = new Vector3(pivot.position.x, waterY + 0.1f + deck, pivot.position.z);
            Quaternion rot = Quaternion.Euler(0f, pivot.eulerAngles.y, 0f);
            if (playerController != null)
                playerController.enabled = false;
            player.SetPositionAndRotation(standAt, rot);
            if (playerController != null)
                playerController.enabled = true;

            if (tpc != null)
                tpc.Grounded = true;
            if (inputs != null)
            {
                inputs.JumpInput(false);
                inputs.SprintInput(false);
            }
            if (playerAnimator != null)
            {
                playerAnimator.SetFloat(SpeedId, 0f);
                playerAnimator.SetBool(JumpId, false);
                playerAnimator.SetBool(FreeFallId, false);
                playerAnimator.SetBool(GroundedId, true);
            }
        }

        private void Bob(float currentSpeed)
        {
            float k = 1f + currentSpeed * 0.15f;
            Transform v = pivot;
            float yaw = v.eulerAngles.y;
            v.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.9f) * 2f * k, yaw, Mathf.Sin(Time.time * 0.7f) * 3f * k);
        }

        private void SetPrompt(string text)
        {
            if (text == prompt)
                return;
            prompt = text;
            GameHUD.SetPrompt(text);
        }

        // ------------------------------------------------------------------ water / shore tests

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private bool IsMine(Collider c)
        {
            Transform t = c.transform;
            return (pivot != null && t.IsChildOf(pivot))
                   || (PlayerLocator.Player != null && t.IsChildOf(PlayerLocator.Player));
        }

        /// <summary>First thing straight below a point (ignoring the raft and the player).</summary>
        private bool GroundBelow(Vector3 xz, float fromY, out RaycastHit ground)
        {
            ground = default;
            RaycastHit[] hits = Physics.RaycastAll(new Vector3(xz.x, fromY, xz.z), Vector3.down, 80f, Physics.DefaultRaycastLayers | (1 << WaterLayer), QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            foreach (RaycastHit h in hits)
            {
                if (IsMine(h.collider) || h.distance >= best)
                    continue;
                best = h.distance;
                ground = h;
                found = true;
            }
            return found;
        }

        private static bool IsWater(Collider c) => c.gameObject.layer == WaterLayer || c.gameObject.name == "Ocean";

        /// <summary>Centre and the two front corners of the raft at <paramref name="c"/> are over water.</summary>
        private bool OpenWater(Vector3 c, Vector3 fwd)
        {
            fwd.y = 0f;
            fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            Vector3[] probes = { c, c + fwd * (halfLength + 0.1f) + side * halfWidth, c + fwd * (halfLength + 0.1f) - side * halfWidth, c + fwd * (halfLength + 0.4f) };
            foreach (Vector3 p in probes)
                if (!GroundBelow(p, waterY + 30f, out RaycastHit h) || !IsWater(h.collider))
                    return false;
            return true;
        }

        /// <summary>The nearest walkable land within reach of the raft, if any.</summary>
        private Vector3? FindShore()
        {
            Vector3 c = pivot.position;
            Vector3? best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                for (float d = 1.5f; d <= shoreReach; d += 0.5f)
                {
                    Vector3 p = c + dir * d;
                    if (!GroundBelow(p, waterY + 30f, out RaycastHit h))
                        continue;
                    if (IsWater(h.collider))
                        continue;
                    float rise = h.point.y - waterY;
                    if (h.normal.y < 0.7f || rise > 3f || rise < -0.5f)
                        break; // a cliff in this direction
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = h.point + Vector3.up * 0.05f;
                    }
                    break;
                }
            }
            return best;
        }

        /// <summary>
        /// Walks from the shore point out to sea and returns the first spot where the raft's
        /// footprint (centre + 4 corners) is over water. The spot is on the water surface.
        /// </summary>
        private bool FindWaterSpot(Vector3 from, Vector3 towards, out Vector3 spot)
        {
            spot = Vector3.zero;
            towards.y = 0f;
            if (towards.sqrMagnitude < 0.01f)
                return false;
            Vector3 dir = towards.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            float hl = halfLength + 0.2f, hw = halfWidth + 0.2f;
            var footprint = new[] { Vector3.zero, side * hw + dir * hl, -side * hw + dir * hl, side * hw - dir * hl, -side * hw - dir * hl };
            for (float d = 2f; d <= 40f; d += 0.5f)
            {
                Vector3 c = from + dir * d;
                bool allWater = true;
                float y = 0f;
                foreach (Vector3 o in footprint)
                {
                    if (!GroundBelow(c + o, from.y + 30f, out RaycastHit hit) || !IsWater(hit.collider))
                    {
                        allWater = false;
                        break;
                    }
                    y = hit.point.y;
                }
                if (allWater)
                {
                    Vector3 c2 = c + dir * 0.75f;
                    spot = new Vector3(c2.x, y + 0.1f, c2.z);
                    return true;
                }
            }
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PlayerAboard = false;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(pivot != null ? pivot.position : transform.position, interactRadius);
            if (destination != null)
                Gizmos.DrawLine(transform.position, destination.position);
        }
    }
}
