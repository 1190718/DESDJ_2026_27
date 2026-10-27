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

    [Header("Wrong Answer")]
    [Tooltip("Shown after a wrong answer to nudge the player toward the hidden rule.")]
    [TextArea(1, 3)]
    [SerializeField] private string hint;
    [Tooltip("Seconds the answer buttons stay locked after a wrong answer.")]
    [SerializeField, Min(0f)] private float wrongAnswerLockSeconds = 3f;

    [Header("Checkpoint Components")]
    [SerializeField] private BoxCollider checkpointCollider;
    [SerializeField] private GameObject collectedGlow;

    private bool completed = false;
    private bool activeCheckpoint = false;

    private CheckpointManager checkpointManager;

    public string Question => question;
    public string[] Answers => answers;
    public string Hint => hint;
    public float WrongAnswerLockSeconds => wrongAnswerLockSeconds;

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

        if (checkpointCollider != null)
        {
            checkpointCollider.enabled = active;
        }
    }

    public bool IsCorrectAnswer(int answerIndex)
    {
        return answerIndex == correctAnswerIndex;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (completed)
            return;

        if (!activeCheckpoint)
            return;

        if (!other.CompareTag("Player"))
            return;

        // Normal checkpoint
        if (QuizUI.Instance != null)
        {
            QuizUI.Instance.ShowQuestion(this);
        }
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