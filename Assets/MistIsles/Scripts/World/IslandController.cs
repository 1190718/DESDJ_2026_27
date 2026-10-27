using System;
using System.Collections.Generic;
using MistIsles.Core;
using MistIsles.Economy;
using MistIsles.Enemies;
using MistIsles.Guardian;
using MistIsles.Rules;
using MistIsles.UI;
using UnityEngine;

namespace MistIsles.World
{
    /// <summary>
    /// Drives one island's progression:
    ///
    ///   Unsolved ──card answered──▶ RuleSolved ──guardian defeated──▶ Cleared
    ///   (crabs obey the hidden rule)  (guardian rises)                  (mist lifts, raft appears)
    ///
    /// It hands its rule to every creature on the island, so the rule lives in one asset.
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

        private static readonly List<IslandController> All = new List<IslandController>();
        private Action onCleared;

        public static IslandController PlayerIsland { get; private set; }
        public static event Action<IslandController> PlayerEnteredIsland;

        public IslandState State { get; private set; } = IslandState.Unsolved;
        public string IslandName => islandName;
        public IslandRule Rule => rule;
        public Transform RespawnPoint => respawnPoint;
        public Bounds Area => area;
        public bool HintPurchased { get; set; }
        public event Action<IslandController> StateChanged;

        public string Objective
        {
            get
            {
                switch (State)
                {
                    case IslandState.Unsolved: return "Watch the Mist Crabs closely. What rules this island? Then answer the ? card.";
                    case IslandState.RuleSolved: return guardian != null ? $"Defeat the {guardian.DisplayName}" : "The island is calming down...";
                    default: return raft != null ? "Board the raft (E) to sail on" : "Island cleared";
                }
            }
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
                e.SetRule(rule);
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
                PlayerEnteredIsland?.Invoke(this);
            }
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
