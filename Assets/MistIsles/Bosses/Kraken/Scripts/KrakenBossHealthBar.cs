using UnityEngine;
using UnityEngine.UI;

namespace MistIsles.Bosses.Kraken
{
    /// <summary>Optional boss bar: a UI Slider shown when the kraken emerges and hidden when it is defeated.</summary>
    public class KrakenBossHealthBar : MonoBehaviour
    {
        public KrakenBoss boss;
        public Slider slider;
        [Tooltip("Object to show/hide (defaults to this GameObject's first child or the slider).")]
        public GameObject visualRoot;
        public float smoothing = 8f;

        float target = 1f;

        void Awake()
        {
            if (visualRoot == null) visualRoot = slider != null ? slider.gameObject : (transform.childCount > 0 ? transform.GetChild(0).gameObject : null);
            if (visualRoot != null) visualRoot.SetActive(false);
        }

        void OnEnable()
        {
            if (boss == null) return;
            boss.onHealthChanged01.AddListener(OnHealth);
            boss.onAwaken.AddListener(Show);
            boss.onDefeated.AddListener(Hide);
        }

        void OnDisable()
        {
            if (boss == null) return;
            boss.onHealthChanged01.RemoveListener(OnHealth);
            boss.onAwaken.RemoveListener(Show);
            boss.onDefeated.RemoveListener(Hide);
        }

        void OnHealth(float v) { target = v; }
        void Show() { if (visualRoot != null) visualRoot.SetActive(true); }
        void Hide() { if (visualRoot != null) visualRoot.SetActive(false); }

        void Update()
        {
            if (slider != null) slider.value = Mathf.MoveTowards(slider.value, target, smoothing * Time.deltaTime);
        }
    }
}
