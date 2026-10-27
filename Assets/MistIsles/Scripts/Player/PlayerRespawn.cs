using System.Collections;
using MistIsles.Core;
using MistIsles.Rules;
using MistIsles.World;
using UnityEngine;

namespace MistIsles.Player
{
    /// <summary>
    /// On death (or falling below killHeight): fade out, move to the current island's
    /// respawn point, refill hearts, fade in. Also provides a safe Teleport used by the raft.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class PlayerRespawn : MonoBehaviour
    {
        [SerializeField] private float delayBeforeFade = 1.0f;
        [SerializeField] private float fadeSeconds = 0.6f;
        [SerializeField] private float killHeight = -15f;

        private Health health;
        private CharacterController controller;
        private Vector3 fallbackSpawn;
        private Quaternion fallbackRotation;
        private bool respawning;

        private void Awake()
        {
            health = GetComponent<Health>();
            controller = GetComponent<CharacterController>();
            fallbackSpawn = transform.position;
            fallbackRotation = transform.rotation;
            health.Died += () => StartCoroutine(Respawn());
            health.Damaged += _ => AudioManager.Play(Sfx.PlayerHurt);
        }

        private void Update()
        {
            if (!respawning && transform.position.y < killHeight)
                StartCoroutine(Respawn());
        }

        private IEnumerator Respawn()
        {
            if (respawning)
                yield break;
            respawning = true;
            GameStats.Deaths++;
            yield return new WaitForSeconds(delayBeforeFade);
            yield return ScreenFader.FadeOut(fadeSeconds);

            IslandController island = IslandController.PlayerIsland;
            if (island != null && island.RespawnPoint != null && island.Area.Contains(transform.position))
                Teleport(island.RespawnPoint.position, island.RespawnPoint.rotation);
            else
                Teleport(fallbackSpawn, fallbackRotation);
            health.ResetHealth();

            yield return ScreenFader.FadeIn(fadeSeconds);
            respawning = false;
        }

        /// <summary>Where to respawn when the player is not on an island with its own respawn point.</summary>
        public void SetFallback(Vector3 position, Quaternion rotation)
        {
            fallbackSpawn = position;
            fallbackRotation = rotation;
        }

        public void Teleport(Vector3 position, Quaternion rotation)
        {
            if (controller != null)
                controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            if (controller != null)
                controller.enabled = true;
            PlayerMotionTracker tracker = GetComponent<PlayerMotionTracker>();
            if (tracker != null)
                tracker.Teleported();
        }
    }
}
