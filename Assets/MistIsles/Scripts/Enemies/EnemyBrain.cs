using System;
using MistIsles.Core;
using MistIsles.Rules;
using UnityEngine;
using UnityEngine.AI;

namespace MistIsles.Enemies
{
    /// <summary>
    /// Finite state machine for the Mist Crab, built on NavMeshAgent.
    ///
    ///   Patrol ──sees player──▶ Suspicious ──0.6 s──▶ Chase ──in range──▶ Attack
    ///     ▲                          │                  │  ▲                │
    ///     └──────── Return ◀──lost───┴──lost / leash────┘  └──cooldown──────┘
    ///
    /// Before running the FSM each frame the brain asks the island's rule whether it may move.
    /// If not, the agent stops, the animator pauses and the FSM's own clock stops, so the crab
    /// freezes mid-action. That freeze is the clue the player has to notice.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public class EnemyBrain : MonoBehaviour
    {
        public enum State { Patrol, Suspicious, Chase, Attack, Return, Dead }

        [Header("Island rule (set by IslandController)")]
        [SerializeField] private IslandRule rule;

        [Header("Perception")]
        [SerializeField] private float sightRange = 11f;
        [SerializeField, Range(10f, 360f)] private float sightAngle = 130f;
        [SerializeField] private float hearingRange = 3.5f;
        [SerializeField] private float loseInterestAfter = 3.5f;
        [SerializeField] private float eyeHeight = 0.5f;

        [Header("Movement")]
        [SerializeField] private float patrolRadius = 7f;
        [SerializeField] private float patrolSpeed = 1.6f;
        [SerializeField] private float chaseSpeed = 3.6f;
        [SerializeField] private float leashRange = 20f;
        [SerializeField] private Vector2 patrolPause = new Vector2(1f, 3f);
        [SerializeField] private float turnSpeed = 540f;

        [Header("Attack")]
        [SerializeField] private float attackRange = 1.5f;
        [SerializeField] private int attackDamage = 1;
        [SerializeField] private float attackCooldown = 1.4f;
        [SerializeField] private float attackDuration = 0.9f;
        [SerializeField] private float attackHitMoment = 0.45f;

        [Header("Reward")]
        [SerializeField] private GameObject lootPrefab;
        [SerializeField] private int lootCount = 2;
        [SerializeField] private float corpseLifetime = 4f;

        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int HitId = Animator.StringToHash("Hit");
        private static readonly int DieId = Animator.StringToHash("Die");

        private NavMeshAgent agent;
        private Health health;
        private Animator animator;
        private Vector3 home;

        // The brain's own clock. It only advances while the rule allows movement,
        // so cooldowns and timers pause together with the body.
        private float clock;
        private float stateTime;
        private float lastSeen = float.NegativeInfinity;
        private float nextAttack;
        private float waitUntil;
        private float nextRepath;
        private bool attackLanded;

        public State Current { get; private set; } = State.Patrol;
        public bool Frozen { get; private set; }
        public IslandRule Rule => rule;
        public event Action<State> StateChanged;
        public event Action<EnemyBrain> Defeated;

        public void SetRule(IslandRule newRule) => rule = newRule;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            animator = GetComponentInChildren<Animator>();
            home = transform.position;
            health.Damaged += OnDamaged;
            health.Died += OnDied;
        }

        private void Start()
        {
            if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            home = transform.position;
            waitUntil = UnityEngine.Random.Range(0f, patrolPause.y);
        }

        private void Update()
        {
            if (Current == State.Dead || !agent.isOnNavMesh)
                return;

            RuleContext ctx = RuleContext.For(transform);
            SetFrozen(rule != null && !rule.AllowsMovement(ctx));
            if (Frozen)
                return;

            float dt = Time.deltaTime;
            clock += dt;
            stateTime += dt;

            if (CanPerceivePlayer(ctx))
                lastSeen = clock;

            switch (Current)
            {
                case State.Patrol: TickPatrol(); break;
                case State.Suspicious: TickSuspicious(); break;
                case State.Chase: TickChase(); break;
                case State.Attack: TickAttack(); break;
                case State.Return: TickReturn(); break;
            }

            if (animator != null)
                animator.SetFloat(SpeedId, agent.velocity.magnitude / Mathf.Max(0.01f, chaseSpeed));
        }

        // ------------------------------------------------------------------ states

        private void TickPatrol()
        {
            agent.speed = patrolSpeed;
            if (SeesPlayerNow())
            {
                Enter(State.Suspicious);
                return;
            }
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
            {
                if (waitUntil <= 0f)
                    waitUntil = clock + UnityEngine.Random.Range(patrolPause.x, patrolPause.y);
                else if (clock >= waitUntil)
                {
                    waitUntil = 0f;
                    if (RandomPointNear(home, patrolRadius, out Vector3 p))
                        agent.SetDestination(p);
                }
            }
        }

        private void TickSuspicious()
        {
            agent.ResetPath();
            FacePlayer();
            if (stateTime >= 0.6f)
                Enter(SeesPlayerNow() ? State.Chase : State.Patrol);
        }

