using UnityEngine;

namespace MistIsles.Rules
{
    /// <summary>
    /// Measures how fast the player actually moves (from position change, so it works with
    /// any controller) and how long they have been still. Rules read this every frame.
    /// </summary>
    public class PlayerMotionTracker : MonoBehaviour
    {
        public static PlayerMotionTracker Instance { get; private set; }

        [Tooltip("Speed (m/s) above which the player counts as moving.")]
        [SerializeField, Min(0f)] private float movingThreshold = 0.3f;
        [Tooltip("Higher = reacts faster, lower = smoother.")]
        [SerializeField, Min(0.1f)] private float smoothing = 14f;

        private Vector3 lastPosition;

        public float Speed { get; private set; }
        public bool IsMoving { get; private set; }
        public float StillTime { get; private set; }

        private void Awake()
        {
            Instance = this;
            lastPosition = transform.position;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;
            Vector3 p = transform.position;
            float raw = (p - lastPosition).magnitude / dt;
            lastPosition = p;
            Speed = Mathf.Lerp(Speed, raw, 1f - Mathf.Exp(-smoothing * dt));
            IsMoving = Speed > movingThreshold;
            StillTime = IsMoving ? 0f : StillTime + dt;
        }

        /// <summary>Call after teleporting so the jump does not count as movement.</summary>
        public void Teleported()
        {
            lastPosition = transform.position;
            Speed = 0f;
            IsMoving = false;
            StillTime = 0f;
        }
    }
}
