using System.Collections.Generic;
using MistIsles.Core;
using MistIsles.Economy;
using MistIsles.World;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MistIsles.UI
{
    /// <summary>
    /// The Cartographer's Market. Shell items are bought with the in-game currency; the
    /// Shell Pouch is a real-money item that goes through IStoreService (test mode in the
    /// prototype, with an explicit confirmation that no payment is taken).
    /// Open with B or from the pause menu.
    /// </summary>
    public class ShopUI : MonoBehaviour
    {
        [SerializeField] private List<ShopItem> items = new List<ShopItem>();

        public static ShopUI Instance { get; private set; }
        public bool IsOpen => panel != null && panel.activeSelf;

        private readonly Dictionary<string, int> bought = new Dictionary<string, int>();
        private readonly List<(ShopItem item, Button button, TextMeshProUGUI label)> rows = new List<(ShopItem, Button, TextMeshProUGUI)>();
        private IStoreService store;
        private InputAction toggle;
        private GameObject panel;
        private GameObject confirm;
        private TextMeshProUGUI confirmText;
        private TextMeshProUGUI balance;
        private ShopItem pending;

        private void Awake()
        {
            Instance = this;
            if (items.Count == 0)
                items.AddRange(ShopCatalog.Defaults());
            store = new MockStoreService();
            toggle = new InputAction("MistIslesShop", InputActionType.Button);
            toggle.AddBinding("<Keyboard>/b");
            toggle.AddBinding("<Gamepad>/select");
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
            else if (!GamePause.IsPaused)
                Open();
        }

        public void Open()
        {
            if (GamePause.Has(GamePause.Quiz) || GamePause.Has(GamePause.EndCard))
                return;
            panel.SetActive(true);
            confirm.SetActive(false);
            Refresh();
            GamePause.Push(GamePause.Shop);
        }

        public void Close()
        {
            panel.SetActive(false);
            GamePause.Pop(GamePause.Shop);
        }

        // ------------------------------------------------------------------ buying

        private void OnBuy(ShopItem item)
        {
            if (item.kind == ShopItemKind.ShellPouch)
            {
                pending = item;
                confirmText.text = $"<b>TEST MODE</b>\n\nBuy <b>{item.title}</b> ({item.amount} shells) for {store.PriceLabel(item.id)}?\n\n" +
                                   "<size=80%>This is a simulated purchase. No payment will be taken.</size>";
                confirm.SetActive(true);
                return;
            }

            Wallet wallet = Wallet.Instance;
            if (wallet == null || !wallet.CanAfford(item.price))
            {
                GameHUD.Toast("Not enough shells.", GameHUD.Danger, 1.5f);
                return;
            }
            if (!CanApply(item, out string reason))
            {
                GameHUD.Toast(reason, GameHUD.Neutral, 2f);
                return;
            }
            wallet.TrySpend(item.price);
            Apply(item);
            bought[item.id] = Bought(item) + 1;
            AudioManager.Play(Sfx.Pickup);
            Refresh();
        }

        private void ConfirmPurchase()
        {
            ShopItem item = pending;
            pending = null;
            confirm.SetActive(false);
            if (item == null)
                return;
            store.Purchase(item.id, (ok, message) =>
            {
                if (ok)
                {
                    Wallet.Instance?.Add(item.amount);
                    AudioManager.Play(Sfx.QuizCorrect);
                }
                GameHUD.Toast(message, ok ? GameHUD.Good : GameHUD.Danger, 2.5f);
                Refresh();
            });
        }

        private int Bought(ShopItem item) => bought.TryGetValue(item.id, out int n) ? n : 0;

        private bool CanApply(ShopItem item, out string reason)
        {
            reason = null;
            Health h = PlayerLocator.Health;
            switch (item.kind)
            {
                case ShopItemKind.Heal:
                    if (h != null && h.Current >= h.Max) { reason = "Your hearts are already full."; return false; }
                    break;
                case ShopItemKind.RuleHint:
                    IslandController island = IslandController.PlayerIsland;
                    if (island == null || island.Rule == null || island.State != IslandController.IslandState.Unsolved)
                    { reason = "No secrets left to whisper here."; return false; }
                    break;
            }
            if (item.stock > 0 && Bought(item) >= item.stock)
            {
                reason = "Sold out.";
                return false;
            }
            return true;
        }

        private static void Apply(ShopItem item)
        {
            Health h = PlayerLocator.Health;
            switch (item.kind)
            {
                case ShopItemKind.Heal:
                    h?.Heal(item.amount);
                    break;
                case ShopItemKind.MaxHealth:
                    if (h != null)
                    {
                        h.SetMax(h.Max + item.amount, false);
                        h.Heal(item.amount);
                    }
                    break;
                case ShopItemKind.RuleHint:
                    IslandController island = IslandController.PlayerIsland;
                    island.HintPurchased = true;
                    GameHUD.Toast("The cartographer whispers: " + island.Rule.Hint, GameHUD.Good, 6f);
                    break;
            }
        }

        // ------------------------------------------------------------------ layout

        private void Refresh()
        {
            Wallet wallet = Wallet.Instance;
            int amount = wallet != null ? wallet.Amount : 0;
            balance.text = $"{amount} {(wallet != null ? wallet.CurrencyName : "Shells")}";
            foreach (var (item, button, label) in rows)
            {
                bool soldOut = item.stock > 0 && Bought(item) >= item.stock;
                if (item.kind == ShopItemKind.ShellPouch)
                {
                    label.text = $"{store.PriceLabel(item.id)}  (test)";
                    button.interactable = true;
                }
                else
                {
                    label.text = soldOut ? "Sold out" : $"{item.price} shells";
                    button.interactable = !soldOut && amount >= item.price;
                }
            }
        }

        private void Build()
        {
            Canvas canvas = UIFactory.CreateCanvas("ShopCanvas", 60, transform);
            RectTransform dim = UIFactory.Stretch("Shop", canvas.transform);
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            panel = dim.gameObject;

            RectTransform box = UIFactory.Rect("Box", dim, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 200f + items.Count * 118f));
            UIFactory.AddPanel(box, UIFactory.PanelLight);

            TextMeshProUGUI title = UIFactory.Text("Title", box, "Cartographer's Market", 46, TextAlignmentOptions.TopLeft, UIFactory.Accent, FontStyles.Bold);
            title.margin = new Vector4(40, 30, 40, 0);
            balance = UIFactory.Text("Balance", box, "", 30, TextAlignmentOptions.TopRight, UIFactory.Shell, FontStyles.Bold);
            balance.margin = new Vector4(40, 40, 40, 0);

            for (int i = 0; i < items.Count; i++)
            {
                ShopItem item = items[i];
                RectTransform row = UIFactory.Rect("Row" + i, box, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f - i * 118f), new Vector2(900f, 104f));
                UIFactory.AddPanel(row, UIFactory.Panel);
                TextMeshProUGUI name = UIFactory.Text("Name", row, item.title, 30, TextAlignmentOptions.TopLeft, UIFactory.Ink, FontStyles.Bold);
                name.margin = new Vector4(24, 14, 300, 0);
                TextMeshProUGUI desc = UIFactory.Text("Desc", row, item.description, 22, TextAlignmentOptions.TopLeft, UIFactory.InkQuiet);
                desc.margin = new Vector4(24, 54, 300, 0);

                Button buy = UIFactory.Button("Buy", row, "", () => OnBuy(item), new Vector2(250f, 64f),
                    item.kind == ShopItemKind.ShellPouch ? new Color(0.85f, 0.65f, 0.25f) : (Color?)null);
                RectTransform brt = (RectTransform)buy.transform;
                brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.5f);
                brt.pivot = new Vector2(1f, 0.5f);
                brt.anchoredPosition = new Vector2(-20f, 0f);
                rows.Add((item, buy, buy.GetComponentInChildren<TextMeshProUGUI>()));
            }

            Button close = UIFactory.Button("Close", box, "Close (B)", Close, new Vector2(240f, 56f));
            RectTransform crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0f);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.anchoredPosition = new Vector2(0f, 24f);

            // test-mode confirmation dialog
            RectTransform c = UIFactory.Stretch("Confirm", dim);
            c.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            RectTransform cbox = UIFactory.Rect("Box", c, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 380f));
            UIFactory.AddPanel(cbox, UIFactory.PanelLight);
            confirmText = UIFactory.Text("Text", cbox, "", 28, TextAlignmentOptions.Top, UIFactory.Ink);
            confirmText.margin = new Vector4(40, 36, 40, 100);
            Button yes = UIFactory.Button("Yes", cbox, "Simulate purchase", ConfirmPurchase, new Vector2(290f, 60f), UIFactory.Accent);
            ((RectTransform)yes.transform).anchoredPosition = new Vector2(-155f, -130f);
            Button no = UIFactory.Button("No", cbox, "Cancel", () => confirm.SetActive(false), new Vector2(290f, 60f));
            ((RectTransform)no.transform).anchoredPosition = new Vector2(155f, -130f);
            confirm = c.gameObject;

            confirm.SetActive(false);
            panel.SetActive(false);
        }
    }
}
