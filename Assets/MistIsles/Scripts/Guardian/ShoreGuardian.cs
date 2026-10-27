using System;
using System.Collections;
using MistIsles.Core;
using MistIsles.Enemies;
using MistIsles.Rules;
using MistIsles.UI;
using UnityEngine;
using UnityEngine.AI;

namespace MistIsles.Guardian
{
    /// <summary>
    /// Still Shore's guardian. It obeys the island rule like the crabs, and the rule is also
    /// the way to beat it:
    ///  - While it moves, its shell is sealed and sword hits only clang off.
    ///  - When the player stands still, it freezes; after a short moment the glowing core
    ///    on its back opens and it can be damaged.
    ///  - Tremor: if the player stays still too long (5 s, 3 s in phase 2) the island shakes
    ///    and hits everyone nearby, so camping in one spot doesn't work.
    ///  - Phase 2 (half health): faster, slams with both claws (area damage) and calls two Mist Crabs.
    /// The guardian sleeps under the sand until the rule is deduced on the Cartographer's Card.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class ShoreGuardian : MonoBehaviour
    {
        public enum Phase { Dormant, Emerging, Fighting, Dead }

        [SerializeField] private string displayName = "Shore Guardian";
        [SerializeField] private IslandRule rule;

        [Header("Emerge")]
        [SerializeField] private float buryDepth = 4f;
        [SerializeField] private float emergeSeconds = 3f;

        [Header("Movement")]
        [SerializeField] private float chaseSpeed = 3.2f;
        [SerializeField] private float phaseTwoSpeedMultiplier = 1.35f;
        [SerializeField] private float turnSpeed = 240f;

        [Header("Attacks")]
        [SerializeField] private float attackRange = 3.2f;
        [SerializeField] private int attackDamage = 1;
        [SerializeField] private float attackCooldown = 1.8f;
        [SerializeField] private float attackHitMoment = 0.5f;
        [SerializeField] private float attackDuration = 1.1f;
        [SerializeField] private float slamRadius = 4.5f;

        [Header("Weak point")]
        [Tooltip("How long it must stay frozen before the core opens.")]
        [SerializeField] private float vulnerableAfter = 0.35f;
        [SerializeField] private Color coreColor = new Color(0.25f, 0.9f, 0.8f);
        [SerializeField] private float coreGlowOpen = 6f;
        [SerializeField] private float coreGlowClosed = 0.4f;

        [Header("Tremor (anti-camping)")]
        [SerializeField] private float tremorStillTimePhaseOne = 5f;
        [SerializeField] private float tremorStillTimePhaseTwo = 3f;
        [SerializeField] private float tremorWindup = 1.1f;
        [SerializeField] private float tremorRadius = 9f;

        [Header("Phase 2")]
        [SerializeField] private GameObject minionPrefab;
        [SerializeField] private int minionCount = 2;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int SlamId = Animator.StringToHash("Slam");
        private static readonly int RoarId = Animator.StringToHash("Roar");
        private static readonly int DieId = Animator.StringToHash("Die");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private NavMeshAgent agent;
        private Health health;
        private Animator animator;
        private Renderer[] renderers;
        private Collider[] colliders;
        private Material coreMaterial;
        private Vector3 restPosition;

        private float clock;
        private float frozenFor;
        private float nextAttack;
        private float nextRepath;
        private bool attacking;
        private bool phaseTwo;
        private bool tremorRunning;
        private bool lastBlockToast;
        private float coreGlow;

        public Phase Current { get; private set; } = Phase.Dormant;
        public bool Vulnerable { get; private set; }
        public bool Frozen { get; private set; }
        public string DisplayName => displayName;
        public Health Health => health;
        public event Action Defeated;

        public void SetRule(IslandRule newRule) => rule = newRule;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            animator = GetComponentInChildren<Animator>();
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            coreMaterial = FindCoreMaterial();

            health.DamageFilter = _ => Vulnerable;
            health.Blocked += OnBlocked;
            health.Damaged += OnDamaged;
            health.Died += OnDied;

            restPosition = transform.position;
            SetDormant();
        }

        private Material FindCoreMaterial()
        {
            foreach (Renderer r in renderers)
                foreach (Material m in r.materials) // instanced copies, safe to edit
                    if (m != null && m.name.StartsWith("Core"))
                    {
                        m.EnableKeyword("_EMISSION");
                        return m;
                    }
            return null;
        }

