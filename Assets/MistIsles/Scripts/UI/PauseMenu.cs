using MistIsles.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MistIsles.UI
{
    /// <summary>Esc / Start opens the pause menu: resume, shop, controls, main menu, quit.</summary>
    public class PauseMenu : MonoBehaviour
    {
        [SerializeField] private string mainMenuScene = "MainMenu";

        private InputAction toggle;
        private GameObject panel;
        private GameObject controls;

        public static PauseMenu Instance { get; private set; }
        public bool IsOpen => panel != null && panel.activeSelf;

        private void Awake()
        {
            Instance = this;
            toggle = new InputAction("MistIslesPause", InputActionType.Button);
            toggle.AddBinding("<Keyboard>/escape");
            toggle.AddBinding("<Keyboard>/p");
            toggle.AddBinding("<Gamepad>/start");
            UIFactory.EnsureEventSystem();
            Build();
        }

        private void OnEnable() => toggle.Enable();
        private void OnDisable() => toggle.Disable();
        private void OnDestroy() => toggle.Dispose();

        private void Update()
        {
            if (!toggle.WasPressedThisFrame())
                return;
            if (IsOpen)
                Close();
            else if (ShopUI.Instance != null && ShopUI.Instance.IsOpen)
                ShopUI.Instance.Close();
            else if (!GamePause.Has(GamePause.Quiz) && !GamePause.Has(GamePause.EndCard))
                Open();
        }

        public void Open()
        {
            panel.SetActive(true);
            controls.SetActive(false);
            GamePause.Push(GamePause.Menu);
        }

        public void Close()
        {
            panel.SetActive(false);
            GamePause.Pop(GamePause.Menu);
        }

        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void GoToMainMenu()
        {
            GamePause.ClearAll();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SceneManager.LoadScene(mainMenuScene);
        }

        private void Build()
        {
            Canvas canvas = UIFactory.CreateCanvas("PauseCanvas", 50, transform);
            RectTransform dim = UIFactory.Stretch("Pause", canvas.transform);
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            panel = dim.gameObject;

            RectTransform box = UIFactory.Rect("Box", dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 560f));
            UIFactory.AddPanel(box, UIFactory.PanelLight);
            UIFactory.Stack(box, 16f, new RectOffset(40, 40, 40, 40));

            TextMeshProUGUI title = UIFactory.Text("Title", box, "Paused", 54, TextAlignmentOptions.Center, UIFactory.Accent, FontStyles.Bold);
            title.rectTransform.sizeDelta = new Vector2(0f, 80f);
            UIFactory.Button("Resume", box, "Resume", Close, new Vector2(0f, 64f));
            UIFactory.Button("Shop", box, "Shop", () =>
            {
                Close();
                ShopUI.Instance?.Open();
            }, new Vector2(0f, 64f));
            UIFactory.Button("Controls", box, "Controls", () => controls.SetActive(!controls.activeSelf), new Vector2(0f, 64f));
            bool hasMenu = Application.CanStreamedLevelBeLoaded(mainMenuScene);
            Button menu = UIFactory.Button("MainMenu", box, "Main menu", GoToMainMenu, new Vector2(0f, 64f));
            menu.interactable = hasMenu;
            UIFactory.Button("Quit", box, "Quit game", QuitGame, new Vector2(0f, 64f));

            RectTransform c = UIFactory.Rect("ControlsPanel", dim, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(290f, 0f), new Vector2(560f, 420f));
            UIFactory.AddPanel(c, UIFactory.Panel);
            TextMeshProUGUI ct = UIFactory.Text("Text", c,
                "<b>Move</b>  WASD / left stick\n<b>Sprint</b>  Shift\n<b>Jump</b>  Space\n<b>Look</b>  Mouse / right stick\n" +
                "<b>Attack</b>  Left click / F / West button\n<b>Interact</b>  E / North button\n<b>Shop</b>  B\n<b>Pause</b>  Esc / Start",
                26, TextAlignmentOptions.TopLeft, UIFactory.Ink);
            ct.margin = new Vector4(30, 30, 30, 30);
            controls = c.gameObject;

            panel.SetActive(false);
        }
    }
}
