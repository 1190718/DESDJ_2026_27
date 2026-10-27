using System;
using UnityEngine;

namespace MistIsles.Economy
{
    public enum ShopItemKind
    {
        /// <summary>Restores hearts. Paid with shells.</summary>
        Heal,
        /// <summary>+1 maximum heart. Paid with shells, limited stock.</summary>
        MaxHealth,
        /// <summary>Reveals a hint about the current island's hidden rule. Paid with shells.</summary>
        RuleHint,
        /// <summary>Gives shells. Paid with real money (test mode in the prototype).</summary>
        ShellPouch
    }

    [Serializable]
    public class ShopItem
    {
        public string id;
        public string title;
        [TextArea(1, 3)] public string description;
        public ShopItemKind kind;
        [Tooltip("Price in shells (ignored for real-money items).")]
        public int price;
        [Tooltip("Hearts healed, hearts added, or shells granted.")]
        public int amount = 1;
        [Tooltip("0 = unlimited")]
        public int stock;
    }

    public static class ShopCatalog
    {
        public const string ShellPouchId = "com.mistisles.shellpouch100";

        /// <summary>Default items used when the ShopUI list is left empty.</summary>
        public static ShopItem[] Defaults() => new[]
        {
            new ShopItem { id = "heal", title = "Sea Kelp Wrap", description = "Restore 2 hearts.", kind = ShopItemKind.Heal, price = 8, amount = 2 },
            new ShopItem { id = "heart", title = "Pearl Heart", description = "+1 maximum heart (max 2).", kind = ShopItemKind.MaxHealth, price = 30, amount = 1, stock = 2 },
            new ShopItem { id = "hint", title = "Cartographer's Whisper", description = "A hint about this island's hidden rule.", kind = ShopItemKind.RuleHint, price = 5, amount = 1 },
            new ShopItem { id = ShellPouchId, title = "Shell Pouch", description = "100 shells. Real-money item, TEST MODE: no payment is taken.", kind = ShopItemKind.ShellPouch, price = 0, amount = 100 },
        };
    }
}
