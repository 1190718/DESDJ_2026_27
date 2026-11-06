using System;
using System.Collections;
using System.Collections.Generic;
using MistIsles.Core;
using MistIsles.Economy;
using MistIsles.World;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MistIsles.UI
{
    /// <summary>
    /// In-game HUD, built in code on Awake:
    /// health bar, shells and map fragments (top left), objective (top right), island banner
    /// and boss bar (top centre), toasts (lower centre), interaction prompt (bottom), a red
    /// flash when hurt, floating damage numbers, small health bars over wounded enemies, and
    /// the end-of-prototype card. Other systems use the static methods (Toast, Banner, ...).
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        public static GameHUD Instance { get; private set; }

        public static readonly Color Good = new Color(0.49f, 0.88f, 0.63f);
        public static readonly Color Danger = new Color(1f, 0.45f, 0.38f);
        public static readonly Color Neutral = new Color(0.95f, 0.93f, 0.88f);
        public static readonly Color DealtColor = new Color(1f, 0.84f, 0.35f);

        [SerializeField] private bool showControlsOnStart = true;

        private Image hpFill, hpChip;
        private TextMeshProUGUI hpText;
        private float hpShown = 1f, hpChipShown = 1f, hpChipHold;
        private TextMeshProUGUI shellText;
        private GameObject fragmentRow;
        private TextMeshProUGUI fragmentText;
        private RectTransform worldLayer;
        private readonly List<FloatingNumber> numbers = new List<FloatingNumber>();
        private readonly List<EnemyBar> enemyBars = new List<EnemyBar>();
        private TextMeshProUGUI objectiveText;
        private CanvasGroup bannerGroup;
        private TextMeshProUGUI bannerTitle, bannerSub;
        private CanvasGroup toastGroup;
        private TextMeshProUGUI toastText;
        private CanvasGroup promptGroup;
        private TextMeshProUGUI promptText;
        private Image damageFlash;
        private CanvasGroup bossGroup;
        private TextMeshProUGUI bossName, bossState;
        private Image bossFill;
        private GameObject endCard;
        private TextMeshProUGUI endTitle, endBody, endStats;

        private readonly Queue<(string text, Color color, float seconds)> toasts = new Queue<(string, Color, float)>();
        private Coroutine toastRoutine, bannerRoutine;
        private Health playerHealth;
        private Health bossHealth;
        private Func<bool> bossVulnerable;
        private float flash;

        private class FloatingNumber
        {
            public TextMeshProUGUI text;
            public Vector3 world;
            public float age, life;
            public bool active;
        }

        private class EnemyBar
        {
            public Health health;
            public float height;
            public float lastHit = float.NegativeInfinity;
            public RectTransform root;
            public Image fill;
        }

        // ------------------------------------------------------------------ static API

        public static void Toast(string text, Color color, float seconds = 2.5f)
        {
            if (Instance != null && !string.IsNullOrEmpty(text))
                Instance.EnqueueToast(text, color, seconds);
        }

        public static void Banner(string title, string subtitle)
        {
            if (Instance != null)
                Instance.ShowBanner(title, subtitle);
        }

        public static void SetPrompt(string text)
        {
            if (Instance == null)
                return;
            Instance.promptGroup.alpha = string.IsNullOrEmpty(text) ? 0f : 1f;
            Instance.promptText.text = text ?? "";
        }

        public static void ShowBoss(string name, Health health, Func<bool> vulnerable = null)
        {
            if (Instance == null)
                return;
            Instance.bossHealth = health;
            Instance.bossVulnerable = vulnerable;
            Instance.bossName.text = name;
            Instance.bossGroup.alpha = 1f;
        }

        public static void HideBoss()
        {
            if (Instance == null)
                return;
            Instance.bossHealth = null;
            Instance.bossGroup.alpha = 0f;
        }

        /// <summary>Map fragments collected on the current island. need = 0 hides the counter.</summary>
        public static void SetFragments(int have, int need)
        {
            if (Instance == null)
                return;
            Instance.fragmentRow.SetActive(need > 0);
            Instance.fragmentText.text = $"Map fragments  {have} / {need}";
        }

        /// <summary>Floating number at a world position, e.g. damage dealt or taken.</summary>
        public static void DamageNumber(Vector3 world, int amount, Color color)
        {
            if (Instance == null || amount <= 0)
                return;
            Instance.SpawnNumber(world, amount.ToString(), color);
        }

        /// <summary>Shows a small health bar over this enemy for a few seconds after each hit.</summary>
        public static void TrackEnemy(Health health, float height)
        {
            if (Instance == null || health == null)
                return;
            Instance.AddEnemyBar(health, height);
        }

        public static void ShowEndCard(string title, string body, string stats)
        {
            if (Instance == null)
                return;
            Instance.endTitle.text = title;
            Instance.endBody.text = body;
            Instance.endStats.text = stats;
            Instance.endCard.SetActive(true);
            GamePause.Push(GamePause.EndCard);
        }

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Instance = this;
            UIFactory.EnsureEventSystem();
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            IslandController.PlayerEnteredIsland -= OnIslandEntered;
            if (Wallet.Instance != null)
                Wallet.Instance.Changed -= OnShellsChanged;
        }

        private void Start()
        {
            playerHealth = PlayerLocator.Health;
            if (playerHealth != null)
            {
                playerHealth.Damaged += info =>
                {
                    flash = 0.45f;
                    hpChipHold = 0.5f;
                    SpawnNumber(PlayerLocator.AimPoint + Vector3.up * 0.9f, "-" + info.Amount, Danger);
                };
                hpShown = hpChipShown = (float)playerHealth.Current / playerHealth.Max;
            }
            if (Wallet.Instance != null)
            {
                Wallet.Instance.Changed += OnShellsChanged;
                OnShellsChanged(Wallet.Instance.Amount);
            }
            IslandController.PlayerEnteredIsland += OnIslandEntered;
            if (showControlsOnStart)
                Toast("WASD move  ·  Mouse look  ·  Left click attack  ·  E interact  ·  B shop  ·  Esc pause", Neutral, 6f);
        }

        private void Update()
        {
            IslandController island = IslandController.PlayerIsland;
            objectiveText.text = island != null ? island.Objective : "Explore the island";

            flash = Mathf.Max(0f, flash - Time.unscaledDeltaTime);
            damageFlash.color = new Color(0.8f, 0.05f, 0.05f, flash * 0.6f);

            UpdateHealthBar();
            UpdateNumbers();
            UpdateEnemyBars();

            if (bossHealth != null)
            {
                bossFill.fillAmount = Mathf.MoveTowards(bossFill.fillAmount, (float)bossHealth.Current / bossHealth.Max, Time.deltaTime);
                bool open = bossVulnerable != null && bossVulnerable();
                bossState.text = open ? "CORE EXPOSED — STRIKE!" : "Shell sealed";
                bossState.color = open ? Good : UIFactory.InkQuiet;
            }
        }

        private void OnIslandEntered(IslandController island) => ShowBanner(island.IslandName, island.State == IslandController.IslandState.Cleared ? "Cleared" : "Something here is not as it seems");

        private void OnShellsChanged(int amount) => shellText.text = amount.ToString();

        // ------------------------------------------------------------------ health bar

        /// <summary>
        /// The red fill follows health quickly; a pale "chip" behind it holds for half a second
        /// and then drains, so the player can read how big the last hit was.
        /// </summary>
        private void UpdateHealthBar()
        {
            if (playerHealth == null)
            {
                playerHealth = PlayerLocator.Health;
                return;
            }
            float target = (float)playerHealth.Current / playerHealth.Max;
            float dt = Time.unscaledDeltaTime;
            hpShown = Mathf.MoveTowards(hpShown, target, dt * 2.5f);
            if (target > hpChipShown)
                hpChipShown = hpShown; // healing: chip follows the fill
            else if ((hpChipHold -= dt) <= 0f)
                hpChipShown = Mathf.MoveTowards(hpChipShown, target, dt * 0.8f);
            hpFill.fillAmount = hpShown;
            hpChip.fillAmount = hpChipShown;
            hpFill.color = target > 0.3f ? UIFactory.Heart : Color.Lerp(UIFactory.Heart, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f));
            hpText.text = $"{playerHealth.Current} / {playerHealth.Max}";
        }

        // ------------------------------------------------------------------ floating numbers

        private void SpawnNumber(Vector3 world, string text, Color color)
        {
            FloatingNumber n = numbers.Find(x => !x.active);
            if (n == null)
            {
                n = new FloatingNumber();
                n.text = UIFactory.Text("Number", worldLayer, "", 34, TextAlignmentOptions.Center, color, FontStyles.Bold);
                RectTransform rt = n.text.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(200f, 60f);
                n.text.outlineWidth = 0.25f;
                n.text.outlineColor = new Color32(10, 20, 25, 255);
                numbers.Add(n);
            }
            n.text.text = text;
            n.text.color = color;
            n.world = world + new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0f, UnityEngine.Random.Range(-0.3f, 0.3f));
            n.age = 0f;
            n.life = 0.9f;
            n.active = true;
            n.text.gameObject.SetActive(true);
        }

        private void UpdateNumbers()
        {
            foreach (FloatingNumber n in numbers)
            {
                if (!n.active)
                    continue;
                n.age += Time.unscaledDeltaTime;
                if (n.age >= n.life)
                {
                    n.active = false;
                    n.text.gameObject.SetActive(false);
                    continue;
                }
                float k = n.age / n.life;
                if (!Place(n.text.rectTransform, n.world + Vector3.up * (k * 1.2f)))
                    continue;
                Color c = n.text.color;
                c.a = 1f - k * k;
                n.text.color = c;
                n.text.rectTransform.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(1.6f, 1f, k / 0.15f) : 1f);
            }
        }

        // ------------------------------------------------------------------ enemy bars

        private void AddEnemyBar(Health health, float height)
        {
            var bar = new EnemyBar { health = health, height = height };
            bar.root = UIFactory.Rect("EnemyBar", worldLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 14f));
            UIFactory.AddPanel(bar.root, new Color(0f, 0f, 0f, 0.6f), false);
            RectTransform f = UIFactory.Stretch("Fill", bar.root);
            f.offsetMin = new Vector2(2f, 2f);
            f.offsetMax = new Vector2(-2f, -2f);
            bar.fill = f.gameObject.AddComponent<Image>();
            bar.fill.sprite = UIFactory.WhiteSprite;
            bar.fill.color = UIFactory.Heart;
            bar.fill.type = Image.Type.Filled;
            bar.fill.fillMethod = Image.FillMethod.Horizontal;
            bar.root.gameObject.SetActive(false);
            health.Damaged += info =>
            {
                bar.lastHit = Time.time;
                // damage you deal: gold, over the enemy's head (damage you take is red with a minus sign)
                SpawnNumber(health.transform.position + Vector3.up * (height + 0.35f), info.Amount.ToString(), DealtColor);
            };
            enemyBars.Add(bar);
        }

        private void UpdateEnemyBars()
        {
            for (int i = enemyBars.Count - 1; i >= 0; i--)
            {
                EnemyBar b = enemyBars[i];
                if (b.health == null)
                {
                    if (b.root != null)
                        Destroy(b.root.gameObject);
                    enemyBars.RemoveAt(i);
                    continue;
                }
                bool show = !b.health.IsDead && Time.time - b.lastHit < 4f
                            && Place(b.root, b.health.transform.position + Vector3.up * b.height);
                b.root.gameObject.SetActive(show);
                if (show)
                    b.fill.fillAmount = (float)b.health.Current / b.health.Max;
            }
        }

        /// <summary>Moves a HUD element over a world position. Returns false if it is behind the camera.</summary>
        private bool Place(RectTransform rt, Vector3 world)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return false;
            Vector3 screen = cam.WorldToScreenPoint(world);
            if (screen.z <= 0f)
            {
                rt.gameObject.SetActive(false);
                return false;
            }
            rt.gameObject.SetActive(true);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(worldLayer, screen, null, out Vector2 local);
            rt.anchoredPosition = local;
            return true;
        }

        // ------------------------------------------------------------------ toasts and banner

        private void EnqueueToast(string text, Color color, float seconds)
        {
            toasts.Enqueue((text, color, seconds));
            if (toastRoutine == null)
                toastRoutine = StartCoroutine(RunToasts());
        }

        private IEnumerator RunToasts()
        {
            while (toasts.Count > 0)
            {
                var (text, color, seconds) = toasts.Dequeue();
                toastText.text = text;
                toastText.color = color;
                yield return FadeGroup(toastGroup, 1f, 0.2f);
                // Shorten the wait if more messages are queued.
                float hold = toasts.Count > 1 ? Mathf.Min(seconds, 1.5f) : seconds;
                yield return new WaitForSecondsRealtime(hold);
                yield return FadeGroup(toastGroup, 0f, 0.25f);
            }
            toastRoutine = null;
        }

        private void ShowBanner(string title, string subtitle)
        {
            bannerTitle.text = title;
            bannerSub.text = subtitle;
            if (bannerRoutine != null)
                StopCoroutine(bannerRoutine);
            bannerRoutine = StartCoroutine(BannerRoutine());
        }

        private IEnumerator BannerRoutine()
        {
            yield return FadeGroup(bannerGroup, 1f, 0.6f);
            yield return new WaitForSecondsRealtime(2.8f);
            yield return FadeGroup(bannerGroup, 0f, 0.8f);
            bannerRoutine = null;
        }

        private static IEnumerator FadeGroup(CanvasGroup g, float to, float seconds)
        {
            float from = g.alpha;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                g.alpha = Mathf.Lerp(from, to, t / seconds);
                yield return null;
            }
            g.alpha = to;
        }

        // ------------------------------------------------------------------ layout

        private void Build()
        {
            Canvas canvas = UIFactory.CreateCanvas("HUD", 10, transform);
            Transform root = canvas.transform;

            // damage flash (behind everything)
            RectTransform flashRt = UIFactory.Stretch("DamageFlash", root);
            damageFlash = flashRt.gameObject.AddComponent<Image>();
            damageFlash.color = Color.clear;
            damageFlash.raycastTarget = false;

            // world-anchored elements (damage numbers, enemy bars) live under the rest of the HUD
            worldLayer = UIFactory.Stretch("WorldLayer", root);

            // health bar + shells + fragments, top left
            RectTransform status = UIFactory.Rect("Status", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(440f, 150f));
            UIFactory.AddPanel(status, UIFactory.Panel);
            UIFactory.Icon("HeartIcon", status, UIFactory.HeartSprite, UIFactory.Heart, new Vector2(0f, 1f), new Vector2(36f, -38f), 36f);
            RectTransform hpBg = UIFactory.Rect("HealthBar", status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(64f, -20f), new Vector2(356f, 36f));
            UIFactory.AddPanel(hpBg, new Color(0f, 0f, 0f, 0.55f));
            hpChip = BarFill(hpBg, "Chip", new Color(1f, 0.9f, 0.75f, 0.85f));
            hpFill = BarFill(hpBg, "Fill", UIFactory.Heart);
            hpText = UIFactory.Text("Value", hpBg, "", 22, TextAlignmentOptions.Center, UIFactory.Ink, FontStyles.Bold);
            hpText.outlineWidth = 0.2f;
            hpText.outlineColor = new Color32(10, 20, 25, 255);

            UIFactory.Icon("ShellIcon", status, UIFactory.CircleSprite, UIFactory.Shell, new Vector2(0f, 1f), new Vector2(36f, -86f), 28f);
            RectTransform shellRt = UIFactory.Rect("Shells", status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(64f, -66f), new Vector2(200f, 40f));
            shellText = UIFactory.Text("Amount", shellRt, "0", 30, TextAlignmentOptions.Left, UIFactory.Shell, FontStyles.Bold);

            RectTransform frag = UIFactory.Rect("Fragments", status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -108f), new Vector2(400f, 34f));
            fragmentText = UIFactory.Text("Text", frag, "", 22, TextAlignmentOptions.Left, UIFactory.Parchment, FontStyles.Bold);
            fragmentRow = frag.gameObject;
            fragmentRow.SetActive(false);

            // objective, top right
            RectTransform obj = UIFactory.Rect("Objective", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -28f), new Vector2(560f, 110f));
            UIFactory.AddPanel(obj, UIFactory.Panel);
            TextMeshProUGUI objLabel = UIFactory.Text("Label", obj, "OBJECTIVE", 18, TextAlignmentOptions.TopLeft, UIFactory.Accent, FontStyles.Bold);
            objLabel.margin = new Vector4(20, 14, 20, 0);
            objectiveText = UIFactory.Text("Text", obj, "", 24, TextAlignmentOptions.TopLeft, UIFactory.Ink);
            objectiveText.margin = new Vector4(20, 40, 20, 10);

            // banner, top centre
            RectTransform banner = UIFactory.Rect("Banner", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(900f, 130f));
            bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerTitle = UIFactory.Text("Title", banner, "", 60, TextAlignmentOptions.Top, UIFactory.Ink, FontStyles.Bold | FontStyles.SmallCaps);
            bannerSub = UIFactory.Text("Subtitle", banner, "", 26, TextAlignmentOptions.Bottom, UIFactory.InkQuiet, FontStyles.Italic);

            // boss bar
            RectTransform boss = UIFactory.Rect("Boss", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(760f, 84f));
            bossGroup = boss.gameObject.AddComponent<CanvasGroup>();
            bossGroup.alpha = 0f;
            bossName = UIFactory.Text("Name", boss, "", 28, TextAlignmentOptions.TopLeft, UIFactory.Ink, FontStyles.Bold);
            bossState = UIFactory.Text("State", boss, "", 22, TextAlignmentOptions.TopRight, UIFactory.InkQuiet, FontStyles.Bold);
            RectTransform barBg = UIFactory.Rect("BarBg", boss, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(760f, 26f));
            UIFactory.AddPanel(barBg, UIFactory.Panel);
            RectTransform barFill = UIFactory.Stretch("Fill", barBg);
            barFill.offsetMin = new Vector2(4f, 4f);
            barFill.offsetMax = new Vector2(-4f, -4f);
            bossFill = barFill.gameObject.AddComponent<Image>();
            bossFill.sprite = UIFactory.WhiteSprite;
            bossFill.color = UIFactory.Heart;
            bossFill.type = Image.Type.Filled;
            bossFill.fillMethod = Image.FillMethod.Horizontal;
            bossFill.fillAmount = 1f;

            // toast, lower centre
            RectTransform toast = UIFactory.Rect("Toast", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(1100f, 90f));
            UIFactory.AddPanel(toast, UIFactory.Panel);
            toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;
            toastGroup.blocksRaycasts = false;
            toastText = UIFactory.Text("Text", toast, "", 28, TextAlignmentOptions.Center, UIFactory.Ink);
            toastText.margin = new Vector4(24, 8, 24, 8);

            // prompt, bottom centre
            RectTransform prompt = UIFactory.Rect("Prompt", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f), new Vector2(620f, 60f));
            UIFactory.AddPanel(prompt, new Color(UIFactory.Accent.r, UIFactory.Accent.g, UIFactory.Accent.b, 0.9f));
            promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            promptGroup.alpha = 0f;
            promptGroup.blocksRaycasts = false;
            promptText = UIFactory.Text("Text", prompt, "", 28, TextAlignmentOptions.Center, new Color(0.05f, 0.1f, 0.12f), FontStyles.Bold);

            BuildEndCard(root);
        }

        private static Image BarFill(RectTransform parent, string name, Color color)
        {
            RectTransform rt = UIFactory.Stretch(name, parent);
            rt.offsetMin = new Vector2(4f, 4f);
            rt.offsetMax = new Vector2(-4f, -4f);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = UIFactory.WhiteSprite;
            img.color = color;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillAmount = 1f;
            img.raycastTarget = false;
            return img;
        }

        private void BuildEndCard(Transform root)
        {
            RectTransform dim = UIFactory.Stretch("EndCard", root);
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            endCard = dim.gameObject;

            RectTransform card = UIFactory.Rect("Card", dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 520f));
            UIFactory.AddPanel(card, UIFactory.PanelLight);
            endTitle = UIFactory.Text("Title", card, "", 52, TextAlignmentOptions.Top, UIFactory.Accent, FontStyles.Bold);
            endTitle.margin = new Vector4(40, 40, 40, 0);
            endBody = UIFactory.Text("Body", card, "", 28, TextAlignmentOptions.Top, UIFactory.Ink);
            endBody.margin = new Vector4(60, 130, 60, 0);
            endStats = UIFactory.Text("Stats", card, "", 22, TextAlignmentOptions.Top, UIFactory.InkQuiet);
            endStats.margin = new Vector4(60, 280, 60, 0);

            Button keep = UIFactory.Button("Keep", card, "Keep exploring", () =>
            {
                endCard.SetActive(false);
                GamePause.Pop(GamePause.EndCard);
            }, new Vector2(300f, 64f), UIFactory.Accent);
            ((RectTransform)keep.transform).anchoredPosition = new Vector2(-170f, -190f);
            keep.GetComponentInChildren<TextMeshProUGUI>().color = new Color(0.05f, 0.1f, 0.12f);

            Button quit = UIFactory.Button("Quit", card, "Quit game", PauseMenu.QuitGame, new Vector2(300f, 64f));
            ((RectTransform)quit.transform).anchoredPosition = new Vector2(170f, -190f);

            endCard.SetActive(false);
        }
    }
}
