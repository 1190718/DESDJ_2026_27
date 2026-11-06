using System.Collections;
using System.Collections.Generic;
using MistIsles.Core;
using MistIsles.UI;
using MistIsles.World;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// The Cartographer's Card. Opens when the player touches an active checkpoint.
///
/// Look: the game stays visible behind a misty, slightly blurred overlay (a temporary
/// post-processing volume adds depth-of-field and a vignette), and the question sits on an
/// aged parchment card built in code.
///
/// Two modes:
///  - Rule builder (when the checkpoint has rule parts): the player assembles the hidden rule
///    from three parts (who / does what / when). With 3 x 4 x 4 options, guessing is
///    impractical; after a wrong rule the card says how many parts were right (deduction
///    feedback), the player loses health and, on an island, an ambush seals the card.
///  - Four answers (islands without rule parts): answers are reshuffled after every wrong
///    answer, which also costs health and locks the buttons for a moment.
/// </summary>
public class QuizUI : MonoBehaviour
{
    public static QuizUI Instance { get; private set; }

    /// <summary>Frame on which the card was last closed (so Esc doesn't also open the pause menu).</summary>
    public static int LastClosedFrame { get; private set; } = -1;

    [Header("Legacy scene UI (hidden at start; the card is built in code)")]
    [SerializeField] private GameObject quizPanel;
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private Button[] answerButtons;

    [Header("Look")]
    [SerializeField] private bool blurBackground = true;
    [SerializeField] private string[] slotLabels = { "WHO", "DOES WHAT", "WHEN" };

    private Checkpoint currentCheckpoint;
    private readonly Dictionary<Checkpoint, int> wrongCount = new Dictionary<Checkpoint, int>();

    // built UI
    private GameObject root;
    private CanvasGroup group;
    private RectTransform card;
    private TextMeshProUGUI cardTitle, cardIntro, sentence, feedback;
    private GameObject builderRoot, classicRoot;
    private readonly TextMeshProUGUI[] slotValues = new TextMeshProUGUI[3];
    private readonly List<Button> slotButtons = new List<Button>();
    private readonly int[] choice = new int[3];
    private readonly Button[] classicButtons = new Button[4];
    private int[] classicOrder = new int[0];
    private Button sealButton, backButton;
    private RectTransform stamp;

    private Volume blurVolume;
    private InputAction cancel;
    private Coroutine fadeRoutine, lockRoutine;
    private bool busy;

    // ------------------------------------------------------------------ lifecycle

    private void Awake()
    {
        Instance = this;
        cancel = new InputAction("MistIslesCardBack", InputActionType.Button);
        cancel.AddBinding("<Keyboard>/escape");
        cancel.AddBinding("<Gamepad>/buttonEast");
        UIFactory.EnsureEventSystem();
        Build();
        if (blurBackground)
            BuildBlurVolume();
    }

    private void Start()
    {
        if (quizPanel != null)
            quizPanel.SetActive(false);
        root.SetActive(false);
    }

    private void OnEnable() => cancel.Enable();
    private void OnDisable() => cancel.Disable();
    private void OnDestroy() => cancel.Dispose();

    private void Update()
    {
        if (currentCheckpoint != null && !busy && cancel.WasPressedThisFrame())
            StepBack();
    }

    // ------------------------------------------------------------------ open / close

    public void ShowQuestion(Checkpoint checkpoint)
    {
        // Don't open another question while one is already open
        if (currentCheckpoint != null || checkpoint == null)
            return;
        currentCheckpoint = checkpoint;
        busy = false;

        bool builder = checkpoint.IsRuleBuilder;
        builderRoot.SetActive(builder);
        classicRoot.SetActive(!builder);
        sealButton.gameObject.SetActive(builder);
        stamp.gameObject.SetActive(false);

        cardTitle.text = builder ? "What is the hidden law of this island?" : checkpoint.Question;
        cardIntro.text = builder
            ? (string.IsNullOrEmpty(checkpoint.RuleIntro) ? "Write the rule you have observed. A wrong rule angers the mist." : checkpoint.RuleIntro)
            : "Choose carefully. A wrong answer angers the mist.";
        feedback.text = checkpoint.LastFeedback ?? "";

        if (builder)
        {
            int[] last = checkpoint.LastChoice;
            for (int i = 0; i < 3; i++)
                choice[i] = last != null && last.Length == 3 ? last[i] : 0;
            RefreshSlots();
        }
        else
        {
            ShuffleClassic();
        }

        SetInteractable(true);
        root.SetActive(true);
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Fade(true));

