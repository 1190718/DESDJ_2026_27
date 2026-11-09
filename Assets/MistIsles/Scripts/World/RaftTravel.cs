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
    /// The raft then waits at the far shore and can take the player back (and forth again).
    /// The old end-of-prototype card is off by default.
    /// </summary>
    public class RaftTravel : MonoBehaviour
    {
        [SerializeField] private Transform destination;
        [SerializeField] private string destinationName = "Whispering Pines";
        [SerializeField] private GameObject visual;
        [SerializeField] private float interactRadius = 4f;
        [SerializeField] private string homeName = "Still Shore";
        [SerializeField] private bool showPrototypeEndCard = false;

        private InputAction interact;
        private bool shown;
        private bool sailing;
        private bool promptVisible;
        private bool atFarShore;
        private Transform homeLanding;
        private Vector3 homeDock;
        private Quaternion homeDockRotation;
        private Vector3 homeVisualPosition;
        private Quaternion homeVisualRotation;

        public bool AtFarShore => atFarShore;
        public string DestinationName => destinationName;

        public void Configure(Transform target, string targetName, GameObject raftVisual, string originName = "Still Shore")
        {
            destination = target;
            destinationName = targetName;
            homeName = originName;
            visual = raftVisual;
            showPrototypeEndCard = false;
        }

        private void Awake()
        {
            interact = new InputAction("MistIslesInteract", InputActionType.Button);
            interact.AddBinding("<Keyboard>/e");
            interact.AddBinding("<Gamepad>/buttonNorth");

            // the way back: the player lands on the dock where the raft waited
            homeDock = transform.position;
            homeDockRotation = transform.rotation;
            homeLanding = new GameObject("MistIsles_RaftHomeLanding").transform;
            homeLanding.SetParent(transform.parent, false);
            homeLanding.SetPositionAndRotation(homeDock + Vector3.up * 0.3f, transform.rotation);
            if (visual != null)
            {
                homeVisualPosition = visual.transform.position;
                homeVisualRotation = visual.transform.rotation;
            }
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
                GameHUD.SetPrompt(near ? $"Press E to sail to {(atFarShore ? homeName : destinationName)}" : null);
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

            Transform to = atFarShore ? homeLanding : destination;
            PlayerRespawn mover = player.GetComponent<PlayerRespawn>();
            if (mover != null)
            {
                mover.Teleport(to.position, to.rotation);
                mover.SetFallback(to.position, to.rotation);
            }
            else
                player.SetPositionAndRotation(to.position, to.rotation);

            // the raft crosses with the player and waits at the shore they arrived on
            atFarShore = !atFarShore;
            MoveRaft(atFarShore);

            yield return new WaitForSeconds(0.4f);
            yield return ScreenFader.FadeIn(0.8f);
            GameHUD.Banner(atFarShore ? destinationName : homeName, atFarShore ? "Uncharted waters. The raft waits on the shore." : "Back home");
            sailing = false;

            if (showPrototypeEndCard && atFarShore)
            {
                yield return new WaitForSeconds(2.5f);
                GameHUD.ShowEndCard(
                    "End of the first prototype",
                    $"You uncovered Still Shore's hidden rule and freed the island.\n{destinationName} and the Drowned Isle are coming in the final version.",
                    $"Time {GameStats.FormatTime(GameStats.PlayTime)}   ·   Creatures defeated {GameStats.CrabsDefeated}   ·   Shells found {GameStats.ShellsCollected}   ·   Falls {GameStats.Deaths}   ·   Wrong answers {GameStats.WrongAnswers}");
            }
        }

        private void MoveRaft(bool farShore)
        {
            if (!farShore)
            {
                transform.SetPositionAndRotation(homeDock, homeDockRotation);
                if (visual != null)
                    visual.transform.SetPositionAndRotation(homeVisualPosition, homeVisualRotation);
                return;
            }
            // dock at the landing, raft on the water a few metres out, pointing back home
            Vector3 back = homeDock - destination.position;
            back.y = 0f;
            Vector3 dir = back.sqrMagnitude > 0.01f ? back.normalized : -destination.forward;
            transform.SetPositionAndRotation(destination.position, Quaternion.LookRotation(dir));
            if (visual != null)
            {
                float waterY = homeVisualPosition.y; // the raft floats at the same sea level on both sides
                Vector3 v = destination.position + dir * 3f;
                visual.transform.SetPositionAndRotation(new Vector3(v.x, waterY, v.z), Quaternion.LookRotation(dir));
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
