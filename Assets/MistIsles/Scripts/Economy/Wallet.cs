using System;
using UnityEngine;

namespace MistIsles.Economy
{
    /// <summary>
    /// The player's soft currency (shells). Earned by defeating crabs and clearing islands,
    /// spent in the shop, and also obtainable through the test-mode store purchase.
    /// </summary>
    public class Wallet : MonoBehaviour
    {
        public static Wallet Instance { get; private set; }

        [SerializeField] private string currencyName = "Shells";
        [SerializeField, Min(0)] private int startingAmount = 0;

        public int Amount { get; private set; }
        public string CurrencyName => currencyName;
        public event Action<int> Changed;

        private void Awake()
        {
            Instance = this;
            Amount = startingAmount;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Add(int amount)
        {
            if (amount <= 0)
                return;
            Amount += amount;
            Changed?.Invoke(Amount);
        }

        public bool CanAfford(int price) => Amount >= price;

        public bool TrySpend(int price)
        {
            if (price < 0 || Amount < price)
                return false;
            Amount -= price;
            Changed?.Invoke(Amount);
            return true;
        }
    }
}
