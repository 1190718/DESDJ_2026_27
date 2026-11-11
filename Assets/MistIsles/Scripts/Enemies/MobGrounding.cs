using MistIsles.Core;
using UnityEngine;
using UnityEngine.AI;

namespace MistIsles.Enemies
{
    /// <summary>
    /// Makes creatures stand on the real ground, like the player, and drop in when they spawn.
    ///
    /// The NavMeshAgent walks on the baked navigation mesh, which is only an approximation of
    /// the ground (it can sit a few tens of centimetres below rocky or uneven surfaces), so the
    /// creatures looked half buried. The agent still decides where they walk, but this component
    /// casts a ray down onto the actual colliders every frame and lifts or lowers the model
    /// (the "Model" child) so its feet rest on what the player would stand on.
    ///
    /// On spawn the model starts a little above the ground and falls with gravity until it lands,
    /// with a small squash on impact. Palm leaves and other things far above the creature are
    /// ignored, so it never jumps onto a tree.
    /// Added automatically by EnemyBrain, so existing prefabs get it without being rebuilt.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class MobGrounding : MonoBehaviour
    {
        [Tooltip("Height above the ground the creature appears at before falling.")]
        [SerializeField] private float dropHeight = 1.6f;
        [SerializeField] private float gravity = 22f;
        [Tooltip("Highest bump above the navigation mesh the model will step onto.")]
        [SerializeField] private float maxRise = 1.2f;
        [Tooltip("Lowest dip below the navigation mesh the model will follow.")]
        [SerializeField] private float maxDrop = 0.8f;

        private Transform model;
        private Vector3 baseLocal;
        private Vector3 baseScale;
        private float offset;          // current model height relative to the agent
        private float fallSpeed;
        private bool falling = true;
        private float squash;

        private void Awake()
        {
            model = transform.Find("Model");
            if (model == null && transform.childCount > 0)
                model = transform.GetChild(0);
            if (model != null)
            {
                baseLocal = model.localPosition;
                baseScale = model.localScale;
            }
        }

        private void Start()
        {
            offset = GroundOffset() + dropHeight;
            fallSpeed = 0f;
            falling = true;
            Apply();
        }

        private void LateUpdate()
        {
            if (model == null)
                return;
            float target = GroundOffset();
            if (falling)
            {
                fallSpeed += gravity * Time.deltaTime;
                offset -= fallSpeed * Time.deltaTime;
                if (offset <= target)
                {
                    offset = target;
                    falling = false;
                    squash = Mathf.Clamp01(fallSpeed / 10f) * 0.25f;
                }
            }
            else
            {
                // follow bumps smoothly; step down quickly, like gravity would
                float rate = target > offset ? 10f : 18f;
                offset = Mathf.MoveTowards(offset, target, rate * Time.deltaTime);
            }
            squash = Mathf.MoveTowards(squash, 0f, Time.deltaTime * 1.5f);
            Apply();
        }

        private void Apply()
        {
            if (model == null)
                return;
            model.localPosition = baseLocal + transform.InverseTransformVector(Vector3.up * offset);
            float s = 1f - squash;
            model.localScale = new Vector3(baseScale.x * (2f - s), baseScale.y * s, baseScale.z * (2f - s));
        }

        /// <summary>Height of the real ground under the creature, relative to its agent position.</summary>
        private float GroundOffset()
        {
            Vector3 p = transform.position;
            RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * (maxRise + 0.5f), Vector3.down, maxRise + 0.5f + maxDrop,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (RaycastHit h in hits)
            {
                Collider c = h.collider;
                if (c.transform.IsChildOf(transform) || c.GetComponentInParent<Health>() != null
                    || c.GetComponentInParent<NavMeshAgent>() != null || c.GetComponentInParent<CharacterController>() != null)
                    continue; // other creatures and the player are not ground
                if (h.normal.y < 0.45f)
                    continue; // walls and steep faces
                if (h.point.y > best)
                    best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? 0f : best - p.y;
        }
    }
}