        private void TickChase()
        {
            agent.speed = chaseSpeed;
            Transform player = PlayerLocator.Player;
            if (player == null || !PlayerLocator.IsAlive || clock - lastSeen > loseInterestAfter
                || Vector3.Distance(transform.position, home) > leashRange)
            {
                Enter(State.Return);
                return;
            }

            float dist = FlatDistance(player.position);
            if (dist <= attackRange)
            {
                agent.ResetPath();
                FacePlayer();
                if (clock >= nextAttack)
                    Enter(State.Attack);
                return;
            }
            if (clock >= nextRepath)
            {
                nextRepath = clock + 0.2f;
                agent.SetDestination(player.position);
            }
        }

        private void TickAttack()
        {
            FacePlayer();
            if (!attackLanded && stateTime >= attackHitMoment)
            {
                attackLanded = true;
                Transform player = PlayerLocator.Player;
                if (player != null && FlatDistance(player.position) <= attackRange + 0.5f)
                {
                    Vector3 dir = (player.position - transform.position).normalized;
                    PlayerLocator.Health?.TakeDamage(new DamageInfo(attackDamage, player.position, dir, gameObject));
                }
            }
            if (stateTime >= attackDuration)
            {
                nextAttack = clock + attackCooldown;
                Enter(State.Chase);
            }
        }

        private void TickReturn()
        {
            agent.speed = patrolSpeed * 1.4f;
            if (stateTime < 0.05f)
                agent.SetDestination(home);
            if (SeesPlayerNow() && Vector3.Distance(transform.position, home) < leashRange * 0.5f)
            {
                Enter(State.Chase);
                return;
            }
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f)
            {
                health.ResetHealth();
                Enter(State.Patrol);
            }
        }

        private void Enter(State next)
        {
            if (Current == State.Dead)
                return;
            Current = next;
            stateTime = 0f;
            switch (next)
            {
                case State.Suspicious:
                    AudioManager.Play(Sfx.CrabClick, transform.position);
                    break;
                case State.Attack:
                    attackLanded = false;
                    agent.ResetPath();
                    if (animator != null)
                        animator.SetTrigger(AttackId);
                    AudioManager.Play(Sfx.CrabAttack, transform.position);
                    break;
                case State.Patrol:
                    waitUntil = 0f;
                    break;
            }
            StateChanged?.Invoke(next);
        }

        // ------------------------------------------------------------------ rule freeze

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

        // ------------------------------------------------------------------ perception

        private bool SeesPlayerNow() => clock - lastSeen < 0.05f;

        private bool CanPerceivePlayer(in RuleContext ctx)
        {
            Transform player = PlayerLocator.Player;
            if (player == null || !PlayerLocator.IsAlive)
                return false;

            Vector3 to = player.position - transform.position;
            float dist = to.magnitude;
            if (dist <= hearingRange)
                return true;

            float range = sightRange * (rule != null ? rule.PerceptionMultiplier(ctx) : 1f);
            if (dist > range)
                return false;
            to.y = 0f;
            if (Vector3.Angle(transform.forward, to) > sightAngle * 0.5f)
                return false;

            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            Vector3 target = PlayerLocator.AimPoint;
            if (Physics.Linecast(eye, target, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.transform.IsChildOf(player) || hit.transform.IsChildOf(transform);
            return true;
        }

        // ------------------------------------------------------------------ damage

        private void OnDamaged(DamageInfo info)
        {
            if (Current == State.Dead)
                return;
            AudioManager.Play(Sfx.HitFlesh, transform.position);
            if (animator != null && !Frozen)
                animator.SetTrigger(HitId);
            Vector3 push = info.Direction;
            push.y = 0f;
            if (push.sqrMagnitude > 0.001f && agent.isOnNavMesh)
                agent.Move(push.normalized * 0.45f);
            lastSeen = clock;
            if (Current == State.Patrol || Current == State.Suspicious || Current == State.Return)
                Enter(State.Chase);
        }

        private void OnDied()
        {
            Current = State.Dead;
            StateChanged?.Invoke(State.Dead);
            if (agent.isOnNavMesh)
                agent.isStopped = true;
            agent.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>())
                c.enabled = false;
            if (animator != null)
            {
                animator.speed = 1f;
                animator.SetTrigger(DieId);
            }
            AudioManager.Play(Sfx.CrabDie, transform.position);
            GameStats.CrabsDefeated++;
            DropLoot();
            Defeated?.Invoke(this);
            Destroy(gameObject, corpseLifetime);
        }

        private void DropLoot()
        {
            if (lootPrefab == null)
                return;
            for (int i = 0; i < lootCount; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * 0.8f;
                Instantiate(lootPrefab, transform.position + new Vector3(r.x, 0.4f, r.y), Quaternion.identity);
            }
        }

        // ------------------------------------------------------------------ helpers

        private void FacePlayer()
        {
            Transform player = PlayerLocator.Player;
            if (player == null)
                return;
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.001f)
                return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), turnSpeed * Time.deltaTime);
        }

        private float FlatDistance(Vector3 p)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private static bool RandomPointNear(Vector3 centre, float radius, out Vector3 point)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * radius;
                if (NavMesh.SamplePosition(centre + new Vector3(r.x, 0f, r.y), out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }
            point = centre;
            return false;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 h = Application.isPlaying ? home : transform.position;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(h, patrolRadius);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, sightRange);
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}
