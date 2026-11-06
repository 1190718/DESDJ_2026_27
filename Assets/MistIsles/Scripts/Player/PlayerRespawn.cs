using System.Collections;
using MistIsles.Core;
using MistIsles.Economy;
using MistIsles.Rules;
using MistIsles.UI;
using MistIsles.World;
using UnityEngine;

namespace MistIsles.Player
{
    /// <summary>
    /// On death: fade out, reset the island the player died on (unless it was already cleared),
    /// lose the shells earned during this run, move to the island's respawn point, refill health,
    /// fade in. Falling below killHeight only respawns (no reset, nothing lost).
    /// Also provides a safe Teleport used by the raft.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerRespawn : MonoBehaviour
    {
        [SerializeField] private float delayBeforeFade = 1.0f;
        [SerializeField] private float fadeSeconds = 0.6f;
        [SerializeField] private float killHeight = -15f;
        [Tooltip("The player never starts with less than this (older scenes still carry the 5-heart value).")]
        [SerializeField] private int minimumMaxHealth = 100;

        private Health health;
        private CharacterController controller;
        private Vector3 fallbackSpawn;
        private Quaternion fallbackRotation;
        private bool respawning;

        private void Awake()
        {
            health = GetComponent<Health>();
            if (health.Max < minimumMaxHealth)
                health.SetMax(minimumMaxHealth, refill: true);
            controller = GetComponent<CharacterController>();
            fallbackSpawn = transform.position;
            fallbackRotation = transform.rotation;
            health.Died += () => StartCoroutine(Respawn(died: true));
            health.Damaged += _ => AudioManager.Play(Sfx.PlayerHurt);
        }

        private void Update()
        {
            if (!respawning && transform.position.y < killHeight)
                StartCoroutine(Respawn(died: false));
        }

        private IEnumerator Respawn(bool died)
        {
            if (respawning)
                yield break;
            respawning = true;
            GameStats.Deaths++;
            yield return new WaitForSeconds(delayBeforeFade);
            yield return ScreenFader.FadeOut(fadeSeconds);

            IslandController island = IslandController.PlayerIsland;
            bool islandReset = false;
            int shellsLost = 0;
            if (died)
            {
                if (island != null)
                    islandReset = island.ResetIsland();
                if (Wallet.Instance != null)
                    shellsLost = Wallet.Instance.LoseRunEarnings();
            }

            if (island != null && island.RespawnPoint != null && island.Area.Contains(transform.position))
                Teleport(island.RespawnPoint.position, island.RespawnPoint.rotation);
            else
                Teleport(fallbackSpawn, fallbackRotation);
            health.ResetHealth();

            yield return ScreenFader.FadeIn(fadeSeconds);
            respawning = false;

            if (died)
            {
                if (islandReset)
                    GameHUD.Banner($"The mist reclaims {island.IslandName}", "Everything starts over");
                string lost = shellsLost > 0 ? $"You lost the {shellsLost} shells earned on this run." : "You had no unbanked shells to lose.";
                GameHUD.Toast(lost + " Clearing an island keeps your shells safe.", GameHUD.Danger, 4.5f);
            }
        }

        /// <summary>Where to respawn when the player is not on an island with its own respawn point.</summary>
        public void SetFallback(Vector3 position, Quaternion rotation)
        {
            fallbackSpawn = position;
            fallbackRotation = rotation;
        }

        /// <summary>
        /// Puts a spawn point on top of the ground. A point that ended up inside the island mesh
        /// (e.g. the terrain was raised after the point was placed) would drop the player through
        /// the one-sided collider to the sea floor under the island, out of sight.
        /// Picks the lowest walkable surface at or above the point (so tree tops are skipped);
        /// if there is none, the highest one below it.
        /// </summary>
        private Vector3 SnapToGround(Vector3 p)
        {
            const float above = 30f;
            RaycastHit[] hits = Physics.RaycastAll(p + Vector3.up * above, Vector3.down, above + 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bestAbove = float.PositiveInfinity, bestBelow = float.NegativeInfinity;
            foreach (RaycastHit h in hits)
            {
                if (h.normal.y < 0.6f || h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<Health>() != null)
                    continue;
                float y = h.point.y;
                if (y >= p.y - 0.5f)
                    bestAbove = Mathf.Min(bestAbove, y);
                else
                    bestBelow = Mathf.Max(bestBelow, y);
            }
            if (!float.IsPositiveInfinity(bestAbove))
                return new Vector3(p.x, bestAbove + 0.05f, p.z);
            if (!float.IsNegativeInfinity(bestBelow))
                return new Vector3(p.x, bestBelow + 0.05f, p.z);
            return p;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (controller != null)
                controller.enabled = false;
            transform.SetPositionAndRotation(SnapToGround(position), rotation);
            Physics.SyncTransforms();
            if (controller != null)
                controller.enabled = true;
            PlayerMotionTracker tracker = GetComponent<PlayerMotionTracker>();
            if (tracker != null)
                tracker.Teleported();
        }
    }
}
