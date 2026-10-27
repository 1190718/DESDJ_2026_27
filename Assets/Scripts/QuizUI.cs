using System.Collections;
using MistIsles.Core;
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
    private Coroutine lockRoutine;

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

        SetButtonsInteractable(true);

        // Pause the game and free the mouse (shared pause service, so other menus don't fight over it)
        GamePause.Push(GamePause.Quiz);
    }

    private void CheckAnswer(int answerIndex)
    {
        if (currentCheckpoint == null)
            return;

        if (currentCheckpoint.IsCorrectAnswer(answerIndex))
        {
            Checkpoint completedCheckpoint = currentCheckpoint;

            AudioManager.Play(Sfx.QuizCorrect);
            CloseQuiz();

            completedCheckpoint.Complete();
        }
        else
        {
            // Wrong answer: give a hint and lock the buttons for a moment,
            // so the player has to think instead of trying every option.
            GameStats.WrongAnswers++;
            AudioManager.Play(Sfx.QuizWrong);
            string hint = currentCheckpoint.Hint;
            feedbackText.text = string.IsNullOrEmpty(hint) ? "Incorrect!" : "Not quite. " + hint;
            if (lockRoutine != null)
                StopCoroutine(lockRoutine);
            lockRoutine = StartCoroutine(LockButtons(currentCheckpoint.WrongAnswerLockSeconds));
        }
    }

    private IEnumerator LockButtons(float seconds)
    {
        SetButtonsInteractable(false);
        // Realtime, because the game is paused while the quiz is open.
        yield return new WaitForSecondsRealtime(seconds);
        SetButtonsInteractable(true);
        lockRoutine = null;
    }

    private void SetButtonsInteractable(bool value)
    {
        foreach (Button b in answerButtons)
            b.interactable = value;
    }

    private void CloseQuiz()
    {
        quizPanel.SetActive(false);

        currentCheckpoint = null;
        if (lockRoutine != null)
        {
            StopCoroutine(lockRoutine);
            lockRoutine = null;
        }

        // Resume the game and lock the mouse again
        GamePause.Pop(GamePause.Quiz);
    }
}