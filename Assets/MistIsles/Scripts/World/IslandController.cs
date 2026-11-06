using System;
using System.Collections.Generic;
using MistIsles.Core;
using MistIsles.Economy;
using MistIsles.Enemies;
using MistIsles.Guardian;
using MistIsles.Rules;
using MistIsles.UI;
using UnityEngine;
using UnityEngine.AI;

namespace MistIsles.World
{
    /// <summary>
    /// Drives one island's progression:
    ///
    ///   Exploring ──3 map fragments──▶ card revealed ──rule written──▶ RuleSolved ──guardian──▶ Cleared
    ///   (mobs obey the hidden rule)    (wrong rule = ambush)           (guardian rises)          (mist lifts, raft)
    ///
    /// It hands its rule to every creature and camp on the island, so the rule lives in one asset.
    /// CheckpointManager calls RuleSolved() when this island's checkpoint is completed and
    /// only unlocks the next island's checkpoint once this island is cleared.
    /// </summary>
    public class IslandController : MonoBehaviour
    {
        public enum IslandState { Unsolved, RuleSolved, Cleared }

        [SerializeField] private string islandName = "Still Shore";
        [SerializeField] private IslandRule rule;
        [SerializeField] private ShoreGuardian guardian;
        [SerializeField] private IslandMist mist;
        [SerializeField] private RaftTravel raft;
        [SerializeField] private Transform respawnPoint;
        [Tooltip("World-space area of the island, used to know where the player is.")]
        [SerializeField] private Bounds area = new Bounds(Vector3.zero, new Vector3(80f, 60f, 80f));
        [SerializeField] private int clearReward = 25;

        [Header("Exploration")]
        [Tooltip("The island's rule card. Sealed until enough map fragments are found.")]
        [SerializeField] private Checkpoint card;
        [SerializeField, Min(0)] private int fragmentsRequired = 3;

        [Header("Ambush (wrong rule)")]
        [SerializeField] private GameObject[] ambushMobs = Array.Empty<GameObject>();
        [SerializeField, Min(1)] private int ambushSize = 3;

        private static readonly List<IslandController> All = new List<IslandController>();
        private Action onCleared;
        private readonly List<EnemyBrain> ambushers = new List<EnemyBrain>();
        private readonly List<EnemyBrain> placedEnemies = new List<EnemyBrain>();

        public static IslandController PlayerIsland { get; private set; }
        public static event Action<IslandController> PlayerEnteredIsland;

        public IslandState State { get; private set; } = IslandState.Unsolved;
        public string IslandName => islandName;
        public IslandRule Rule => rule;
        public Transform RespawnPoint => respawnPoint;
        public Bounds Area => area;
        public bool HintPurchased { get; set; }
        public int FragmentsFound { get; private set; }
        public int FragmentsRequired => fragmentsRequired;
        public bool CardRevealed => card == null || FragmentsFound >= fragmentsRequired;
        public bool AmbushActive => ambushers.Count > 0;
        public event Action<IslandController> StateChanged;

        public string Objective
        {
            get
            {
                switch (State)
                {
                    case IslandState.Unsolved:
                        if (AmbushActive)
                            return $"The mist rejected your rule! Defeat the ambush ({ambushers.Count} left)";
                        if (!CardRevealed)
                            return $"Explore {islandName}: clear the camps and find map fragments ({FragmentsFound}/{fragmentsRequired}). Watch how the creatures behave.";
                        return "Climb to the cartographer's card and write down the island's hidden rule.";
                    case IslandState.RuleSolved: return guardian != null ? $"Defeat the {guardian.DisplayName}" : "The island is calming down...";
                    default: return raft != null ? "Board the raft (E) to sail on" : "Island cleared";
                }
            }
        }

        public void ConfigureExploration(Checkpoint islandCard, int fragments, GameObject[] ambush, int ambushCount)
        {
            card = islandCard;
            fragmentsRequired = fragments;
            ambushMobs = ambush;
            ambushSize = ambushCount;
        }