        // Pause the game and free the mouse (shared pause service, so other menus don't fight over it)
        GamePause.Push(GamePause.Quiz);
        AudioManager.Play(Sfx.UiClick);
        Select(builder ? slotButtons[1].gameObject : classicButtons[0].gameObject);
    }

    /// <summary>Leave the card without answering. No penalty.</summary>
    private void StepBack()
    {
        if (currentCheckpoint != null && currentCheckpoint.IsRuleBuilder)
            currentCheckpoint.LastChoice = (int[])choice.Clone();
        CloseQuiz();
    }

    private void CloseQuiz()
    {
        currentCheckpoint = null;
        if (lockRoutine != null)
        {
            StopCoroutine(lockRoutine);
            lockRoutine = null;
        }
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Fade(false));
        LastClosedFrame = Time.frameCount;

        // Resume the game and lock the mouse again
        GamePause.Pop(GamePause.Quiz);
    }

    private IEnumerator Fade(bool show)
    {
        float from = group.alpha, to = show ? 1f : 0f;
        const float seconds = 0.25f;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            group.alpha = Mathf.Lerp(from, to, k);
            float s = show ? Mathf.Lerp(0.94f, 1f, k) : Mathf.Lerp(1f, 0.97f, k);
            card.localScale = new Vector3(s, s, 1f);
            if (blurVolume != null)
                blurVolume.weight = group.alpha;
            yield return null;
        }
        group.alpha = to;
        card.localScale = Vector3.one;
        if (blurVolume != null)
            blurVolume.weight = to;
        if (!show)
            root.SetActive(false);
        fadeRoutine = null;
    }

    // ------------------------------------------------------------------ rule builder

    private string[] Options(int slot)
    {
        switch (slot)
        {
            case 0: return currentCheckpoint.RuleSubjects;
            case 1: return currentCheckpoint.RuleActions;
            default: return currentCheckpoint.RuleConditions;
        }
    }

    private void Cycle(int slot, int step)
    {
        if (currentCheckpoint == null || busy)
            return;
        int n = Options(slot).Length;
        choice[slot] = ((choice[slot] + step) % n + n) % n;
        RefreshSlots();
    }

    private void RefreshSlots()
    {
        for (int i = 0; i < 3; i++)
        {
            string[] o = Options(i);
            choice[i] = Mathf.Clamp(choice[i], 0, o.Length - 1);
            slotValues[i].text = o[choice[i]];
        }
        sentence.text = $"“{slotValues[0].text} {slotValues[1].text} {slotValues[2].text}.”";
    }

    private void SealRule()
    {
        if (currentCheckpoint == null || busy)
            return;
        int parts = currentCheckpoint.CountCorrectParts(choice[0], choice[1], choice[2]);
        currentCheckpoint.LastChoice = (int[])choice.Clone();
        if (parts == 3)
            StartCoroutine(Correct());
        else
            Wrong($"{parts} of 3 parts of your last rule rang true.");
    }

    // ------------------------------------------------------------------ four answers

    private void ShuffleClassic()
    {
        string[] answers = currentCheckpoint.Answers;
        int n = Mathf.Min(answers.Length, classicButtons.Length);
        classicOrder = new int[n];
        for (int i = 0; i < n; i++)
            classicOrder[i] = i;
        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (classicOrder[i], classicOrder[j]) = (classicOrder[j], classicOrder[i]);
        }
        for (int i = 0; i < classicButtons.Length; i++)
        {
            bool used = i < n;
            classicButtons[i].gameObject.SetActive(used);
            if (used)
                classicButtons[i].GetComponentInChildren<TextMeshProUGUI>().text = answers[classicOrder[i]];
        }
    }

    private void PickClassic(int buttonIndex)
    {
        if (currentCheckpoint == null || busy || buttonIndex >= classicOrder.Length)
            return;
        if (currentCheckpoint.IsCorrectAnswer(classicOrder[buttonIndex]))
            StartCoroutine(Correct());
        else
            Wrong("That was not it.");
    }

    // ------------------------------------------------------------------ outcomes

    private IEnumerator Correct()
    {
        busy = true;
        SetInteractable(false);
        Checkpoint completedCheckpoint = currentCheckpoint;
        completedCheckpoint.LastFeedback = null;
        AudioManager.Play(Sfx.QuizCorrect);

        // wax seal stamp
        stamp.gameObject.SetActive(true);
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            float k = t / 0.35f;
            float s = Mathf.Lerp(2.2f, 1f, k * k);
            stamp.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        stamp.localScale = Vector3.one;
        yield return new WaitForSecondsRealtime(0.7f);

        CloseQuiz();
        completedCheckpoint.Complete();
    }

    private void Wrong(string partsFeedback)
    {
        Checkpoint cp = currentCheckpoint;
        GameStats.WrongAnswers++;
        AudioManager.Play(Sfx.QuizWrong);

        int count = wrongCount.TryGetValue(cp, out int c) ? c + 1 : 1;
        wrongCount[cp] = count;
        string hint = count >= 2 && !string.IsNullOrEmpty(cp.Hint) ? "  Hint: " + cp.Hint : "";
        cp.LastFeedback = partsFeedback + hint;

        // The card wounds but never kills.
        Health h = PlayerLocator.Health;
        if (h != null && cp.WrongAnswerDamage > 0 && h.Current > 1)
        {
            int dmg = Mathf.Min(cp.WrongAnswerDamage, h.Current - 1);
            Transform p = PlayerLocator.Player;
            h.TakeDamage(new DamageInfo(dmg, p.position, -p.forward, gameObject));
        }

        IslandController island = cp.GetComponentInParent<IslandController>();
        if (island != null && cp.AmbushOnWrongAnswer)
        {
            CloseQuiz();
            GameHUD.Toast(partsFeedback, GameHUD.Danger, 3f);
            island.StartAmbush(cp);
            return;
        }

        // No island ambush: stay on the card, show feedback, lock and reshuffle.
        feedback.text = cp.LastFeedback;
        if (lockRoutine != null)
            StopCoroutine(lockRoutine);
        lockRoutine = StartCoroutine(LockThenReshuffle(cp.WrongAnswerLockSeconds));
    }

    private IEnumerator LockThenReshuffle(float seconds)
    {
        SetInteractable(false);
        // Realtime, because the game is paused while the card is open.
        yield return new WaitForSecondsRealtime(seconds);
        if (currentCheckpoint != null && !currentCheckpoint.IsRuleBuilder)
            ShuffleClassic();
        SetInteractable(true);
        lockRoutine = null;
    }

    private void SetInteractable(bool value)
    {
        foreach (Button b in slotButtons)
            b.interactable = value;
        foreach (Button b in classicButtons)
            b.interactable = value;
        sealButton.interactable = value;
        backButton.interactable = value;
    }

    private static void Select(GameObject go)
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(go);
    }

    // ------------------------------------------------------------------ layout

    private void Build()
    {
        Canvas canvas = UIFactory.CreateCanvas("CartographerCard", 40);
        root = canvas.gameObject;
        group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // the game stays visible: a misty tint and a vignette, no opaque panel
        RectTransform tint = UIFactory.Stretch("MistTint", root.transform);
        tint.gameObject.AddComponent<Image>().color = UIFactory.MistOverlay;
        RectTransform vig = UIFactory.Stretch("Vignette", root.transform);
        Image vigImg = vig.gameObject.AddComponent<Image>();
        vigImg.sprite = UIFactory.VignetteSprite;
        vigImg.color = new Color(0f, 0.03f, 0.04f, 0.9f);
        vigImg.raycastTarget = false;

        // card + shadow
        Vector2 size = new Vector2(1060f, 780f);
        RectTransform shadow = UIFactory.Rect("Shadow", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14f, -18f), size);
        Image sh = Parchment(shadow, new Color(0f, 0f, 0f, 0.35f));
        sh.raycastTarget = false;
        shadow.localRotation = Quaternion.Euler(0f, 0f, -1.2f);

        card = UIFactory.Rect("Card", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
        Parchment(card, UIFactory.Parchment);
        card.localRotation = Quaternion.Euler(0f, 0f, -1.2f);

        TextMeshProUGUI kicker = UIFactory.Text("Kicker", card, "THE CARTOGRAPHER'S CARD", 22, TextAlignmentOptions.Top, UIFactory.InkFaded, FontStyles.Bold);
        kicker.characterSpacing = 12f;
        kicker.margin = new Vector4(60, 52, 60, 0);
        cardTitle = UIFactory.Text("Title", card, "", 40, TextAlignmentOptions.Top, UIFactory.InkBrown, FontStyles.Bold);
        cardTitle.margin = new Vector4(70, 92, 70, 0);
        InkRule(card, -190f);
        cardIntro = UIFactory.Text("Intro", card, "", 24, TextAlignmentOptions.Top, UIFactory.InkFaded, FontStyles.Italic);
        cardIntro.margin = new Vector4(90, 205, 90, 0);

        BuildBuilder();
        BuildClassic();

        feedback = UIFactory.Text("Feedback", card, "", 23, TextAlignmentOptions.Bottom, UIFactory.WaxRed, FontStyles.Italic | FontStyles.Bold);
        feedback.margin = new Vector4(80, 0, 80, 128);

        backButton = InkButton(card, "Back", "Step back", StepBack, new Vector2(260f, 66f), false);
        Anchor(backButton, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(70f, 52f));
        sealButton = InkButton(card, "Seal", "Seal the rule", SealRule, new Vector2(320f, 66f), true);
        Anchor(sealButton, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-70f, 52f));

        // wax seal shown on a correct rule
        stamp = UIFactory.Rect("Stamp", card, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-140f, -150f), new Vector2(170f, 170f));
        Image wax = stamp.gameObject.AddComponent<Image>();
        wax.sprite = UIFactory.CircleSprite;
        wax.color = UIFactory.WaxRed;
        wax.raycastTarget = false;
        TextMeshProUGUI st = UIFactory.Text("Text", stamp, "TRUE", 34, TextAlignmentOptions.Center, UIFactory.Parchment, FontStyles.Bold);
        st.characterSpacing = 8f;
        stamp.localRotation = Quaternion.Euler(0f, 0f, 14f);
        stamp.gameObject.SetActive(false);
    }

    private void BuildBuilder()
    {
        RectTransform b = UIFactory.Rect("RuleBuilder", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(920f, 340f));
        builderRoot = b.gameObject;
        for (int i = 0; i < 3; i++)
        {
            int slot = i;
            float y = -i * 88f;
            RectTransform label = UIFactory.Rect("Label" + i, b, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(180f, 72f));
            TextMeshProUGUI lt = UIFactory.Text("Text", label, i < slotLabels.Length ? slotLabels[i] : "", 21, TextAlignmentOptions.Right, UIFactory.InkFaded, FontStyles.Bold);
            lt.characterSpacing = 6f;

            Button prev = InkButton(b, "Prev" + i, "<", () => Cycle(slot, -1), new Vector2(70f, 70f), false);
            Anchor(prev, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(200f, y - 3f));
            slotButtons.Add(prev);

            RectTransform box = UIFactory.Rect("Value" + i, b, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(285f, y), new Vector2(540f, 72f));
            UIFactory.AddPanel(box, new Color(UIFactory.ParchmentDark.r, UIFactory.ParchmentDark.g, UIFactory.ParchmentDark.b, 0.55f));
            slotValues[i] = UIFactory.Text("Text", box, "", 30, TextAlignmentOptions.Center, UIFactory.InkBrown, FontStyles.Bold);

            Button next = InkButton(b, "Next" + i, ">", () => Cycle(slot, +1), new Vector2(70f, 70f), false);
            Anchor(next, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(840f, y - 3f));
            slotButtons.Add(next);
        }
        RectTransform sent = UIFactory.Rect("Sentence", b, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -262f), new Vector2(900f, 70f));
        sentence = UIFactory.Text("Text", sent, "", 30, TextAlignmentOptions.Center, UIFactory.InkBrown, FontStyles.Italic);
    }

    private void BuildClassic()
    {
        RectTransform c = UIFactory.Rect("Answers", card, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(880f, 340f));
        classicRoot = c.gameObject;
        for (int i = 0; i < classicButtons.Length; i++)
        {
            int index = i;
            Button btn = InkButton(c, "Answer" + i, "", () => PickClassic(index), new Vector2(880f, 72f), false);
            Anchor(btn, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -i * 88f));
            btn.GetComponentInChildren<TextMeshProUGUI>().fontSize = 26;
            classicButtons[i] = btn;
        }
    }

    private static Image Parchment(RectTransform rt, Color color)
    {
        Image img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIFactory.ParchmentSprite;
        img.type = Image.Type.Sliced;
        img.color = color;
        return img;
    }

    private static void InkRule(RectTransform parent, float y)
    {
        RectTransform line = UIFactory.Rect("InkRule", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(760f, 3f));
        Image img = line.gameObject.AddComponent<Image>();
        img.color = new Color(UIFactory.InkFaded.r, UIFactory.InkFaded.g, UIFactory.InkFaded.b, 0.5f);
        img.raycastTarget = false;
    }

    /// <summary>Buttons drawn as ink on paper; "primary" is a red wax button.</summary>
    private static Button InkButton(Transform parent, string name, string label, System.Action onClick, Vector2 size, bool primary)
    {
        Color bg = primary ? UIFactory.WaxRed : new Color(UIFactory.ParchmentDark.r, UIFactory.ParchmentDark.g, UIFactory.ParchmentDark.b, 0.75f);
        Button b = UIFactory.Button(name, parent, label, onClick, size, bg);
        TextMeshProUGUI t = b.GetComponentInChildren<TextMeshProUGUI>();
        t.color = primary ? UIFactory.Parchment : UIFactory.InkBrown;
        ColorBlock cb = b.colors;
        cb.highlightedColor = new Color(1.12f, 1.08f, 1.0f, 1f);
        cb.selectedColor = new Color(1.12f, 1.08f, 1.0f, 1f);
        b.colors = cb;
        return b;
    }

    private static void Anchor(Button b, Vector2 anchor, Vector2 pivot, Vector2 pos)
    {
        RectTransform rt = (RectTransform)b.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
    }

    /// <summary>
    /// A global volume, weight 0 until the card opens: depth of field blurs the game behind
    /// the card, plus a vignette and a slight desaturation. Needs post-processing enabled on
    /// the camera (the Mist Isles setup does that).
    /// </summary>
    private void BuildBlurVolume()
    {
        var go = new GameObject("CardBlurVolume");
        go.transform.SetParent(transform, false);
        blurVolume = go.AddComponent<Volume>();
        blurVolume.isGlobal = true;
        blurVolume.priority = 100f;
        blurVolume.weight = 0f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        DepthOfField dof = profile.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(0.5f);
        dof.gaussianEnd.Override(4f);
        dof.gaussianMaxRadius.Override(1.5f);
        dof.highQualitySampling.Override(true);
        Vignette v = profile.Add<Vignette>(true);
        v.intensity.Override(0.35f);
        ColorAdjustments ca = profile.Add<ColorAdjustments>(true);
        ca.saturation.Override(-25f);
        blurVolume.sharedProfile = profile;
    }
}
