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
    ///                                        │
    ///                                        └─ wrong rule: card crumbles, ambush rises, 2 new fragments needed
    ///                                           (one from the defeated ambush, one from any camp emptied again)
    ///
    /// Until the island is cleared the player cannot leave its area: a wall of mist pushes
    /// them back at the edge of the water around it.
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
        [Tooltip("Map fragments needed to redraw the card after a wrong rule.")]
        [SerializeField, Min(1)] private int retryFragments = 2;

        [Header("Boundary")]
        [Tooltip("Keep the player inside this island's area until it is cleared.")]
        [SerializeField] private bool confinePlayerUntilCleared = true;
        [Tooltip("How far into the sea the player may go from this island's coast before the mist pushes back.")]
        [SerializeField] private float boundaryWater = 6f;

        private static readonly List<IslandController> All = new List<IslandController>();
        private Action onCleared;
        private readonly List<EnemyBrain> ambushers = new List<EnemyBrain>();
        private readonly List<EnemyBrain> placedEnemies = new List<EnemyBrain>();
        private readonly List<GameObject> ambushFragments = new List<GameObject>();
        private int fragmentsTarget = -1;
        private bool retrying;
        private float nextBoundaryToast;

        public static IslandController PlayerIsland { get; private set; }
        public static event Action<IslandController> PlayerEnteredIsland;

        public IslandState State { get; private set; } = IslandState.Unsolved;
        public string IslandName => islandName;
        public IslandRule Rule => rule;
        public Transform RespawnPoint => respawnPoint;
        public Bounds Area => area;
        public bool HintPurchased { get; set; }
        public int FragmentsFound { get; private set; }
        /// <summary>Fragments needed right now: the full map at first, fewer to redraw the card after a wrong rule.</summary>
        public int FragmentsRequired => fragmentsTarget >= 0 ? fragmentsTarget : fragmentsRequired;
        public bool CardRevealed => card == null || FragmentsFound >= FragmentsRequired;
        /// <summary>True after a wrong rule, until the card has been redrawn.</summary>
        public bool Retrying => retrying;
        public bool AmbushActive => ambushers.Count > 0;
        public event Action<IslandController> StateChanged;

        public string Objective
        {
            get
            {
                switch (State)
                {
                    case IslandState.Unsolved:
                        if (retrying && !CardRevealed)
                        {
                            string ambush = AmbushActive ? $"Defeat the ambush ({ambushers.Count} left) and empty" : "Empty";
                            return $"The card crumbled. {ambush} a camp to find map fragments and redraw it ({FragmentsFound}/{FragmentsRequired}).";
                        }
                        if (!CardRevealed)
                            return $"Explore {islandName}: clear the camps and find map fragments ({FragmentsFound}/{FragmentsRequired}). Watch how the creatures behave.";
                        return "Climb to the cartographer's card and write down the island's hidden rule.";
                    case IslandState.RuleSolved: return guardian != null ? $"Defeat the {guardian.DisplayName}" : "The island is calming down...";
                    default:
                        if (raft == null)
                            return "Island cleared";
                        if (raft.Aboard)
                            return $"Steer the raft with WASD towards {raft.DestinationName}. Press E near a shore to step off.";
                        return raft.AtFarShore
                            ? $"Explore {raft.DestinationName}. Your raft waits on the water to take you back."
                            : $"Board the raft by the shore (E) and sail to {raft.DestinationName}";
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
                if (retrying)
                {
                    retrying = false;
                    DisarmCamps();
                    GameHUD.Banner("The card is redrawn", "Try again, and use what you saw");
                }
                else
                    GameHUD.Banner("The map is complete", "The cartographer's card has appeared");
                GameHUD.Toast("Now write down the island's hidden rule on the card. Think before you answer.", GameHUD.Good, 5f);
            }
            else
            {
                GameHUD.Toast($"Map fragment found ({FragmentsFound}/{FragmentsRequired}).", GameHUD.Good, 2.5f);
            }
        }

        private void RefreshFragmentCounter()
        {
            if (PlayerIsland != this)
                return;
            bool show = State == IslandState.Unsolved && card != null && fragmentsRequired > 0 && !CardRevealed;
            GameHUD.SetFragments(FragmentsFound, show ? FragmentsRequired : 0);
        }

        // ------------------------------------------------------------------ ambush

        /// <summary>
        /// Called by the card after a wrong rule. The card crumbles (sealed) and has to be
        /// redrawn with <see cref="retryFragments"/> new map fragments. Two sources always exist:
        /// the ambush that rises around the card drops one when it is wiped out, and every camp
        /// drops one the next time it is emptied (empty camps regroup a few seconds later).
        /// Returns false if no ambush mobs could be spawned (the fragments are still required).
        /// </summary>
        public bool StartAmbush(Checkpoint fromCard)
        {
            card = fromCard;
            card.SetSealed(true);
            retrying = true;
            FragmentsFound = 0;
            fragmentsTarget = retryFragments;
            foreach (MobCamp camp in GetComponentsInChildren<MobCamp>(true))
                camp.ArmRetryFragment();
            RefreshFragmentCounter();
            StateChanged?.Invoke(this);

            if (ambushMobs == null || ambushMobs.Length == 0 || AmbushActive)
            {
                GameHUD.Toast("The card crumbles into the mist. Empty a camp to find the fragments again.", GameHUD.Danger, 4f);
                return false;
            }
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
            AudioManager.Play(Sfx.GuardianRoar, centre, 0f, 0.6f);
            GameHUD.Toast(ambushers.Count > 0
                ? "The card crumbles and creatures rise around it! Defeat them and empty a camp to redraw the card."
                : "The card crumbles into the mist. Empty a camp to find the fragments again.", GameHUD.Danger, 4.5f);
            return ambushers.Count > 0;
        }

        private void OnAmbusherDefeated(EnemyBrain brain)
        {
            ambushers.Remove(brain);
            if (ambushers.Count > 0)
                return;
            // the last one drops a fragment of the crumbled card
            GameObject prefab = FragmentPrefab();
            if (retrying && !CardRevealed && prefab != null)
            {
                Vector3 at = brain.transform.position;
                if (NavMesh.SamplePosition(at, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    at = hit.position;
                GameObject go = Instantiate(prefab, at + Vector3.up * 0.6f, Quaternion.identity, transform);
                MapFragment f = go.GetComponent<MapFragment>();
                if (f != null)
                    f.DroppedByCamp = true; // destroyed (not restored) on an island reset
                ambushFragments.Add(go);
                GameHUD.Toast("The ambush is broken. A piece of the card falls to the ground.", GameHUD.Good, 3.5f);
            }
        }

        private GameObject FragmentPrefab()
        {
            foreach (MobCamp camp in GetComponentsInChildren<MobCamp>(true))
                if (camp.FragmentPrefab != null)
                    return camp.FragmentPrefab;
            return null;
        }

        private void DisarmCamps()
        {
            foreach (MobCamp camp in GetComponentsInChildren<MobCamp>(true))
                camp.Disarm();
        }

        // ------------------------------------------------------------------ boundary

        /// <summary>True while the player must stay inside this island's area (enforced by PlayerBoundary).</summary>
        public bool ConfinesPlayer => confinePlayerUntilCleared && State != IslandState.Cleared;

        // Boundary mask: a 1 m grid over the island area. A cell is allowed if it is this island's
        // land (a ray from above first hits a collider that belongs to the island) or sea within
        // boundaryWater metres of that land. So the limit follows the real coastline, whatever
        // shape the island has after being edited, and never reaches another island's shore.
        private const float Cell = 1f;
        private bool[] allowed;
        private int gridW, gridH;
        private float gridX0, gridZ0;

        private void Start() => BuildBoundaryMask();

        /// <summary>Recomputes the boundary (call after moving island pieces at runtime).</summary>
        public void BuildBoundaryMask()
        {
            allowed = null;
            gridW = Mathf.CeilToInt(area.size.x / Cell) + 1;
            gridH = Mathf.CeilToInt(area.size.z / Cell) + 1;
            if (gridW <= 1 || gridH <= 1 || gridW * gridH > 400000)
                return;
            gridX0 = area.min.x;
            gridZ0 = area.min.z;

            var land = new bool[gridW * gridH];
            int landCells = 0;
            float top = area.max.y + 5f, length = area.size.y + 30f;
            for (int iz = 0; iz < gridH; iz++)
                for (int ix = 0; ix < gridW; ix++)
                {
                    var origin = new Vector3(gridX0 + ix * Cell, top, gridZ0 + iz * Cell);
                    if (IsOwnLand(origin, length))
                    {
                        land[iz * gridW + ix] = true;
                        landCells++;
                    }
                }
            if (landCells < 30)
            {
                Debug.LogWarning($"[Mist Isles] {islandName}: only {landCells} land cells found (pieces without colliders?). Using the area box as the boundary.");
                return;
            }

            // grow the land by boundaryWater (breadth-first, 8 neighbours)
            int reach = Mathf.Max(0, Mathf.RoundToInt(boundaryWater / Cell));
            var dist = new int[land.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < land.Length; i++)
            {
                dist[i] = land[i] ? 0 : int.MaxValue;
                if (land[i])
                    queue.Enqueue(i);
            }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                if (dist[i] >= reach)
                    continue;
                int x = i % gridW, z = i / gridW;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= gridW || nz >= gridH)
                            continue;
                        int n = nz * gridW + nx;
                        if (dist[n] > dist[i] + 1)
                        {
                            dist[n] = dist[i] + 1;
                            queue.Enqueue(n);
                        }
                    }
            }
            allowed = new bool[land.Length];
            for (int i = 0; i < land.Length; i++)
                allowed[i] = dist[i] <= reach;
        }

        private bool IsOwnLand(Vector3 origin, float length)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider first = null;
            foreach (RaycastHit h in hits)
            {
                Collider c = h.collider;
                if (c.GetComponentInParent<Health>() != null || c.GetComponentInParent<NavMeshAgent>() != null
                    || c.GetComponentInParent<CharacterController>() != null)
                    continue; // creatures and the player are not ground
                if (h.distance < best)
                {
                    best = h.distance;
                    first = c;
                }
            }
            return first != null && first.gameObject.layer != 4 && first.transform.IsChildOf(transform);
        }

        /// <summary>Whether the player may stand at this point (coast mask, or the area box as a fallback).</summary>
        public bool IsInsideBoundary(Vector3 p)
        {
            if (allowed == null)
                return area.Contains(new Vector3(p.x, area.center.y, p.z));
            int ix = Mathf.RoundToInt((p.x - gridX0) / Cell);
            int iz = Mathf.RoundToInt((p.z - gridZ0) / Cell);
            if (ix < 0 || iz < 0 || ix >= gridW || iz >= gridH)
                return false;
            return allowed[iz * gridW + ix];
        }

        /// <summary>PlayerBoundary calls this when it stops the player at the edge.</summary>
        public void NotifyBoundaryHit()
        {
            if (Time.time < nextBoundaryToast)
                return;
            nextBoundaryToast = Time.time + 4f;
            GameHUD.Toast($"A wall of mist pushes you back. Free {islandName} before sailing on.", GameHUD.Neutral, 3f);
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
            foreach (GameObject f in ambushFragments)
                if (f != null)
                    Destroy(f);
            ambushFragments.Clear();
            retrying = false;
            fragmentsTarget = -1;

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
