using MistIsles.Core;
using StarterAssets;
using UnityEngine;

namespace MistIsles.Player
{
    /// <summary>
    /// While any menu is open (quiz, pause, shop), clears the Starter Assets inputs so the
    /// character and camera don't move behind the UI.
    /// </summary>
    [RequireComponent(typeof(StarterAssetsInputs))]
    public class PlayerInputBlocker : MonoBehaviour
    {
        private StarterAssetsInputs inputs;

        private void Awake()
        {
            inputs = GetComponent<StarterAssetsInputs>();
        }

        private void OnEnable() => GamePause.Changed += OnPauseChanged;
        private void OnDisable() => GamePause.Changed -= OnPauseChanged;

        private void OnPauseChanged(bool paused)
        {
            inputs.cursorInputForLook = !paused;
            inputs.cursorLocked = !paused;
            inputs.MoveInput(Vector2.zero);
            inputs.LookInput(Vector2.zero);
            inputs.JumpInput(false);
            inputs.SprintInput(false);
        }

        private void LateUpdate()
        {
            if (!GamePause.IsPaused)
                return;
            inputs.MoveInput(Vector2.zero);
            inputs.LookInput(Vector2.zero);
        }
    }
}
