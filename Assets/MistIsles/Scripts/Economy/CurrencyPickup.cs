using MistIsles.Core;
using UnityEngine;

namespace MistIsles.Economy
{
    /// <summary>A shell dropped by a crab. Bobs in place, then flies to the player when they get close.</summary>
    public class CurrencyPickup : MonoBehaviour
    {
        [SerializeField, Min(1)] private int value = 1;
        [SerializeField] private float magnetRadius = 3f;
        [SerializeField] private float collectRadius = 0.7f;
        [SerializeField] private float flySpeed = 9f;
        [SerializeField] private float lifetime = 40f;

        private Vector3 basePosition;
        private float spawnTime;
        private bool flying;

        private void Start()
        {
            spawnTime = Time.time;
            // settle on the ground below
            if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit hit, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                transform.position = hit.point + Vector3.up * 0.3f;
            basePosition = transform.position;
        }

        private void Update()
        {
            transform.Rotate(0f, 120f * Time.deltaTime, 0f, Space.World);
            Transform player = PlayerLocator.Player;
            if (player == null)
                return;

            Vector3 target = player.position + Vector3.up * 0.9f;
            float dist = Vector3.Distance(transform.position, target);
            if (!flying && dist < magnetRadius && Time.time - spawnTime > 0.5f)
                flying = true;

            if (flying)
            {
                transform.position = Vector3.MoveTowards(transform.position, target, flySpeed * Time.deltaTime);
                if (Vector3.Distance(transform.position, target) < collectRadius)
                    Collect();
            }
            else
            {
                transform.position = basePosition + Vector3.up * (Mathf.Sin((Time.time - spawnTime) * 3f) * 0.1f);
                if (Time.time - spawnTime > lifetime)
                    Destroy(gameObject);
            }
        }

        private void Collect()
        {
            Wallet.Instance?.Add(value);
            GameStats.ShellsCollected += value;
            AudioManager.Play(Sfx.Pickup);
            Destroy(gameObject);
        }
    }
}
