using System.Collections;
using MistIsles.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MistIsles.World
{
    /// <summary>
    /// The visible sign that an island is still "unsolved": a local post-processing volume
    /// that drains colour, plus fog while the player is on the island. Lift() fades both
    /// out when the guardian falls, so the player sees the island regain its colour.
    /// The volume needs a trigger collider covering the island (the setup tool adds one).
    /// </summary>
    [RequireComponent(typeof(Volume))]
    public class IslandMist : MonoBehaviour
    {
        [SerializeField, Range(-100f, 0f)] private float saturation = -70f;
        [SerializeField] private float exposure = -0.35f;
        [SerializeField] private Color tint = new Color(0.82f, 0.88f, 0.94f);
        [SerializeField] private float blendDistance = 10f;
        [SerializeField] private float fogDensity = 0.03f;
        [SerializeField] private Color fogColor = new Color(0.72f, 0.78f, 0.82f);
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
            ca.saturation.Override(saturation);
            ca.postExposure.Override(exposure);
            ca.colorFilter.Override(tint);
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
            RenderSettings.fogDensity = Mathf.Lerp(baseDensity, fogDensity, fogWeight);
            RenderSettings.fogColor = Color.Lerp(originalColor, fogColor, fogWeight);
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