        public void Configure(string name, IslandRule islandRule, ShoreGuardian islandGuardian, IslandMist islandMist,
                              RaftTravel islandRaft, Transform respawn, Bounds bounds)
        {
            islandName = name;
            rule = islandRule;
            guardian = islandGuardian;
            mist = islandMist;
            raft = islandRaft;
            respawnPoint = respawn;
            area = bounds;
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Awake()
        {
            foreach (EnemyBrain e in GetComponentsInChildren<EnemyBrain>(true))
            {
                e.SetRule(rule);
                if (e.GetComponentInParent<MobCamp>() == null)
                    placedEnemies.Add(e);
            }
            foreach (MobCamp c in GetComponentsInChildren<MobCamp>(true))
                c.SetRule(rule);
            if (card == null)
                card = GetComponentInChildren<Checkpoint>(true);
            // Awake runs before CheckpointManager.Start, so the card is hidden from the first frame.
            if (card != null && fragmentsRequired > 0)
                card.SetSealed(true);
            if (guardian != null)
            {
                guardian.SetRule(rule);
                guardian.Defeated += Clear;
            }
        }

        private void Update()
        {
            Transform p = PlayerLocator.Player;
            if (p == null)
                return;
            bool inside = area.Contains(p.position);
            if (inside && PlayerIsland != this)
            {
                PlayerIsland = this;
                // a new island is a new run: what was earned before arriving is safe
                Wallet.Instance?.StartRun();
                PlayerEnteredIsland?.Invoke(this);
                RefreshFragmentCounter();
            }
        }

        // ------------------------------------------------------------------ exploration

        public void AddFragment()
        {
            if (CardRevealed)
                return;
            FragmentsFound++;
            RefreshFragmentCounter();
            if (CardRevealed)
            {
                card.SetSealed(false);
                AudioManager.Play(Sfx.MistLift);
                GameHUD.Banner("The map is complete", "The cartographer's card has appeared");
                GameHUD.Toast("Now write down the island's hidden rule on the card. Think before you answer.", GameHUD.Good, 5f);
            }
            else
            {
                GameHUD.Toast($"Map fragment found ({FragmentsFound}/{fragmentsRequired}).", GameHUD.Good, 2.5f);
            }
        }

        private void RefreshFragmentCounter()
        {
            if (PlayerIsland != this)
                return;
            bool show = State == IslandState.Unsolved && card != null && fragmentsRequired > 0 && !CardRevealed;
            GameHUD.SetFragments(FragmentsFound, show ? fragmentsRequired : 0);
        }

        // ------------------------------------------------------------------ ambush

        /// <summary>
        /// Called by the card after a wrong rule. The card is sealed and mobs rise around it;
        /// it only comes back once they are all defeated, so guessing is costly.
        /// Returns false if no ambush could be spawned (the card then stays open).
        /// </summary>
        public bool StartAmbush(Checkpoint fromCard)
        {
            if (ambushMobs == null || ambushMobs.Length == 0 || AmbushActive)
                return false;
            Vector3 centre = fromCard.transform.position;
            // the card may float above a rock: ring the ambush around the nearest walkable ground
            if (NavMesh.SamplePosition(centre, out NavMeshHit ground, 15f, NavMesh.AllAreas))
                centre = ground.position;
            for (int i = 0; i < ambushSize; i++)
            {
                GameObject prefab = ambushMobs[i % ambushMobs.Length];
                if (prefab == null)
                    continue;
                Vector3 offset = Quaternion.Euler(0f, i * 360f / ambushSize + UnityEngine.Random.Range(-20f, 20f), 0f) * Vector3.forward * UnityEngine.Random.Range(3.5f, 6f);
                if (!NavMesh.SamplePosition(centre + offset, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    continue;
                GameObject go = Instantiate(prefab, hit.position, Quaternion.LookRotation(-offset), transform);
                EnemyBrain brain = go.GetComponent<EnemyBrain>();
                if (brain == null)
                    continue;
                brain.SetRule(rule);
                brain.Alert();
                ambushers.Add(brain);
                brain.Defeated += OnAmbusherDefeated;
            }
            if (ambushers.Count == 0)
                return false;
            card = fromCard;
            card.SetSealed(true);
            AudioManager.Play(Sfx.GuardianRoar, centre, 0f, 0.6f);
            GameHUD.Toast("The mist rejects your rule... creatures rise around the card!", GameHUD.Danger, 3.5f);
            return true;
        }

        private void OnAmbusherDefeated(EnemyBrain brain)
        {
            ambushers.Remove(brain);
            if (ambushers.Count > 0)
                return;
            if (card != null && CardRevealed)
                card.SetSealed(false);
            GameHUD.Toast("The card settles again. Think carefully before you write.", GameHUD.Good, 3f);
        }

        // ------------------------------------------------------------------ death reset

        /// <summary>
        /// Called by PlayerRespawn when the player dies on this island (behind the fade).
        /// A cleared island stays cleared. Otherwise everything goes back to how it was on
        /// arrival: camps refilled, fragments to find again, the card sealed and unanswered,
        /// the guardian buried, loose shells gone. Returns false if nothing was reset.
        /// </summary>
        public bool ResetIsland()
        {
            if (State == IslandState.Cleared)
                return false;

            foreach (EnemyBrain b in ambushers)
                if (b != null)
                    Destroy(b.gameObject);
            ambushers.Clear();

            foreach (MobCamp camp in GetComponentsInChildren<MobCamp>(true))
                camp.ResetCamp();
            foreach (EnemyBrain e in placedEnemies)
                if (e != null)
                    e.ResetToHome();
            foreach (MapFragment f in GetComponentsInChildren<MapFragment>(true))
            {
                if (f.DroppedByCamp)
                    Destroy(f.gameObject);
                else
                    f.ResetFragment();
            }
            foreach (CurrencyPickup pickup in FindObjectsByType<CurrencyPickup>(FindObjectsSortMode.None))
                if (area.Contains(pickup.transform.position))
                    Destroy(pickup.gameObject);

            if (guardian != null)
                guardian.ResetToDormant();

            FragmentsFound = 0;
            onCleared = null;
            State = IslandState.Unsolved;
            if (card != null)
            {
                card.ResetCard();
                card.SetSealed(fragmentsRequired > 0);
            }
            RefreshFragmentCounter();
            StateChanged?.Invoke(this);
            return true;
        }

        /// <summary>The rule card was answered correctly.</summary>
        public void RuleSolved(Action whenCleared)
        {
            if (State != IslandState.Unsolved)
            {
                whenCleared?.Invoke();
                return;
            }
            onCleared = whenCleared;
            State = IslandState.RuleSolved;
            StateChanged?.Invoke(this);
            RefreshFragmentCounter();
            if (rule != null && !string.IsNullOrEmpty(rule.RevealText))
                GameHUD.Toast(rule.RevealText, GameHUD.Good, 4f);
            if (guardian != null)
                guardian.Awaken();
            else
                Clear();
        }

        private void Clear()
        {
            if (State == IslandState.Cleared)
                return;
            State = IslandState.Cleared;
            if (mist != null)
                mist.Lift();
            if (raft != null)
                raft.Show();
            Wallet.Instance?.Add(clearReward);
            // clearing the island banks the run: these shells can no longer be lost
            Wallet.Instance?.StartRun();
            AudioManager.Play(Sfx.MistLift);
            GameHUD.Banner($"{islandName} is free", "The mist lifts. A raft drifts ashore.");
            StateChanged?.Invoke(this);
            onCleared?.Invoke();
            onCleared = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            All.Clear();
            PlayerIsland = null;
            PlayerEnteredIsland = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.25f);
            Gizmos.DrawWireCube(area.center, area.size);
        }
    }
}
