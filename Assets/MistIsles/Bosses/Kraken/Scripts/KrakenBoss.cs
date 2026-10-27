using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace MistIsles.Bosses.Kraken
{
    public enum KrakenState { Dormant, Emerging, Idle, Attacking, Stunned, PhaseTransition, Dead }
    public enum KrakenAttack { SlamLeft, SlamRight, Sweep, Grab, Ink, Summon }

    [Serializable] public class KrakenIntEvent : UnityEvent<int> { }
    [Serializable] public class KrakenFloatEvent : UnityEvent<float> { }
    [Serializable] public class KrakenVector3Event : UnityEvent<Vector3> { }

    /// <summary>
    /// Drowned Isle guardian. Sits in the water facing the island and fights with tentacle attacks.
    /// Rule: water protects the player (grab/ink) and a slam that lands in shallow water gets stuck -> stun ->
    /// the glowing weak point on its head becomes hittable. Gets faster and nastier in phases 2 and 3.
    /// Place the prefab pivot at the water surface, facing the shore.
    /// </summary>
    public class KrakenBoss : MonoBehaviour
    {
        [Header("References")]
        public Animator animator;
        [Tooltip("Found by tag if empty.")]
        public Transform player;
        public string playerTag = "Player";
        [Tooltip("Player components disabled while grabbed (ThirdPersonController is found automatically).")]
        public Behaviour[] disableOnPlayerWhileGrabbed;

        [Header("Encounter")]
        [Tooltip("Emerge as soon as the scene starts. Otherwise call Awaken() (e.g. after the Cartographer's Card is solved).")]
        public bool awakenOnStart = false;
        [Tooltip("If > 0, emerge when the player gets this close (horizontal metres).")]
        public float awakenRadius = 0f;
        [Tooltip("Degrees per second the kraken turns to track the player.")]
        public float turnSpeed = 55f;

        [Header("Health")]
        public int maxHealth = 100;
        [Range(0f, 1f)] public float phase2At = 0.66f;
        [Range(0f, 1f)] public float phase3At = 0.33f;
        [Tooltip("Damage applied when the weak point is hit by a collider tagged as the weapon tag (see KrakenWeakPoint).")]
        public int defaultWeaponDamage = 10;
        [Tooltip("Minimum time between two weak point hits.")]
        public float weakPointHitCooldown = 0.35f;

        [Header("Pacing (phase 1 / 2 / 3)")]
        public float[] attackCooldown = { 2.2f, 1.6f, 1.1f };
        public float[] animationSpeed = { 1f, 1.12f, 1.25f };
        public float[] stunDuration = { 6f, 5f, 4f };
        public int[] inkShots = { 1, 2, 3 };
        public int[] eruptCount = { 0, 2, 3 };
        public float summonCooldown = 12f;
        public float crossFade = 0.2f;

        [Header("Attack weights (phase 1 / 2 / 3, 0 = disabled)")]
        public float[] slamWeight = { 3f, 3f, 3f };
        public float[] sweepWeight = { 2f, 2f, 2f };
        public float[] grabWeight = { 0f, 2f, 2f };
        public float[] inkWeight = { 0.6f, 1.5f, 1.5f };
        public float[] summonWeight = { 0f, 0f, 1.5f };

        [Header("Damage to player")]
        public int slamDamage = 25;
        public int sweepDamage = 20;
        public int grabDamage = 15;
        public int throwLandDamage = 10;
        public int inkDamage = 10;
        public int inkPuddleDamage = 4;
        public int eruptDamage = 20;
        public float knockback = 7f;
        [Tooltip("Player cannot be hit twice by the kraken within this time.")]
        public float playerInvulnerability = 0.6f;

        [Header("Drowned Isle water rule")]
        public bool waterProtectsFromGrabAndInk = true;
        public bool slamIntoWaterStuns = true;
        public bool weakPointOnlyWhenStunned = true;
        [Tooltip("Erupting tentacles target the last dry-land position while the player hides in shallow water.")]
        public bool eruptAvoidsPlayerInWater = true;

        [Header("Grab")]
        public float grabRadius = 2.4f;
        public float throwDistance = 5f;
        public LayerMask groundMask = ~0;

        [Header("Prefabs (assigned by Tools > Mist Isles > Kraken > Setup)")]
        public KrakenInkBlob inkBlobPrefab;
        public KrakenInkPuddle inkPuddlePrefab;
        public KrakenEruptTentacle eruptTentaclePrefab;

        [Header("Events")]
        public UnityEvent onAwaken;
        public UnityEvent onRoar;
        public KrakenIntEvent onPhaseChanged;
        public KrakenFloatEvent onHealthChanged01;
        public UnityEvent onStunned;
        public UnityEvent onStunEnd;
        public UnityEvent onWeakPointHit;
        public UnityEvent onWeakPointBlocked;
        public KrakenIntEvent onPlayerDamaged;
        public KrakenVector3Event onSlamImpact;
        public KrakenVector3Event onInkDissolved;
        public UnityEvent onGrabMissed;
        public UnityEvent onDeathStarted;
        [Tooltip("Fired when the death animation ends. Hook mist clearing / island completion here.")]
        public UnityEvent onDefeated;

        public KrakenState State { get; private set; } = KrakenState.Dormant;
        public int Phase { get; private set; }
        public int Health { get; private set; }
        public bool PlayerInWater { get; private set; }
        public Vector3 LastDryPlayerPosition { get; private set; }
        public KrakenAttack? CurrentAttack { get; private set; }

        Transform[] tentL, tentR;
        Transform mouthSocket;
        float nextPlayerHitTime;
        float nextWeakPointHitTime;
        float lastSummonTime = -999f;
        bool stunBreak, stunHitRequested, pendingPhase;
        KrakenAttack? lastAttack, lastAttack2;

        // grab state
        bool holdingPlayer;
        CharacterController grabbedCC;
        readonly List<Behaviour> disabledBehaviours = new List<Behaviour>();

        float Scale => Mathf.Abs(transform.lossyScale.x);

        // ------------------------------------------------------------------ setup
        void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            Health = maxHealth;
            tentL = KrakenGeometry.FindChain(transform, "T_FL_", 8);
            tentR = KrakenGeometry.FindChain(transform, "T_FR_", 8);
            mouthSocket = KrakenGeometry.FindDeep(transform, "Mouth_Socket");
            if (animator != null) animator.applyRootMotion = false;
        }

        void Start()
        {
            FindPlayer();
            if (player != null) LastDryPlayerPosition = player.position;
            Play(KrakenAnimData.Hidden, 0f);
            onHealthChanged01.Invoke(1f);
            if (awakenOnStart) Awaken();
        }

        void FindPlayer()
        {
            if (player != null) return;
            var go = GameObject.FindGameObjectWithTag(playerTag);
            if (go != null) player = go.transform;
        }

        void Update()
        {
            if (player == null) { FindPlayer(); return; }
            PlayerInWater = KrakenWaterZone.AnyContainsFeet(player.position);
            if (!PlayerInWater && !holdingPlayer) LastDryPlayerPosition = player.position;

            if (State == KrakenState.Dormant && awakenRadius > 0f && HorizontalDistance(player.position) <= awakenRadius)
                Awaken();
        }

        void LateUpdate()
        {
            if (holdingPlayer && player != null && tentL != null && tentL[5] != null && tentL[7] != null)
            {
                Vector3 socket = (tentL[5].position + tentL[7].position) * 0.5f;
                var body = KrakenGeometry.GetPlayerBody(player);
                player.position = socket - (body.Center - player.position);
            }
        }

        // ------------------------------------------------------------------ public API
        /// <summary>Starts the encounter (emerge + fight). Call when the guardian is unlocked.</summary>
        public void Awaken()
        {
            if (State != KrakenState.Dormant) return;
            StartCoroutine(EncounterRoutine());
        }

        /// <summary>Called by KrakenWeakPoint. Also reachable with SendMessage("TakeDamage", int) on the boss.</summary>
        public void HandleWeakPointHit(int damage)
        {
            if (State == KrakenState.Dead) return;
            if (weakPointOnlyWhenStunned && State != KrakenState.Stunned)
            {
                onWeakPointBlocked.Invoke();
                return;
            }
            if (Time.time < nextWeakPointHitTime) return;
            nextWeakPointHitTime = Time.time + weakPointHitCooldown;

            int before = Health;
            Health = Mathf.Max(0, Health - Mathf.Max(0, damage));
            onHealthChanged01.Invoke(maxHealth > 0 ? (float)Health / maxHealth : 0f);
            onWeakPointHit.Invoke();

            if (Health <= 0)
            {
                stunBreak = true;
                if (State != KrakenState.Stunned) Die();
                return;
            }
            if (PhaseForHealth(Health) > PhaseForHealth(before))
            {
                pendingPhase = true;
                stunBreak = true;
                return;
            }
            stunHitRequested = true;
        }

        public void TakeDamage(int damage) { HandleWeakPointHit(damage); }
        public void ApplyDamage(float damage) { HandleWeakPointHit(Mathf.RoundToInt(damage)); }

        /// <summary>Debug helper: forces a stun as if a slam got stuck in water.</summary>
        [ContextMenu("Debug: Force Stun")]
        public void DebugForceStun()
        {
            if (State == KrakenState.Idle || State == KrakenState.Attacking) forceStun = true;
        }
        bool forceStun;

        // ------------------------------------------------------------------ flow
        IEnumerator EncounterRoutine()
        {
            State = KrakenState.Emerging;
            SetSpeed(0);
            onAwaken.Invoke();
            Play(KrakenAnimData.Emerge, 0f);
            yield return WaitClip(KrakenAnimData.Emerge_Roar);
            onRoar.Invoke();
            yield return WaitClip(KrakenAnimData.Emerge_End - KrakenAnimData.Emerge_Roar);
            yield return FightLoop();
        }

        IEnumerator FightLoop()
        {
            while (State != KrakenState.Dead)
            {
                if (pendingPhase)
                {
                    yield return PhaseTransition();
                    continue;
                }

                State = KrakenState.Idle;
                CurrentAttack = null;
                Play(KrakenAnimData.Idle, crossFade);
                float wait = Pick(attackCooldown);
                while (wait > 0f && !forceStun)
                {
                    wait -= Time.deltaTime;
                    if (player != null) TurnTowards(KrakenGeometry.Yaw(player.position - transform.position));
                    yield return null;
                }
                if (forceStun)
                {
                    forceStun = false;
                    yield return StunRoutine();
                    continue;
                }
                if (player == null) { yield return null; continue; }

                var attack = ChooseAttack();
                if (attack == null) { yield return null; continue; }
                CurrentAttack = attack;
                lastAttack2 = lastAttack;
                lastAttack = attack;
                State = KrakenState.Attacking;
                switch (attack.Value)
                {
                    case KrakenAttack.SlamLeft: yield return SlamRoutine(true); break;
                    case KrakenAttack.SlamRight: yield return SlamRoutine(false); break;
                    case KrakenAttack.Sweep: yield return SweepRoutine(); break;
                    case KrakenAttack.Grab: yield return GrabRoutine(); break;
                    case KrakenAttack.Ink: yield return InkRoutine(); break;
                    case KrakenAttack.Summon: yield return SummonRoutine(); break;
                }
            }
        }

        KrakenAttack? ChooseAttack()
        {
            float d = HorizontalDistance(player.position) / Scale;
            float bearing = LocalBearing(player.position);
            var options = new List<KeyValuePair<KrakenAttack, float>>();

            bool slamRange = d > 4.0f && d < 12.0f;
            if (slamRange)
                options.Add(new KeyValuePair<KrakenAttack, float>(bearing < 0f ? KrakenAttack.SlamLeft : KrakenAttack.SlamRight, Pick(slamWeight)));
            if (d > 5.0f && d < 11.0f) options.Add(new KeyValuePair<KrakenAttack, float>(KrakenAttack.Sweep, Pick(sweepWeight)));
            if (d > 6.5f && d < 10.5f) options.Add(new KeyValuePair<KrakenAttack, float>(KrakenAttack.Grab, Pick(grabWeight)));
            float ink = Pick(inkWeight) * (slamRange ? 1f : 3f);
            if (d > 3.0f) options.Add(new KeyValuePair<KrakenAttack, float>(KrakenAttack.Ink, ink));
            if (Time.time - lastSummonTime > summonCooldown && Pick(eruptCount) > 0)
                options.Add(new KeyValuePair<KrakenAttack, float>(KrakenAttack.Summon, Pick(summonWeight)));

            float total = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                float w = options[i].Value;
                if (lastAttack.HasValue && lastAttack2.HasValue && lastAttack == lastAttack2 && options[i].Key == lastAttack) w = 0f;
                options[i] = new KeyValuePair<KrakenAttack, float>(options[i].Key, Mathf.Max(0f, w));
                total += options[i].Value;
            }
            if (total <= 0f) return null;
            float r = UnityEngine.Random.value * total;
            foreach (var o in options)
            {
                r -= o.Value;
                if (r <= 0f) return o.Key;
            }
            return options[options.Count - 1].Key;
        }

        // ------------------------------------------------------------------ attacks
        IEnumerator SlamRoutine(bool left)
        {
            string clip = left ? KrakenAnimData.Attack_Slam_L : KrakenAnimData.Attack_Slam_R;
            var bones = left ? tentL : tentR;
            var impact = left ? KrakenAnimData.SlamLImpact : KrakenAnimData.SlamRImpact;
            bool hit = false, impacted = false, stuck = false;
            Play(clip, crossFade);
            float t = 0f, len = KrakenAnimData.GetLength(clip);
            while (t < len)
            {
                if (t < 0.85f && player != null)
                {
                    float d = HorizontalDistance(player.position) / Scale;
                    TurnTowards(KrakenGeometry.Yaw(player.position - transform.position) - LocalAngleAtDistance(impact, d));
                }
                if (!hit && t >= KrakenAnimData.Slam_L_HitStart && t <= KrakenAnimData.Slam_L_HitEnd && ChainHits(bones, KrakenAnimData.FrontTentacleRadii, 1))
                {
                    hit = true;
                    DamagePlayer(slamDamage, bones[5].position, Vector3.up * 0.5f + (player.position - transform.position).normalized, "Slam");
                }
                if (!impacted && t >= KrakenAnimData.Slam_L_Impact)
                {
                    impacted = true;
                    onSlamImpact.Invoke(bones[5] != null ? bones[5].position : transform.position);
                }
                if (t >= 1.3f && !stuck && (forceStun || (slamIntoWaterStuns && ChainInWater(bones))))
                {
                    stuck = true;
                    forceStun = false;
                    break;
                }
                t += Time.deltaTime * animator.speed;
                yield return null;
            }
            if (stuck) yield return StunRoutine();
        }

        IEnumerator SweepRoutine()
        {
            bool hit = false;
            Play(KrakenAnimData.Attack_Sweep, crossFade);
            float t = 0f, len = KrakenAnimData.GetLength(KrakenAnimData.Attack_Sweep);
            while (t < len)
            {
                if (t < 0.6f && player != null) TurnTowards(KrakenGeometry.Yaw(player.position - transform.position) - 20f);
                if (!hit && t >= KrakenAnimData.Sweep_HitStart && t <= KrakenAnimData.Sweep_HitEnd && ChainHits(tentR, KrakenAnimData.FrontTentacleRadii, 1))
                {
                    hit = true;
                    Vector3 side = -transform.right;
                    DamagePlayer(sweepDamage, tentR[5].position, side + Vector3.up * 0.4f, "Sweep");
                }
                t += Time.deltaTime * animator.speed;
                yield return null;
            }
        }

        IEnumerator GrabRoutine()
        {
            bool checkedGrab = false, released = false, hit = false;
            Play(KrakenAnimData.Attack_Grab, crossFade);
            float grabAngle = Mathf.Atan2(KrakenAnimData.GrabPoint.x, KrakenAnimData.GrabPoint.z) * Mathf.Rad2Deg;
            float t = 0f, len = KrakenAnimData.GetLength(KrakenAnimData.Attack_Grab);
            while (t < len && State != KrakenState.Dead)
            {
                if (t < 0.8f && player != null) TurnTowards(KrakenGeometry.Yaw(player.position - transform.position) - grabAngle);
                if (!checkedGrab && t >= KrakenAnimData.Grab_GrabCheck)
                {
                    checkedGrab = true;
                    Vector3 socket = (tentL[5].position + tentL[7].position) * 0.5f;
                    var body = KrakenGeometry.GetPlayerBody(player);
                    Vector3 delta = body.Center - socket;
                    bool inReach = new Vector2(delta.x, delta.z).magnitude <= grabRadius * Scale && Mathf.Abs(delta.y) <= 2.5f * Scale;
                    if (inReach && !(waterProtectsFromGrabAndInk && PlayerInWater))
                    {
                        hit = true;
                        DamagePlayer(grabDamage, socket, Vector3.zero, "Grab", true);
                        BeginHold();
                    }
                    else if (inReach) onGrabMissed.Invoke();
                }
                if (hit && !released && t >= KrakenAnimData.Grab_Release)
                {
                    released = true;
                    EndHold(true);
                }
                t += Time.deltaTime * animator.speed;
                yield return null;
            }
            if (holdingPlayer) EndHold(false);
        }

        IEnumerator InkRoutine()
        {
            Play(KrakenAnimData.Attack_Ink, crossFade);
            float[] shots = { KrakenAnimData.Ink_Shot1, KrakenAnimData.Ink_Shot2, KrakenAnimData.Ink_Shot3 };
            int count = Mathf.Clamp(Pick(inkShots), 1, 3);
            int fired = 0;
            float t = 0f, len = KrakenAnimData.GetLength(KrakenAnimData.Attack_Ink);
            while (t < len)
            {
                if (t < 0.9f && player != null) TurnTowards(KrakenGeometry.Yaw(player.position - transform.position));
                if (fired < count && t >= shots[fired])
                {
                    FireInk(fired);
                    fired++;
                }
                t += Time.deltaTime * animator.speed;
                yield return null;
            }
        }

        void FireInk(int index)
        {
            if (inkBlobPrefab == null || player == null) return;
            Vector3 target = player.position;
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) target += Vector3.ProjectOnPlane(cc.velocity, Vector3.up) * 0.5f;
            if (index > 0)
            {
                Vector3 side = Vector3.Cross(Vector3.up, (target - transform.position).normalized);
                target += side * (index % 2 == 1 ? 1.8f : -1.8f) + UnityEngine.Random.insideUnitSphere.WithY(0f) * 0.6f;
            }
            Vector3 from = mouthSocket != null ? mouthSocket.position : transform.position + Vector3.up * 3f;
            var blob = Instantiate(inkBlobPrefab, from, Quaternion.identity);
            float flight = 0.9f + 0.03f * Vector3.Distance(from, target);
            blob.Launch(this, target, flight, 3.5f * Scale, inkDamage, inkPuddleDamage);
        }

        IEnumerator SummonRoutine()
        {
            lastSummonTime = Time.time;
            Play(KrakenAnimData.Attack_Summon, crossFade);
            int count = Mathf.Max(0, Pick(eruptCount));
            float span = KrakenAnimData.Summon_SpawnEnd - KrakenAnimData.Summon_SpawnStart - 1.0f;
            int spawned = 0;
            float t = 0f, len = KrakenAnimData.GetLength(KrakenAnimData.Attack_Summon);
            while (t < len)
            {
                float next = KrakenAnimData.Summon_SpawnStart + (count > 1 ? span * spawned / (count - 1) : 0f);
                if (spawned < count && t >= next)
                {
                    SpawnErupt(spawned);
                    spawned++;
                }
                t += Time.deltaTime * animator.speed;
                yield return null;
            }
        }

        void SpawnErupt(int index)
        {
            if (eruptTentaclePrefab == null || player == null) return;
            Vector3 target = (eruptAvoidsPlayerInWater && PlayerInWater) ? LastDryPlayerPosition : player.position;
            if (index > 0) target += UnityEngine.Random.insideUnitSphere.WithY(0f) * 1.2f;
            Vector3 dir = target - transform.position;
            dir.y = 0f;
            var rot = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-dir.normalized) : Quaternion.identity;
            var e = Instantiate(eruptTentaclePrefab, target, rot);
            e.Init(this, eruptDamage);
        }

        // ------------------------------------------------------------------ stun / phases / death
        IEnumerator StunRoutine()
        {
            State = KrakenState.Stunned;
            stunBreak = false;
            stunHitRequested = false;
            onStunned.Invoke();
            Play(KrakenAnimData.Stunned_Enter, 0.25f);
            yield return WaitClip(KrakenAnimData.GetLength(KrakenAnimData.Stunned_Enter));
            Play(KrakenAnimData.Stunned_Loop, 0.15f);
            float remaining = Pick(stunDuration);
            while (remaining > 0f && !stunBreak)
            {
                if (stunHitRequested)
                {
                    stunHitRequested = false;
                    Play(KrakenAnimData.Stunned_Hit, 0.05f);
                    float h = 0f;
                    while (h < KrakenAnimData.GetLength(KrakenAnimData.Stunned_Hit) && !stunBreak)
                    {
                        h += Time.deltaTime * animator.speed;
                        remaining -= Time.deltaTime;
                        yield return null;
                    }
                    if (!stunBreak) Play(KrakenAnimData.Stunned_Loop, 0.15f);
                    continue;
                }
                remaining -= Time.deltaTime;
                yield return null;
            }
            if (Health <= 0)
            {
                yield return DeathRoutine();
                yield break;
            }
            Play(KrakenAnimData.Stunned_Exit, 0.2f);
            yield return WaitClip(KrakenAnimData.GetLength(KrakenAnimData.Stunned_Exit));
            State = KrakenState.Idle;
            onStunEnd.Invoke();
        }

        IEnumerator PhaseTransition()
        {
            pendingPhase = false;
            State = KrakenState.PhaseTransition;
            Phase = PhaseForHealth(Health);
            SetSpeed(Phase);
            onPhaseChanged.Invoke(Phase + 1);
            Play(KrakenAnimData.Roar, crossFade);
            yield return WaitClip(KrakenAnimData.Roar_Roar);
            onRoar.Invoke();
            yield return WaitClip(KrakenAnimData.GetLength(KrakenAnimData.Roar) - KrakenAnimData.Roar_Roar);
        }

        /// <summary>Kills the kraken immediately (interrupts any attack).</summary>
        [ContextMenu("Debug: Kill")]
        public void Die()
        {
            if (State == KrakenState.Dead) return;
            StopAllCoroutines();
            holdingPlayer = false;
            RestorePlayer();
            StartCoroutine(DeathRoutine());
        }

        IEnumerator DeathRoutine()
        {
            if (State == KrakenState.Dead) yield break;
            State = KrakenState.Dead;
            if (holdingPlayer) EndHold(false);
            onDeathStarted.Invoke();
            SetSpeed(0);
            Play(KrakenAnimData.Death, 0.25f);
            yield return WaitClip(KrakenAnimData.GetLength(KrakenAnimData.Death));
            onDefeated.Invoke();
        }

        int PhaseForHealth(int hp)
        {
            float f = maxHealth > 0 ? (float)hp / maxHealth : 0f;
            if (f <= phase3At) return 2;
            if (f <= phase2At) return 1;
            return 0;
        }

        // ------------------------------------------------------------------ player damage
        /// <summary>Deals damage to the player (respects the shared invulnerability window).</summary>
        public void DamagePlayer(int amount, Vector3 point, Vector3 pushDir, string source, bool ignoreInvulnerability = false)
        {
            if (player == null || amount <= 0) return;
            if (!ignoreInvulnerability && Time.time < nextPlayerHitTime) return;
            nextPlayerHitTime = Time.time + playerInvulnerability;

            var info = new KrakenHitInfo { damage = amount, point = point, direction = pushDir, source = source };
            var receiver = player.GetComponentInParent<IKrakenDamageable>();
            if (receiver != null) receiver.TakeKrakenDamage(info);
            else player.SendMessage("TakeDamage", amount, SendMessageOptions.DontRequireReceiver);
            onPlayerDamaged.Invoke(amount);

            if (knockback > 0f && pushDir.sqrMagnitude > 0.001f && !holdingPlayer)
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) StartCoroutine(Knockback(cc, pushDir.normalized * knockback));
            }
        }

        IEnumerator Knockback(CharacterController cc, Vector3 velocity)
        {
            float t = 0f;
            while (t < 0.3f && cc != null && cc.enabled)
            {
                cc.Move(velocity * (1f - t / 0.3f) * Time.deltaTime);
                t += Time.deltaTime;
                yield return null;
            }
        }

        public bool PlayerWithin(Vector3 point, float radius)
        {
            if (player == null) return false;
            var b = KrakenGeometry.GetPlayerBody(player);
            float r = radius + b.radius;
            return KrakenGeometry.SegmentSegmentSqr(point, point, b.bottom, b.top) <= r * r;
        }

        public bool ChainHits(Transform[] bones, float[] radii, int fromIndex = 0)
        {
            if (player == null || holdingPlayer) return false;
            return KrakenGeometry.ChainHitsPlayer(bones, radii, Scale, KrakenGeometry.GetPlayerBody(player), fromIndex);
        }

        public void NotifyInkDissolved(Vector3 p) { onInkDissolved.Invoke(p); }

        bool ChainInWater(Transform[] bones)
        {
            int n = 0;
            for (int i = 3; i < bones.Length; i++)
                if (bones[i] != null && KrakenWaterZone.AnyContainsXZ(bones[i].position)) n++;
            return n >= 2;
        }

        // ------------------------------------------------------------------ grab
        void BeginHold()
        {
            holdingPlayer = true;
            disabledBehaviours.Clear();
            grabbedCC = player.GetComponent<CharacterController>();
            if (grabbedCC != null) grabbedCC.enabled = false;
            var tpc = player.GetComponent("ThirdPersonController") as Behaviour;
            if (tpc != null && tpc.enabled) { tpc.enabled = false; disabledBehaviours.Add(tpc); }
            if (disableOnPlayerWhileGrabbed != null)
                foreach (var b in disableOnPlayerWhileGrabbed)
                    if (b != null && b.enabled) { b.enabled = false; disabledBehaviours.Add(b); }
        }

        void EndHold(bool thrown)
        {
            holdingPlayer = false;
            if (thrown && player != null) StartCoroutine(ThrowPlayer());
            else RestorePlayer();
        }

        IEnumerator ThrowPlayer()
        {
            Vector3 start = player.position;
            Vector3 away = player.position - transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward;
            Vector3 land = start + away * throwDistance * Scale;
            land = GroundAt(land, start.y);
            float t = 0f, dur = 0.7f;
            while (t < dur && player != null)
            {
                float u = t / dur;
                player.position = Vector3.Lerp(start, land, u) + Vector3.up * (4f * 2.5f * u * (1f - u));
                t += Time.deltaTime;
                yield return null;
            }
            if (player != null) player.position = land;
            RestorePlayer();
            DamagePlayer(throwLandDamage, land, Vector3.zero, "GrabThrow", true);
        }

        void RestorePlayer()
        {
            if (grabbedCC != null) grabbedCC.enabled = true;
            grabbedCC = null;
            foreach (var b in disabledBehaviours) if (b != null) b.enabled = true;
            disabledBehaviours.Clear();
        }

        Vector3 GroundAt(Vector3 p, float fallbackY)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 80f, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 result = new Vector3(p.x, fallbackY, p.z);
            foreach (var h in hits)
            {
                if (player != null && h.transform.IsChildOf(player)) continue;
                if (h.transform.IsChildOf(transform)) continue;
                if (h.distance < best) { best = h.distance; result = h.point; }
            }
            return result;
        }

        // ------------------------------------------------------------------ helpers
        void Play(string state, float fade)
        {
            if (animator == null) return;
            if (fade <= 0f) animator.Play(state, 0, 0f);
            else animator.CrossFadeInFixedTime(state, fade, 0, 0f);
        }

        IEnumerator WaitClip(float clipSeconds)
        {
            float t = 0f;
            while (t < clipSeconds)
            {
                t += Time.deltaTime * (animator != null ? animator.speed : 1f);
                yield return null;
            }
        }

        void SetSpeed(int phase)
        {
            if (animator != null) animator.speed = Pick(animationSpeed, phase);
        }

        float Pick(float[] arr) { return Pick(arr, Phase); }
        int Pick(int[] arr) { return arr == null || arr.Length == 0 ? 0 : arr[Mathf.Clamp(Phase, 0, arr.Length - 1)]; }
        static float Pick(float[] arr, int phase) { return arr == null || arr.Length == 0 ? 1f : arr[Mathf.Clamp(phase, 0, arr.Length - 1)]; }

        void TurnTowards(float yaw)
        {
            var e = transform.eulerAngles;
            float y = Mathf.MoveTowardsAngle(e.y, yaw, turnSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(e.x, y, e.z);
        }

        float HorizontalDistance(Vector3 p)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        /// <summary>Angle of p relative to the kraken's forward (negative = kraken's left).</summary>
        float LocalBearing(Vector3 p)
        {
            Vector3 l = transform.InverseTransformPoint(p);
            return Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
        }

        /// <summary>Local angle of the slam polyline at horizontal distance d (local units).</summary>
        static float LocalAngleAtDistance(Vector3[] pts, float d)
        {
            float prevD = Flat(pts[0]);
            if (d <= prevD) return AngleOf(pts[0]);
            for (int i = 1; i < pts.Length; i++)
            {
                float di = Flat(pts[i]);
                if (d <= di)
                {
                    float u = Mathf.InverseLerp(prevD, di, d);
                    return AngleOf(Vector3.Lerp(pts[i - 1], pts[i], u));
                }
                prevD = di;
            }
            return AngleOf(pts[pts.Length - 1]);
        }

        static float Flat(Vector3 p) { return new Vector2(p.x, p.z).magnitude; }
        static float AngleOf(Vector3 p) { return Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg; }

        // ------------------------------------------------------------------ gizmos
        void OnDrawGizmosSelected()
        {
            var m = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one * Scale);
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
            DrawPolyline(KrakenAnimData.SlamLImpact);
            DrawPolyline(KrakenAnimData.SlamRImpact);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            DrawPolyline(KrakenAnimData.SweepTipArc);
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(KrakenAnimData.GrabPoint, grabRadius);
            Gizmos.color = new Color(0.4f, 0.95f, 1f, 0.9f);
            Gizmos.DrawWireSphere(KrakenAnimData.StunnedWeakPoint, KrakenAnimData.WeakPointRadius);
            DrawCircle(4f);
            DrawCircle(12f);
            Gizmos.matrix = m;
        }

        static void DrawPolyline(Vector3[] pts)
        {
            for (int i = 1; i < pts.Length; i++) Gizmos.DrawLine(pts[i - 1], pts[i]);
        }

        static void DrawCircle(float r)
        {
            Vector3 prev = new Vector3(0f, 0.05f, r);
            for (int i = 1; i <= 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Sin(a) * r, 0.05f, Mathf.Cos(a) * r);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }

    static class KrakenVectorExt
    {
        public static Vector3 WithY(this Vector3 v, float y) { v.y = y; return v; }
    }
}
