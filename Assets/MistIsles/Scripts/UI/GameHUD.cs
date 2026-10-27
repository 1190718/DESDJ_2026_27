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
    /// hearts and shells (top left), objective (top right), island banner and boss bar (top
    /// centre), toasts (lower centre), interaction prompt (bottom), a red flash when hurt, and
    /// the end-of-prototype card. Other systems use the static methods (Toast, Banner, ...).
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        public static GameHUD Instance { get; private set; }

        public static readonly Color Good = new Color(0.49f, 0.88f, 0.63f);
        public static readonly Color Danger = new Color(1f, 0.45f, 0.38f);
        public static readonly Color Neutral = new Color(0.95f, 0.93f, 0.88f);

        [SerializeField] private bool showControlsOnStart = true;

        private readonly List<Image> hearts = new List<Image>();
        private RectTransform heartRow;
        private TextMeshProUGUI shellText;
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
                playerHealth.Changed += (cur, max) => RefreshHearts();
                playerHealth.Damaged += _ => flash = 0.45f;
                RefreshHearts();
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

        private void RefreshHearts()
        {
            if (playerHealth == null)
                return;
            while (hearts.Count < playerHealth.Max)
            {
                Image h = UIFactory.Icon("Heart", heartRow, UIFactory.HeartSprite, UIFactory.Heart, new Vector2(0f, 0.5f), new Vector2(22f + hearts.Count * 50f, 0f), 44f);
                hearts.Add(h);
            }
            for (int i = 0; i < hearts.Count; i++)
            {
                hearts[i].gameObject.SetActive(i < playerHealth.Max);
                hearts[i].color = i < playerHealth.Current ? UIFactory.Heart : UIFactory.HeartEmpty;
            }
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

            // hearts + shells, top left
            RectTransform status = UIFactory.Rect("Status", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -28f), new Vector2(330f, 120f));
            UIFactory.AddPanel(status, UIFactory.Panel);
            heartRow = UIFactory.Rect("Hearts", status, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -12f), new Vector2(300f, 48f));
            UIFactory.Icon("ShellIcon", status, UIFactory.CircleSprite, UIFactory.Shell, new Vector2(0f, 0f), new Vector2(36f, 32f), 30f);
            RectTransform shellRt = UIFactory.Rect("Shells", status, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(62f, 12f), new Vector2(240f, 40f));
            shellText = UIFactory.Text("Amount", shellRt, "0", 30, TextAlignmentOptions.Left, UIFactory.Shell, FontStyles.Bold);

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
