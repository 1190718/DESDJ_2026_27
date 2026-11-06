using MistIsles.Core;
using UnityEngine;

namespace MistIsles.World
{
    /// <summary>
    /// A torn piece of the cartographer's map. Floats and spins with a light beam so it can be
    /// spotted from a distance; walking into it collects it for the island the player is on.
    /// </summary>
    public class MapFragment : MonoBehaviour
    {
        [SerializeField] private float collectRadius = 1.4f;
        [SerializeField] private Transform spinner;

        private Vector3 basePosition;
        private bool collected;

        /// <summary>True for fragments dropped by a camp (destroyed on reset); false for ones placed in the scene.</summary>
        public bool DroppedByCamp { get; set; }

        private void Awake()
        {
            basePosition = transform.position;
            if (spinner == null && transform.childCount > 0)
                spinner = transform.GetChild(0);
        }

        /// <summary>Makes a collected scene fragment available again (island reset).</summary>
        public void ResetFragment()
        {
            collected = false;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (collected)
                return;
            float t = Time.time;
            if (spinner != null)
            {
                spinner.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
                spinner.position = basePosition + Vector3.up * (0.25f + Mathf.Sin(t * 2.2f) * 0.12f);
            }

            Transform player = PlayerLocator.Player;
            if (player == null)
                return;
            Vector3 d = player.position + Vector3.up * 0.8f - (basePosition + Vector3.up * 0.25f);
            if (d.magnitude <= collectRadius)
                Collect();
        }

        private void Collect()
        {
            collected = true;
            IslandController island = IslandController.PlayerIsland;
            if (island == null)
                island = GetComponentInParent<IslandController>();
            AudioManager.Play(Sfx.QuizCorrect);
            if (island != null)
                island.AddFragment();
            // scene fragments are only hidden, so the island can bring them back after a death
            if (DroppedByCamp)
                Destroy(gameObject);
            else
                gameObject.SetActive(false);
        }
    }
}
