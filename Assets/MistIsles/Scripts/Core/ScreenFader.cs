using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MistIsles.Core
{
    /// <summary>Full-screen black overlay used for respawns and raft travel. Creates itself on demand.</summary>
    public class ScreenFader : MonoBehaviour
    {
        private static ScreenFader instance;
        private Image image;

        private static ScreenFader Get()
        {
            if (instance != null)
                return instance;
            var go = new GameObject("ScreenFader");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var imgGo = new GameObject("Black", typeof(RectTransform));
            imgGo.transform.SetParent(go.transform, false);
            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            instance = go.AddComponent<ScreenFader>();
            instance.image = imgGo.AddComponent<Image>();
            instance.image.color = new Color(0f, 0f, 0f, 0f);
            instance.image.raycastTarget = false;
            return instance;
        }

        public static IEnumerator FadeOut(float seconds) => Get().Fade(1f, seconds);
        public static IEnumerator FadeIn(float seconds) => Get().Fade(0f, seconds);

        private IEnumerator Fade(float target, float seconds)
        {
            float start = image.color.a;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                image.color = new Color(0f, 0f, 0f, Mathf.Lerp(start, target, t / seconds));
                yield return null;
            }
            image.color = new Color(0f, 0f, 0f, target);
        }
    }
}
