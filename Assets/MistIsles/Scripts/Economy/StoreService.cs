using System;

namespace MistIsles.Economy
{
    /// <summary>Real-money store abstraction. The shop only talks to this interface.</summary>
    public interface IStoreService
    {
        bool IsTestMode { get; }
        string PriceLabel(string productId);
        void Purchase(string productId, Action<bool, string> onComplete);
    }

    /// <summary>
    /// Test-mode store for the prototype: purchases always succeed and no payment happens.
    /// A Unity IAP implementation of IStoreService can replace it without touching the shop
    /// (that swap is planned for the final stage, after the professor confirms the approach).
    /// </summary>
    public class MockStoreService : IStoreService
    {
        public bool IsTestMode => true;

        public string PriceLabel(string productId)
        {
            switch (productId)
            {
                case ShopCatalog.ShellPouchId: return "0,99 €";
                default: return "—";
            }
        }

        public void Purchase(string productId, Action<bool, string> onComplete)
        {
            onComplete?.Invoke(true, "Test purchase completed (no payment was made).");
        }
    }
}
