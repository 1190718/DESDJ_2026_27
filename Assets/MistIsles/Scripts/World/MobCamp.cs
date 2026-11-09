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
    /// After a wrong rule the island "arms" every camp: the next time a camp is emptied it
    /// drops another fragment, so the card can always be earned back.
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
        [Tooltip("After a wrong rule, an empty camp regroups this many seconds later.")]
        [SerializeField] private float retryRegroupDelay = 6f;

        private readonly List<EnemyBrain> alive = new List<EnemyBrain>();
        private int firstGroupLeft;
        private bool fragmentDropped;
        private Coroutine refill;
        private GameObject droppedFragment;
        private readonly List<GameObject> retryFragments = new List<GameObject>();
        private int generation;
        private bool armed;

        public string CampName => campName;
        public GameObject FragmentPrefab => fragmentPrefab;
        /// <summary>True while the camp still owes a map fragment for a retry of the card.</summary>
        public bool Armed => armed;
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
            foreach (GameObject f in retryFragments)
                if (f != null)
                    Destroy(f);
            retryFragments.Clear();
            fragmentDropped = false;
            armed = false;
            SpawnFirstGroup();
        }

        /// <summary>
        /// Called after a wrong rule: the next time this camp is emptied it drops a map fragment.
        /// An already empty camp regroups shortly, so there is always something to fight.
        /// </summary>
        public void ArmRetryFragment()
        {
            armed = true;
            if (alive.Count == 0)
            {
                if (refill != null)
                    StopCoroutine(refill);
                refill = StartCoroutine(Regroup());
            }
        }

        public void Disarm() => armed = false;

        private IEnumerator Regroup()
        {
            int gen = generation;
            yield return new WaitForSeconds(retryRegroupDelay);
            // prefer to appear out of the player's face, but never wait forever
            Transform p = PlayerLocator.Player;
            for (float waited = 0f; waited < 15f && p != null && Vector3.Distance(p.position, transform.position) < radius + 4f; waited += 1f)
                yield return new WaitForSeconds(1f);
            if (gen == generation)
                for (int i = alive.Count; i < Mathf.Max(1, maxAlive); i++)
                    Spawn(UnityEngine.Random.Range(0, 1000), firstGroup: false);
            refill = null;
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
            bool droppedNow = false;
            if (firstGroup && --firstGroupLeft == 0)
            {
                if (!fragmentDropped && fragmentPrefab != null)
                {
                    fragmentDropped = true;
                    droppedFragment = DropFragment(brain.transform.position);
                    droppedNow = true;
                }
                Cleared?.Invoke(this);
            }
            if (armed && alive.Count == 0 && fragmentPrefab != null)
            {
                armed = false;
                if (!droppedNow)
                    retryFragments.Add(DropFragment(brain.transform.position));
            }
            if (refill == null && maxAlive > 0)
                refill = StartCoroutine(Refill());
        }

        private GameObject DropFragment(Vector3 at)
        {
            if (NavMesh.SamplePosition(at, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                at = hit.position;
            GameObject go = Instantiate(fragmentPrefab, at + Vector3.up * 0.6f, Quaternion.identity, transform.parent);
            MapFragment f = go.GetComponent<MapFragment>();
            if (f != null)
                f.DroppedByCamp = true;
            GameHUD.Toast("A map fragment glints among the remains.", GameHUD.Good, 3f);
            return go;
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
