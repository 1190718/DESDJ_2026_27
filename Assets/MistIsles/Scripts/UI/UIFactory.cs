using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MistIsles.UI
{
    /// <summary>
    /// Builds uGUI elements from code (canvas, panels, text, buttons) with a shared look.
    /// Building UI in code keeps it out of the scene file, so several people can change
    /// the HUD without scene merge conflicts. Sprites (rounded panel, heart, circle) are
    /// generated procedurally, so the UI needs no image assets.
    /// </summary>
    public static class UIFactory
    {
        public static readonly Color Panel = new Color(0.06f, 0.10f, 0.14f, 0.82f);
        public static readonly Color PanelLight = new Color(0.14f, 0.20f, 0.26f, 0.95f);
        public static readonly Color Accent = new Color(0.25f, 0.88f, 0.78f);
        public static readonly Color Ink = new Color(0.95f, 0.93f, 0.88f);
        public static readonly Color InkQuiet = new Color(0.72f, 0.76f, 0.78f);
        public static readonly Color Heart = new Color(0.94f, 0.33f, 0.36f);
        public static readonly Color HeartEmpty = new Color(0.25f, 0.27f, 0.3f, 0.9f);
        public static readonly Color Shell = new Color(0.98f, 0.86f, 0.62f);

        private static Sprite rounded, heart, circle;

        public static Sprite RoundedSprite => rounded != null ? rounded : rounded = MakeRounded();
        public static Sprite HeartSprite => heart != null ? heart : heart = MakeShape(64, Heart01);
        public static Sprite CircleSprite => circle != null ? circle : circle = MakeShape(64, Circle01);

        // ------------------------------------------------------------------ builders

        public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null)
                go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image AddPanel(Transform target, Color color, bool rounded = true)
        {
            var img = target.gameObject.AddComponent<Image>();
            img.color = color;
            if (rounded)
            {
                img.sprite = RoundedSprite;
                img.type = Image.Type.Sliced;
            }
            return img;
        }

        public static Image Icon(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 position, float size)
        {
            RectTransform rt = Rect(name, parent, anchor, new Vector2(0.5f, 0.5f), position, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string text, float size, TextAlignmentOptions align, Color color, FontStyles style = FontStyles.Normal)
        {
            RectTransform rt = Stretch(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(string name, Transform parent, string label, Action onClick, Vector2 size, Color? color = null)
        {
            RectTransform rt = Rect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            Image bg = AddPanel(rt, color ?? PanelLight);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            ColorBlock cb = button.colors;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            button.colors = cb;
            TextMeshProUGUI t = Text("Label", rt, label, 28, TextAlignmentOptions.Center, Ink, FontStyles.Bold);
            t.margin = new Vector4(12, 4, 12, 4);
            if (onClick != null)
                button.onClick.AddListener(() =>
                {
                    Core.AudioManager.Play(Core.Sfx.UiClick);
                    onClick();
                });
            return button;
        }

        /// <summary>Adds a vertical layout so children stack top to bottom.</summary>
        public static VerticalLayoutGroup Stack(Transform target, float spacing, RectOffset padding, TextAnchor align = TextAnchor.UpperCenter)
        {
            var v = target.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = padding;
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = false;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return v;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null)
                return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // ------------------------------------------------------------------ procedural sprites

        private static Sprite MakeRounded()
        {
            const int n = 64;
            const float r = 18f;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, n - r);
                    float cy = Mathf.Clamp(y + 0.5f, r, n - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    byte a = (byte)(255 * Mathf.Clamp01(r - d + 0.5f));
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r + 2, r + 2, r + 2, r + 2));
        }

        private static Sprite MakeShape(int n, Func<float, float, float> inside)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            const int ss = 4; // supersampling for smooth edges
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cover = 0f;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float u = (x + (sx + 0.5f) / ss) / n * 2f - 1f;
                            float v = (y + (sy + 0.5f) / ss) / n * 2f - 1f;
                            cover += inside(u, v);
                        }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * cover / (ss * ss)));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        // (x² + y² − 1)³ − x²y³ ≤ 0 is the classic heart curve.
        private static float Heart01(float u, float v)
        {
            float x = u * 1.25f;
            float y = v * 1.25f + 0.15f;
            float a = x * x + y * y - 1f;
            return a * a * a - x * x * y * y * y <= 0f ? 1f : 0f;
        }

        private static float Circle01(float u, float v) => u * u + v * v <= 0.92f ? 1f : 0f;
    }
}
