using UnityEngine;

public class Checkpoint : MonoBehaviour
{
    [Header("Question")]
    [TextArea(2, 5)]
    [SerializeField] private string question;

    [Header("Answers")]
    [SerializeField] private string[] answers = new string[4];

    [Header("Correct Answer")]
    [Tooltip("0 = first answer, 1 = second answer, etc.")]
    [SerializeField] private int correctAnswerIndex;

    [Header("Rule builder (optional)")]
    [Tooltip("If all three lists are filled, the card asks the player to assemble the rule from three parts instead of picking one of the answers above.")]
    [TextArea(1, 3)]
    [SerializeField] private string ruleIntro;
    [SerializeField] private string[] ruleSubjects = new string[0];
    [SerializeField] private string[] ruleActions = new string[0];
    [SerializeField] private string[] ruleConditions = new string[0];
    [SerializeField] private int correctSubject;
    [SerializeField] private int correctAction;
    [SerializeField] private int correctCondition;

    [Header("Wrong Answer")]
    [Tooltip("Shown after a wrong answer to nudge the player toward the hidden rule.")]
    [TextArea(1, 3)]
    [SerializeField] private string hint;
    [Tooltip("Seconds the answer buttons stay locked after a wrong answer (4-answer mode only).")]
    [SerializeField, Min(0f)] private float wrongAnswerLockSeconds = 3f;
    [Tooltip("Health the player loses for a wrong answer. A wrong answer never kills.")]
    [SerializeField, Min(0)] private int wrongAnswerDamage = 15;
    [Tooltip("On an island with an IslandController, a wrong answer seals the card and summons mobs.")]
    [SerializeField] private bool ambushOnWrongAnswer = true;

    [Header("Visual")]
    [Tooltip("Hidden while the card is sealed. Defaults to the child with FloatingCheckpoint.")]
    [SerializeField] private GameObject visual;

    [Header("Checkpoint Components")]
    [SerializeField] private BoxCollider checkpointCollider;
    [SerializeField] private GameObject collectedGlow;

    private bool completed = false;
    private bool activeCheckpoint = false;
    private bool sealedByIsland = false;

    private CheckpointManager checkpointManager;

    public string Question => question;
    public string[] Answers => answers;
    public string Hint => hint;
    public float WrongAnswerLockSeconds => wrongAnswerLockSeconds;
    public int WrongAnswerDamage => wrongAnswerDamage;
    public bool AmbushOnWrongAnswer => ambushOnWrongAnswer;
    public bool IsSealed => sealedByIsland;

    public bool IsRuleBuilder =>
        ruleSubjects != null && ruleSubjects.Length > 0 &&
        ruleActions != null && ruleActions.Length > 0 &&
        ruleConditions != null && ruleConditions.Length > 0;
    public string RuleIntro => ruleIntro;
    public string[] RuleSubjects => ruleSubjects;
    public string[] RuleActions => ruleActions;
    public string[] RuleConditions => ruleConditions;

    // Remembered between openings of the card (selections and the last feedback).
    public int[] LastChoice { get; set; }
    public string LastFeedback { get; set; }

    /// <summary>How many of the three parts of the rule are right (0-3).</summary>
    public int CountCorrectParts(int subject, int action, int condition)
    {
        return (subject == correctSubject ? 1 : 0)
             + (action == correctAction ? 1 : 0)
             + (condition == correctCondition ? 1 : 0);
    }

    public void Initialize(CheckpointManager manager, bool isActive)
    {
        checkpointManager = manager;

        // Make sure the glow starts turned off
        if (collectedGlow != null)
        {
            collectedGlow.SetActive(false);
        }

        SetCheckpointActive(isActive);
    }

    public void SetCheckpointActive(bool active)
    {
        activeCheckpoint = active;
        ApplyColliderAndVisual();
    }

    /// <summary>
    /// A sealed card is hidden and cannot be touched. Islands seal their card until the
    /// player has explored enough (map fragments) and while an ambush is running.
    /// </summary>
    public void SetSealed(bool value)
    {
        sealedByIsland = value;
        ApplyColliderAndVisual();
    }

    private void ApplyColliderAndVisual()
    {
        if (checkpointCollider != null)
            checkpointCollider.enabled = activeCheckpoint && !sealedByIsland && !completed;

        if (visual == null)
        {
            FloatingCheckpoint f = GetComponentInChildren<FloatingCheckpoint>(true);
            if (f != null)
                visual = f.gameObject;
        }
        if (visual != null && !completed)
            visual.SetActive(!sealedByIsland);
    }

    public bool IsCorrectAnswer(int answerIndex)
    {
        return answerIndex == correctAnswerIndex;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (completed)
            return;

        if (!activeCheckpoint || sealedByIsland)
            return;

        if (!other.CompareTag("Player"))
            return;

        // Normal checkpoint
        if (QuizUI.Instance != null)
        {
            QuizUI.Instance.ShowQuestion(this);
        }
    }

    /// <summary>
    /// Puts the card back to unanswered (used when the player dies and the island resets).
    /// The island decides afterwards whether it stays sealed.
    /// </summary>
    public void ResetCard()
    {
        // A completed card was the manager's current target (the index only advances once the
        // island is cleared), so it becomes the active card again.
        if (completed)
            activeCheckpoint = true;
        completed = false;
        LastChoice = null;
        LastFeedback = null;
        if (collectedGlow != null)
            collectedGlow.SetActive(false);
        ApplyColliderAndVisual();
    }

    public void Complete()
    {
        if (completed)
            return;

        completed = true;
        activeCheckpoint = false;

        // Disable this checkpoint's collider
        if (checkpointCollider != null)
        {
            checkpointCollider.enabled = false;
        }

        // Turn on the neon border
        if (collectedGlow != null)
        {
            collectedGlow.SetActive(true);
        }

        // Tell the manager to activate the next checkpoint
        if (checkpointManager != null)
        {
            checkpointManager.CheckpointCompleted(this);
        }
    }
}