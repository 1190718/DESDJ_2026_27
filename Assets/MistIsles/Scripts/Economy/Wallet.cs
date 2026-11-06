using System;
using UnityEngine;

namespace MistIsles.Economy
{
    /// <summary>
    /// The player's soft currency (shells). Earned by defeating crabs and clearing islands,
    /// spent in the shop, and also obtainable through the test-mode store purchase.
    ///
    /// Shells earned in the field belong to the current "run" (one attempt at an island).
    /// Dying loses them; clearing the island banks them. Store purchases are never at risk.
    /// </summary>
    public class Wallet : MonoBehaviour
    {
        public static Wallet Instance { get; private set; }

        [SerializeField] private string currencyName = "Shells";
        [SerializeField, Min(0)] private int startingAmount = 0;

        public int Amount { get; private set; }
        /// <summary>Shells earned since the current run started (not yet banked).</summary>
        public int RunEarnings { get; private set; }
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

        /// <param name="atRisk">False for shells that can never be lost (store purchases).</param>
        public void Add(int amount, bool atRisk = true)
        {
            if (amount <= 0)
                return;
            Amount += amount;
            if (atRisk)
                RunEarnings += amount;
            Changed?.Invoke(Amount);
        }

        /// <summary>Banks everything earned so far: it can no longer be lost by dying.</summary>
        public void StartRun() => RunEarnings = 0;

        /// <summary>
        /// Called on death. Removes the shells earned during this run (or what is left of them,
        /// if some were already spent) and starts a new run. Returns how many were lost.
        /// </summary>
        public int LoseRunEarnings()
        {
            int lost = Mathf.Min(RunEarnings, Amount);
            RunEarnings = 0;
            if (lost <= 0)
                return 0;
            Amount -= lost;
            Changed?.Invoke(Amount);
            return lost;
        }

        public bool CanAfford(int price) => Amount >= price;

        public bool TrySpend(int price)
        {
            if (price < 0 || Amount < price)
                return false;
            Amount -= price;
            // spending comes out of the run's earnings first, so they can't be lost twice
            RunEarnings = Mathf.Max(0, RunEarnings - price);
            Changed?.Invoke(Amount);
            return true;
        }
    }
}
