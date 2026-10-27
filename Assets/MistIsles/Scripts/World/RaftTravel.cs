using System.Collections;
using MistIsles.Core;
using MistIsles.Player;
using MistIsles.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MistIsles.World
{
    /// <summary>
    /// Hidden until the island is cleared. Then it floats up by the shore; pressing E nearby
    /// fades out, moves the player to the next island's landing point and fades back in.
    /// In the prototype the arrival also shows the end-of-slice card.
    /// </summary>
    public class RaftTravel : MonoBehaviour
    {
        [SerializeField] private Transform destination;
        [SerializeField] private string destinationName = "Whispering Pines";
        [SerializeField] private GameObject visual;
        [SerializeField] private float interactRadius = 4f;
        [SerializeField] private bool showPrototypeEndCard = true;

        private InputAction interact;
        private bool shown;
        private bool sailing;
        private bool promptVisible;

        public void Configure(Transform target, string targetName, GameObject raftVisual)
        {
            destination = target;
            destinationName = targetName;
            visual = raftVisual;
        }

        private void Awake()
        {
            interact = new InputAction("MistIslesInteract", InputActionType.Button);
            interact.AddBinding("<Keyboard>/e");
            interact.AddBinding("<Gamepad>/buttonNorth");
            Hide();
        }

        private void OnEnable() => interact.Enable();
        private void OnDisable() => interact.Disable();
        private void OnDestroy() => interact.Dispose();

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
            Vector3 target = visual.transform.position;
            Vector3 start = target + Vector3.down * 1.5f;
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                visual.transform.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / 2.5f));
                yield return null;
            }
            visual.transform.position = target;
        }

        private void Update()
        {
            if (visual != null && shown)
                visual.transform.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.9f) * 2f, 0f, Mathf.Sin(Time.time * 0.7f) * 3f);

            Transform player = PlayerLocator.Player;
            bool near = shown && !sailing && player != null && destination != null
                        && Vector3.Distance(player.position, transform.position) <= interactRadius;
            if (near != promptVisible)
            {
                promptVisible = near;
                GameHUD.SetPrompt(near ? $"Press E to sail to {destinationName}" : null);
            }
            if (near && !GamePause.IsPaused && interact.WasPressedThisFrame())
                StartCoroutine(Sail(player));
        }

        private IEnumerator Sail(Transform player)
        {
            sailing = true;
            promptVisible = false;
            GameHUD.SetPrompt(null);
            AudioManager.Play(Sfx.RaftSplash);
            yield return ScreenFader.FadeOut(0.8f);

            PlayerRespawn mover = player.GetComponent<PlayerRespawn>();
            if (mover != null)
            {
                mover.Teleport(destination.position, destination.rotation);
                mover.SetFallback(destination.position, destination.rotation);
            }
            else
                player.SetPositionAndRotation(destination.position, destination.rotation);

            yield return new WaitForSeconds(0.4f);
            yield return ScreenFader.FadeIn(0.8f);
            GameHUD.Banner(destinationName, "Uncharted waters");
            sailing = false;

            if (showPrototypeEndCard)
            {
                yield return new WaitForSeconds(2.5f);
                GameHUD.ShowEndCard(
                    "End of the first prototype",
                    $"You uncovered Still Shore's hidden rule and freed the island.\n{destinationName} and the Drowned Isle are coming in the final version.",
                    $"Time {GameStats.FormatTime(GameStats.PlayTime)}   ·   Crabs defeated {GameStats.CrabsDefeated}   ·   Shells found {GameStats.ShellsCollected}   ·   Falls {GameStats.Deaths}   ·   Wrong answers {GameStats.WrongAnswers}");
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, interactRadius);
            if (destination != null)
                Gizmos.DrawLine(transform.position, destination.position);
        }
    }
}
