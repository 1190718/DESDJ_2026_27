using System.Collections;
using MistIsles.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MistIsles.World
{
    /// <summary>
    /// The visible sign that an island is still "unsolved": a sea mist while the player is on it.
    /// Real coastal mist is bright, not grey: light scatters in it, so it lifts the shadows,
    /// lowers contrast and takes the colour of the sky. Here that is a local post-processing
    /// volume (a little brighter, softer contrast, colours only slightly muted, a faint cool
    /// tint) plus exponential-squared fog in a pale sky blue, so nearby things stay clear and
    /// the far shore fades into white-blue. Lift() fades both out when the guardian falls.
    /// The volume needs a trigger collider covering the island (the setup tool adds one).
    ///
    /// The fields were renamed from the first (too grey) version on purpose: scenes saved with
    /// the old values pick up the new look without having to run the setup again.
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public class IslandMist : MonoBehaviour
    {
        [Header("Colour grading inside the mist")]
        [SerializeField, Range(-100f, 0f)] private float mistSaturation = -22f;
        [Tooltip("Mist scatters light: slightly brighter, not darker.")]
        [SerializeField] private float mistExposure = 0.12f;
        [SerializeField, Range(-100f, 0f)] private float mistContrast = -18f;
        [SerializeField] private Color mistTint = new Color(0.96f, 0.98f, 1f);
        [SerializeField] private float blendDistance = 12f;

        [Header("Fog")]
        [Tooltip("Exponential squared: clear up close, thick far away (about 1/density metres of visibility).")]
        [SerializeField] private float mistFogDensity = 0.018f;
        [SerializeField] private Color mistFogColor = new Color(0.80f, 0.87f, 0.93f);
        [SerializeField] private float liftSeconds = 4f;
        [SerializeField] private IslandController island;

        private Volume volume;
        private bool lifted;
        private float fogWeight;
        private bool originalFog;
        private float originalDensity;
        private Color originalColor;
        private FogMode originalMode;

        private void Awake()
        {
            volume = GetComponent<Volume>();
            volume.isGlobal = false;
            volume.blendDistance = blendDistance;
            volume.priority = 10f;
            volume.weight = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            ColorAdjustments ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(mistSaturation);
            ca.postExposure.Override(mistExposure);
            ca.contrast.Override(mistContrast);
            ca.colorFilter.Override(mistTint);
            volume.sharedProfile = profile;

            if (island == null)
                island = GetComponentInParent<IslandController>();

            originalFog = RenderSettings.fog;
            originalDensity = RenderSettings.fogDensity;
            originalColor = RenderSettings.fogColor;
            originalMode = RenderSettings.fogMode;
        }

        private void Update()
        {
            Transform p = PlayerLocator.Player;
            bool playerHere = p != null && island != null && island.Area.Contains(p.position);
            float target = !lifted && playerHere ? 1f : 0f;
            fogWeight = Mathf.MoveTowards(fogWeight, target, Time.deltaTime / 2f);
            ApplyFog();
        }

        private void ApplyFog()
        {
            if (fogWeight <= 0.001f)
            {
                RenderSettings.fog = originalFog;
                RenderSettings.fogDensity = originalDensity;
                RenderSettings.fogColor = originalColor;
                RenderSettings.fogMode = originalMode;
                return;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            float baseDensity = originalFog ? originalDensity : 0f;
            RenderSettings.fogDensity = Mathf.Lerp(baseDensity, mistFogDensity, fogWeight);
            RenderSettings.fogColor = Color.Lerp(originalColor, mistFogColor, fogWeight);
        }

        public void Lift()
        {
            if (lifted)
                return;
            lifted = true;
            StartCoroutine(FadeVolume());
        }

        private IEnumerator FadeVolume()
        {
            for (float t = 0f; t < liftSeconds; t += Time.deltaTime)
            {
                volume.weight = 1f - t / liftSeconds;
                yield return null;
            }
            volume.weight = 0f;
        }

        private void OnDisable()
        {
            RenderSettings.fog = originalFog;
            RenderSettings.fogDensity = originalDensity;
            RenderSettings.fogColor = originalColor;
            RenderSettings.fogMode = originalMode;
        }
    }
}
