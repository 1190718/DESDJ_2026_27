using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class QuizUI : MonoBehaviour
{
    public static QuizUI Instance { get; private set; }

    [Header("UI")]
    [SerializeField] private GameObject quizPanel;
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private Button[] answerButtons;

    private Checkpoint currentCheckpoint;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        quizPanel.SetActive(false);
    }

    public void ShowQuestion(Checkpoint checkpoint)
    {
        // Don't open another question while one is already open
        if (currentCheckpoint != null)
            return;

        currentCheckpoint = checkpoint;

        quizPanel.SetActive(true);

        questionText.text = checkpoint.Question;
        feedbackText.text = "";

        string[] answers = checkpoint.Answers;

        for (int i = 0; i < answerButtons.Length; i++)
        {
            answerButtons[i].onClick.RemoveAllListeners();

            if (i < answers.Length)
            {
                answerButtons[i].gameObject.SetActive(true);

                TMP_Text buttonText =
                    answerButtons[i].GetComponentInChildren<TMP_Text>();

                buttonText.text = answers[i];

                int answerIndex = i;

                answerButtons[i].onClick.AddListener(
                    () => CheckAnswer(answerIndex)
                );
            }
            else
            {
                answerButtons[i].gameObject.SetActive(false);
            }
        }

        // Pause the game
        Time.timeScale = 0f;

        // Allow the mouse to click UI buttons
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void CheckAnswer(int answerIndex)
    {
        if (currentCheckpoint == null)
            return;

        if (currentCheckpoint.IsCorrectAnswer(answerIndex))
        {
            Checkpoint completedCheckpoint = currentCheckpoint;

            CloseQuiz();

            completedCheckpoint.Complete();
        }
        else
        {
            feedbackText.text = "Incorrect!";
        }
    }

    private void CloseQuiz()
    {
        quizPanel.SetActive(false);

        currentCheckpoint = null;

        // Resume the game
        Time.timeScale = 1f;

        // Return mouse control to the game
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}