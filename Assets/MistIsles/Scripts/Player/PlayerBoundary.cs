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
    /// The limit follows the island's coastline (IslandController builds a mask of its land plus
    /// a strip of sea). At the edge the player slides along it instead of stopping dead.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class PlayerBoundary : MonoBehaviour
    {
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

            // inside, or we were already outside (spawned/teleported there): never trap the player
            if (island.IsInsideBoundary(pos) || !island.IsInsideBoundary(lastPosition))
            {
                lastPosition = pos;
                return;
            }

            // Outside the coast limit: keep whichever part of the step stays inside (slide along
            // the limit), otherwise go back to the last allowed position.
            Vector3 target;
            if (island.IsInsideBoundary(new Vector3(pos.x, pos.y, lastPosition.z)))
                target = new Vector3(pos.x, pos.y, lastPosition.z);
            else if (island.IsInsideBoundary(new Vector3(lastPosition.x, pos.y, pos.z)))
                target = new Vector3(lastPosition.x, pos.y, pos.z);
            else
                target = new Vector3(lastPosition.x, pos.y, lastPosition.z);

            if (controller != null && controller.enabled)
                controller.Move(target - pos); // keeps collisions; nothing is ever pushed into a rock
            else
                transform.position = target;
            island.NotifyBoundaryHit();
            lastPosition = transform.position;
        }
    }
}
