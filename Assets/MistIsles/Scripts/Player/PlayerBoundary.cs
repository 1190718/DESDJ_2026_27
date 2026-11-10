using MistIsles.World;
using UnityEngine;

namespace MistIsles.Player
{
    /// <summary>
    /// Keeps the player inside the current island's area until that island is cleared.
    ///
    /// It runs in Update right after the character controller has moved (execution order 150,
    /// after ThirdPersonController and PlayerGroundingAssist) and before any LateUpdate. The
    /// Cinemachine camera follows in LateUpdate, so it only ever sees the clamped position.
    /// (When this was done in LateUpdate the camera could read the position before the push-back
    /// and drift past the border.)
    ///
    /// The edge is a soft wall: inside the last few metres the outward part of the movement is
    /// damped, so the player slows down instead of hitting an invisible wall at full speed.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class PlayerBoundary : MonoBehaviour
    {
        [Tooltip("Metres before the edge where outward movement starts to slow down.")]
        [SerializeField] private float softZone = 3f;
        [Tooltip("Distance kept from the exact edge of the island area.")]
        [SerializeField] private float margin = 0.5f;

        private CharacterController controller;
        private Vector3 lastPosition;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            lastPosition = transform.position;
        }

        private void OnEnable() => lastPosition = transform.position;

        private void Update()
        {
            IslandController island = IslandController.PlayerIsland;
            Vector3 pos = transform.position;
            if (island == null || !island.ConfinesPlayer || RaftTravel.PlayerAboard)
            {
                lastPosition = pos;
                return;
            }

            Bounds a = island.Area;
            float minX = a.min.x + margin, maxX = a.max.x - margin;
            float minZ = a.min.z + margin, maxZ = a.max.z - margin;

            float x = SoftAxis(lastPosition.x, pos.x, minX, maxX, out bool hitX);
            float z = SoftAxis(lastPosition.z, pos.z, minZ, maxZ, out bool hitZ);

            if (x != pos.x || z != pos.z)
            {
                Vector3 target = new Vector3(x, pos.y, z);
                if (controller != null && controller.enabled)
                    controller.Move(target - pos); // keeps collisions; nothing is ever pushed into a rock
                else
                    transform.position = target;
            }
            if (hitX || hitZ)
                island.NotifyBoundaryHit();
            lastPosition = transform.position;
        }

        /// <summary>
        /// One horizontal axis: inside the soft zone the outward step is scaled down towards zero,
        /// and the position is never allowed past the hard edge. Inward movement is untouched.
        /// </summary>
        private float SoftAxis(float from, float to, float min, float max, out bool hit)
        {
            hit = false;
            float step = to - from;
            if (step > 0f && to > max - softZone)
            {
                float room = Mathf.Clamp01((max - from) / Mathf.Max(0.01f, softZone));
                to = from + step * room;
                hit = room < 0.35f;
            }
            else if (step < 0f && to < min + softZone)
            {
                float room = Mathf.Clamp01((from - min) / Mathf.Max(0.01f, softZone));
                to = from + step * room;
                hit = room < 0.35f;
            }
            if (to > max) { to = max; hit = true; }
            if (to < min) { to = min; hit = true; }
            return to;
        }
    }
}
