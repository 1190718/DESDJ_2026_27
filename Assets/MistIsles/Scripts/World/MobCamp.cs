using System;
using System.Collections;
using System.Collections.Generic;
using MistIsles.Core;
using MistIsles.Enemies;
using MistIsles.Rules;
using MistIsles.UI;
using UnityEngine;
using UnityEngine.AI;

namespace MistIsles.World
{
    /// <summary>
    /// A spot on the island where common mobs gather. It spawns its first group on start,
    /// on random NavMesh points around itself. When that first group is wiped out, the camp
    /// drops a map fragment (exploration reward). Afterwards it slowly refills, out of the
    /// player's sight, so there are always mobs to farm shells from.
    /// Every mob it spawns receives the island's hidden rule.
    /// </summary>
    public class MobCamp : MonoBehaviour
    {
        [SerializeField] private string campName = "Camp";
        [Tooltip("Mobs to spawn; repeat a prefab to make it more common.")]
        [SerializeField] private GameObject[] mobs = Array.Empty<GameObject>();
        [SerializeField, Min(1)] private int firstGroupSize = 3;
        [SerializeField, Min(0)] private int maxAlive = 3;
        [SerializeField] private float radius = 5f;
        [Tooltip("Seconds before a dead mob is replaced (after the first group is cleared).")]
        [SerializeField] private float respawnDelay = 30f;
        [Tooltip("Mobs only respawn while the player is at least this far away.")]
        [SerializeField] private float respawnMinPlayerDistance = 14f;
        [SerializeField] private GameObject fragmentPrefab;
        [SerializeField] private IslandRule rule;

        private readonly List<EnemyBrain> alive = new List<EnemyBrain>();
        private int firstGroupLeft;
        private bool fragmentDropped;
        private Coroutine refill;
        private GameObject droppedFragment;
        private int generation;

        public string CampName => campName;
        public bool FirstGroupCleared => firstGroupLeft <= 0;
        public event Action<MobCamp> Cleared;

        public void Configure(string name, GameObject[] mobPrefabs, int groupSize, float campRadius, GameObject fragment)
        {
            campName = name;
            mobs = mobPrefabs;
            firstGroupSize = groupSize;
            maxAlive = groupSize;
            radius = campRadius;
            fragmentPrefab = fragment;
        }

        public void SetRule(IslandRule islandRule) => rule = islandRule;

        private void Start() => SpawnFirstGroup();

        private void SpawnFirstGroup()
        {
            firstGroupLeft = 0;
            for (int i = 0; i < firstGroupSize; i++)
                if (Spawn(i, firstGroup: true))
                    firstGroupLeft++;
            if (firstGroupLeft == 0)
                Debug.LogWarning($"[Mist Isles] Camp '{campName}' found no NavMesh within {radius} m; move it onto walkable ground.", this);
        }

        /// <summary>
        /// Back to the state at the start of the island: every mob removed, a fresh first group,
        /// and the map fragment can be earned again.
        /// </summary>
        public void ResetCamp()
        {
            generation++; // callbacks from the old mobs are ignored from now on
            if (refill != null)
            {
                StopCoroutine(refill);
                refill = null;
            }
            foreach (EnemyBrain b in alive)
                if (b != null)
                    Destroy(b.gameObject);
            alive.Clear();
            if (droppedFragment != null)
                Destroy(droppedFragment);
            droppedFragment = null;
            fragmentDropped = false;
            SpawnFirstGroup();
        }

        private bool Spawn(int index, bool firstGroup)
        {
            if (mobs == null || mobs.Length == 0)
                return false;
            GameObject prefab = mobs[index % mobs.Length];
            if (prefab == null || !RandomPoint(out Vector3 p))
                return false;

            GameObject go = Instantiate(prefab, p, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f), transform);
            EnemyBrain brain = go.GetComponent<EnemyBrain>();
            if (brain == null)
                return false;
            brain.SetRule(rule);
            alive.Add(brain);
            int gen = generation;
            brain.Defeated += b =>
            {
                if (gen == generation)
                    OnMobDefeated(b, firstGroup);
            };
            return true;
        }

        private void OnMobDefeated(EnemyBrain brain, bool firstGroup)
        {
            alive.Remove(brain);
            if (firstGroup && --firstGroupLeft == 0)
            {
                if (!fragmentDropped && fragmentPrefab != null)
                {
                    fragmentDropped = true;
                    Vector3 at = brain.transform.position;
                    if (NavMesh.SamplePosition(at, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                        at = hit.position;
                    droppedFragment = Instantiate(fragmentPrefab, at + Vector3.up * 0.6f, Quaternion.identity, transform.parent);
                    MapFragment f = droppedFragment.GetComponent<MapFragment>();
                    if (f != null)
                        f.DroppedByCamp = true;
                    GameHUD.Toast("A map fragment glints among the remains.", GameHUD.Good, 3f);
                }
                Cleared?.Invoke(this);
            }
            if (refill == null && maxAlive > 0)
                refill = StartCoroutine(Refill());
        }

        private IEnumerator Refill()
        {
            while (alive.Count < maxAlive)
            {
                yield return new WaitForSeconds(respawnDelay);
                Transform p = PlayerLocator.Player;
                while (p != null && Vector3.Distance(p.position, transform.position) < respawnMinPlayerDistance)
                    yield return new WaitForSeconds(2f);
                Spawn(UnityEngine.Random.Range(0, 1000), firstGroup: false);
            }
            refill = null;
        }

        private bool RandomPoint(out Vector3 point)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * radius;
                if (NavMesh.SamplePosition(transform.position + new Vector3(r.x, 0f, r.y), out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit near, radius + 3f, NavMesh.AllAreas))
            {
                point = near.position;
                return true;
            }
            point = transform.position;
            return false;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.7f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