        private void SetDormant()
        {
            Current = Phase.Dormant;
            agent.enabled = false;
            foreach (Collider c in colliders)
                c.enabled = false;
            foreach (Renderer r in renderers)
                r.enabled = false;
            transform.position = restPosition + Vector3.down * buryDepth;
        }

        /// <summary>Called by the IslandController when the rule card is answered correctly.</summary>
        public void Awaken()
        {
            if (Current == Phase.Dormant)
                StartCoroutine(Emerge());
        }

        private IEnumerator Emerge()
        {
            Current = Phase.Emerging;
            foreach (Renderer r in renderers)
                r.enabled = true;
            if (animator != null)
                animator.SetTrigger(RoarId);
            AudioManager.PlayMusic(MusicTrack.Guardian);
            AudioManager.Play(Sfx.GuardianSlam, restPosition, 0f);
            GameHUD.Toast($"The {displayName} rises from the sand!", GameHUD.Danger, 3f);

            Vector3 from = restPosition + Vector3.down * buryDepth;
            for (float t = 0f; t < emergeSeconds; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / emergeSeconds);
                Vector3 shake = UnityEngine.Random.insideUnitSphere * 0.08f * (1f - k);
                transform.position = Vector3.Lerp(from, restPosition, k) + shake;
                if (t > emergeSeconds * 0.6f && t - Time.deltaTime <= emergeSeconds * 0.6f)
                    AudioManager.Play(Sfx.GuardianRoar, restPosition, 0f);
                yield return null;
            }
            transform.position = restPosition;
            foreach (Collider c in colliders)
                c.enabled = true;
            agent.enabled = true;
            agent.Warp(restPosition);
            Current = Phase.Fighting;
            GameHUD.ShowBoss(displayName, health, () => Vulnerable);
        }

        private void Update()
        {
            UpdateCoreGlow();
            if (Current != Phase.Fighting || !agent.isOnNavMesh)
                return;

            RuleContext ctx = RuleContext.For(transform);
            bool frozen = rule != null && !rule.AllowsMovement(ctx);
            SetFrozen(frozen);

            // Weak point: opens after being frozen for a moment.
            frozenFor = frozen ? frozenFor + Time.deltaTime : 0f;
            bool vulnerable = frozen && frozenFor >= vulnerableAfter;
            if (vulnerable && !Vulnerable)
                AudioManager.Play(Sfx.GuardianArmorOpen, transform.position, 0f);
            Vulnerable = vulnerable;

            // Tremor runs on real time even while frozen: the island punishes standing still too long.
            float tremorAfter = phaseTwo ? tremorStillTimePhaseTwo : tremorStillTimePhaseOne;
            if (!tremorRunning && ctx.PlayerStillTime >= tremorAfter && ctx.DistanceToPlayer <= tremorRadius * 1.5f)
                StartCoroutine(Tremor());

            if (frozen)
                return;

            clock += Time.deltaTime;
            agent.speed = chaseSpeed * (phaseTwo ? phaseTwoSpeedMultiplier : 1f);

            Transform player = PlayerLocator.Player;
            if (player == null || !PlayerLocator.IsAlive)
            {
                agent.ResetPath();
            }
            else if (!attacking)
            {
                Vector3 to = player.position - transform.position;
                to.y = 0f;
                if (to.magnitude <= attackRange)
                {
                    agent.ResetPath();
                    if (to.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), turnSpeed * Time.deltaTime);
                    if (clock >= nextAttack)
                        StartCoroutine(AttackRoutine());
                }
                else if (clock >= nextRepath)
                {
                    nextRepath = clock + 0.25f;
                    agent.SetDestination(player.position);
                }
            }

            if (animator != null)
                animator.SetFloat(SpeedId, agent.velocity.magnitude / Mathf.Max(0.01f, chaseSpeed));
        }

        private void SetFrozen(bool frozen)
        {
            if (frozen == Frozen)
                return;
            Frozen = frozen;
            agent.isStopped = frozen;
            if (frozen)
                agent.velocity = Vector3.zero;
            if (animator != null)
                animator.speed = frozen ? 0f : 1f;
        }

        /// <summary>Runs on the guardian's own clock, so a freeze pauses the attack mid-swing.</summary>
        private IEnumerator AttackRoutine()
        {
            attacking = true;
            bool slam = phaseTwo;
            if (animator != null)
                animator.SetTrigger(slam ? SlamId : AttackId);
            float start = clock;
            bool landed = false;
            while (clock - start < attackDuration && Current == Phase.Fighting)
            {
                if (!landed && clock - start >= attackHitMoment)
                {
                    landed = true;
                    if (slam)
                        AudioManager.Play(Sfx.GuardianSlam, transform.position, 0.05f, 0.7f);
                    else
                        AudioManager.Play(Sfx.CrabAttack, transform.position, 0.05f);
                    HitPlayerWithin(slam ? slamRadius : attackRange + 0.6f);
                }
                yield return null;
            }
            nextAttack = clock + attackCooldown;
            attacking = false;
        }

        private IEnumerator Tremor()
        {
            tremorRunning = true;
            GameHUD.Toast("The ground trembles... move!", GameHUD.Danger, tremorWindup + 0.5f);
            AudioManager.Play(Sfx.TremorWarning, transform.position, 0f);
            yield return new WaitForSeconds(tremorWindup);
            if (Current == Phase.Fighting)
            {
                AudioManager.Play(Sfx.GuardianSlam, transform.position, 0f);
                HitPlayerWithin(tremorRadius);
            }
            yield return new WaitForSeconds(1.5f);
            tremorRunning = false;
        }

        private void HitPlayerWithin(float radius)
        {
            Transform player = PlayerLocator.Player;
            if (player == null)
                return;
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            if (to.magnitude <= radius)
                PlayerLocator.Health?.TakeDamage(new DamageInfo(attackDamage, player.position, to.normalized, gameObject));
        }

        private void UpdateCoreGlow()
        {
            if (coreMaterial == null)
                return;
            float target = Vulnerable ? coreGlowOpen : coreGlowClosed;
            coreGlow = Mathf.MoveTowards(coreGlow, target, Time.deltaTime * 20f);
            float pulse = Vulnerable ? 1f + 0.25f * Mathf.Sin(Time.time * 10f) : 1f;
            coreMaterial.SetColor(EmissionId, coreColor * coreGlow * pulse);
        }

        private void OnBlocked(DamageInfo info)
        {
            AudioManager.Play(Sfx.ShellClang, info.Point);
            if (!lastBlockToast)
            {
                lastBlockToast = true;
                GameHUD.Toast("The shell is sealed while it moves...", GameHUD.Neutral, 2.5f);
            }
        }

        private void OnDamaged(DamageInfo info)
        {
            AudioManager.Play(Sfx.HitFlesh, info.Point, 0.05f, 1.2f);
            if (!phaseTwo && health.Current <= health.Max / 2)
                EnterPhaseTwo();
        }

        private void EnterPhaseTwo()
        {
            phaseTwo = true;
            if (animator != null)
                animator.SetTrigger(RoarId);
            AudioManager.Play(Sfx.GuardianRoar, transform.position, 0f);
            GameHUD.Toast($"The {displayName} calls the tide for help!", GameHUD.Danger, 3f);
            if (minionPrefab == null)
                return;
            for (int i = 0; i < minionCount; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, i * 360f / minionCount + 45f, 0f) * Vector3.forward * 4f;
                if (NavMesh.SamplePosition(transform.position + offset, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                {
                    GameObject m = Instantiate(minionPrefab, hit.position, Quaternion.identity, transform.parent);
                    EnemyBrain brain = m.GetComponent<EnemyBrain>();
                    if (brain != null)
                        brain.SetRule(rule);
                }
            }
        }

        private void OnDied()
        {
            Current = Phase.Dead;
            Vulnerable = false;
            StopAllCoroutines();
            if (agent.isOnNavMesh)
                agent.isStopped = true;
            agent.enabled = false;
            if (animator != null)
            {
                animator.speed = 1f;
                animator.SetTrigger(DieId);
            }
            AudioManager.Play(Sfx.GuardianRoar, transform.position, 0f, 0.8f);
            AudioManager.PlayMusic(MusicTrack.Island);
            GameHUD.HideBoss();
            Defeated?.Invoke();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, attackRange);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, tremorRadius);
        }
    }
}
