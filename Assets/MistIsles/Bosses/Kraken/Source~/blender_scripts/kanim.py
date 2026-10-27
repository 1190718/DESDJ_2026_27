"""Procedural animation for the kraken. Every clip is a function t(seconds) -> pose, baked per frame."""
import math
from kmodel import TENT_ORDER, NB

FPS = 30
TAU = 2 * math.pi
W = TAU / 4.0  # idle period 4 s


# ---------------------------------------------------------------- easing / helpers
def cl(u):
    return max(0.0, min(1.0, u))


EASE = {
    "lin": lambda u: u,
    "smooth": lambda u: u * u * (3 - 2 * u),
    "in": lambda u: u ** 3,
    "out": lambda u: 1 - (1 - u) ** 3,
    "in2": lambda u: u * u,
    "out2": lambda u: 1 - (1 - u) ** 2,
    "back": lambda u: 1 + 2.4 * (u - 1) ** 3 + 1.4 * (u - 1) ** 2,
    "bounce": lambda u: 1 - abs(math.cos(u * math.pi * 1.5)) * (1 - u) if u < 1 else 1.0,
}


def lerp(a, b, u):
    if isinstance(a, dict):
        keys = set(a) | set(b)
        return {k: lerp(a.get(k, 0.0), b.get(k, 0.0), u) for k in keys}
    if isinstance(a, list):
        return [x + (y - x) * u for x, y in zip(a, b)]
    return a + (b - a) * u


def sample(keys, t):
    """keys: [(time, pose_or_fn(t), ease)] ; ease applies to the segment ending at that key."""
    def res(p):
        return p(t) if callable(p) else p
    if t <= keys[0][0]:
        return res(keys[0][1])
    for (t0, p0, _), (t1, p1, e) in zip(keys, keys[1:]):
        if t <= t1:
            u = EASE[e](cl((t - t0) / (t1 - t0)))
            return lerp(res(p0), res(p1), u)
    return res(keys[-1][1])


def win(t, t0, t1):
    """1 inside [t0,t1] with short ramps (envelope)."""
    r = 0.12
    return cl((t - t0) / r) * cl((t1 - t) / r)


# ---------------------------------------------------------------- idle
PHASE = {n: i * 1.37 for i, n in enumerate(TENT_ORDER)}
S_CURVE = [14, 4, -8, -10, -3, 16, 26, 32]


def T(bend, side=None, twist=0.0, lift=0.0):
    return {"bend": list(bend), "side": list(side or [0.0] * NB), "twist": twist, "lift": lift}


def idle_tent(n, t):
    ph = PHASE[n]
    bend = [S_CURVE[i] + (3 + 2.2 * i) * math.sin(W * t + ph - 0.55 * i) for i in range(NB)]
    side = [(1.5 + 1.5 * i) * math.sin(W * t + ph * 1.3 + 1.0 - 0.4 * i) for i in range(NB)]
    return T(bend, side, 6 * math.sin(W * t + ph), 0.15 * math.sin(W * t + ph + 0.5))


def blink(t):
    tt = t % 4.0
    if 2.5 <= tt <= 2.74:
        return 1 - 0.92 * math.sin(math.pi * (tt - 2.5) / 0.24)
    return 1.0


BODY0 = dict(body_up=0, body_fwd=0, root_up=0, root_fwd=0, root_x=0, root_pitch=0, root_yaw=0, root_roll=0, base_p=0, mid_p=0, top_p=0,
             top_s=1, jaw=0, eye_sz=1, eye_s=1, eye_spin=0, eye_look=0, core_s=1)


def idle_body(t):
    b = dict(BODY0)
    b.update(root_up=0.22 * math.sin(W * t), base_p=1.5 * math.sin(W * t + 0.6), mid_p=2.5 * math.sin(W * t + 1.0),
             top_p=3.5 * math.sin(W * t + 1.4), root_roll=1.2 * math.sin(W * t + 2), top_s=1 + 0.025 * math.sin(2 * W * t),
             jaw=4 + 3 * math.sin(W * t + 0.3), eye_sz=blink(t), eye_look=6 * math.sin(W * t + 0.4),
             core_s=1 + 0.07 * math.sin(2 * W * t))
    return b


def add(a, d):
    o = dict(a)
    for k, v in d.items():
        o[k] = o.get(k, 0.0) + v
    return o


def tent_seq(n, t, keys, delay=0.02):
    """Per-bone time delay base->tip for follow-through."""
    out = T([0] * NB)
    p0 = sample(keys, t)
    out["twist"], out["lift"] = p0["twist"], p0["lift"]
    for i in range(NB):
        p = sample(keys, t - i * delay) if i else p0
        out["bend"][i] = p["bend"][i]
        out["side"][i] = p["side"][i]
    return out


def idle_fn(n):
    return lambda t: idle_tent(n, t)


def with_wiggle(pose, t, amp=4.0, freq=6.0, bones=range(NB), ph=0.0):
    p = {"bend": list(pose["bend"]), "side": list(pose["side"]), "twist": pose["twist"], "lift": pose["lift"]}
    for i in bones:
        p["bend"][i] += amp * math.sin(TAU * freq * t + ph + i * 0.8)
        p["side"][i] += amp * 0.6 * math.cos(TAU * freq * 0.7 * t + ph + i)
    return p


def mod(pose, **kw):
    p = {"bend": list(pose["bend"]), "side": list(pose["side"]), "twist": pose["twist"], "lift": pose["lift"]}
    p.update(kw)
    return p


# ---------------------------------------------------------------- key poses
HEAD = {"FL": -55.0, "FR": -125.0}


def aim(n, heading):
    return heading - HEAD[n]


SLAM = [0, 48, 42, 5, 5, 4, 0, -4]
WIND = [-5, -10, -10, -8, 0, 15, 25, 30]
SPREAD = [22, 4, -6, -14, -10, 12, 22, 30]
LIMP_GROUND = [0, 45, 40, 6, 4, 2, -2, -6]
LIMP_WATER = [42, 30, 12, 6, 5, 5, 5, 5]
HIDDEN_T = T([20] * NB, lift=-9.5)


# ---------------------------------------------------------------- clips
CLIPS = {}
EVENTS = {}


def clip(name, dur, loop=False, events=None):
    def deco(fn):
        CLIPS[name] = (fn, dur, loop)
        EVENTS[name] = events or {}
        return fn
    return deco


def full(t, body_delta=None, tents=None, body_abs=None):
    b = body_abs if body_abs is not None else add(idle_body(t), body_delta or {})
    ts = {n: idle_tent(n, t) for n in TENT_ORDER}
    ts.update(tents or {})
    return b, ts


@clip("Idle", 4.0, loop=True)
def c_idle(t):
    return full(t)


HIDDEN_BODY = add(BODY0, dict(body_up=-9.8, eye_sz=-0.9, jaw=0))


@clip("Hidden", 1.0, loop=True)
def c_hidden(t):
    return HIDDEN_BODY, {n: HIDDEN_T for n in TENT_ORDER}


@clip("Emerge", 4.0, events={"splash": 0.25, "bodySurface": 1.2, "roar": 2.4, "end": 4.0})
def c_emerge(t):
    order = ["BBL", "BBR", "BL", "BR", "SL", "SR", "FL", "FR"]
    ts = {}
    for k, n in enumerate(order):
        t0 = 0.15 + k * 0.1
        keys = [(t0, HIDDEN_T, "lin"), (t0 + 0.5, T(SPREAD, lift=0.8), "back"), (2.6, T(SPREAD, lift=0.8), "lin"),
                (3.8, idle_fn(n), "smooth")]
        ts[n] = tent_seq(n, t, keys, 0.03)
    body = sample([(0.0, HIDDEN_BODY, "lin"), (1.0, HIDDEN_BODY, "lin"),
                   (2.2, add(BODY0, dict(body_up=0.7, base_p=-4, mid_p=-6, top_p=-6, eye_sz=-0.9, jaw=8)), "back"),
                   (2.5, add(BODY0, dict(body_up=0.6, base_p=-6, mid_p=-10, top_p=-12, eye_s=0.15, jaw=45)), "out"),
                   (3.2, add(BODY0, dict(body_up=0.6, base_p=-6, mid_p=-10, top_p=-12, eye_s=0.15, jaw=45)), "lin"),
                   (4.0, idle_body, "smooth")], t)
    if 2.5 < t < 3.2:
        body = add(body, dict(top_p=2.0 * math.sin(TAU * 9 * t), root_roll=1.5 * math.sin(TAU * 7 * t)))
    return body, ts


@clip("Roar", 3.0, events={"roar": 0.6, "end": 3.0})
def c_roar(t):
    rear = dict(body_up=0.8, base_p=-6, mid_p=-10, top_p=-12, jaw=48, eye_s=0.18)
    bd = sample([(0, {}, "lin"), (0.6, rear, "back"), (2.2, rear, "lin"), (3.0, {}, "smooth")], t)
    e = win(t, 0.6, 2.2)
    bd = add(bd, dict(top_p=2.5 * e * math.sin(TAU * 9 * t), mid_p=1.5 * e * math.sin(TAU * 9 * t + 1),
                      root_roll=2.0 * e * math.sin(TAU * 6 * t)))
    ts = {}
    for n in TENT_ORDER:
        tp = T(SPREAD, lift=0.9)
        keys = [(0, idle_fn(n), "lin"), (0.6, tp, "back"), (2.2, tp, "lin"), (3.0, idle_fn(n), "smooth")]
        p = tent_seq(n, t, keys, 0.03)
        if e > 0:
            p = with_wiggle(p, t, 6 * e, 7, ph=PHASE[n])
        ts[n] = p
    return full(t, bd, ts)


def slam_clip(n, sign):
    a = aim(n, -90 + 12 * sign)

    def fn(t):
        wind = T([10, 16, 14, 8, -6, -22, -32, -38], twist=105 * sign, lift=2.0)
        slam = T(SLAM, twist=a, lift=1.0)
        squash = T([0, 50, 44, 8, 8, 7, 3, -2], twist=a, lift=0.85)
        stuck = lambda tt: with_wiggle(slam, tt, 2.5, 3, bones=range(4, NB))
        keys = [(0, idle_fn(n), "lin"), (0.85, wind, "smooth"), (0.95, mod(wind, lift=2.2, bend=[12, 19, 16, 9, -8, -26, -36, -42]), "smooth"),
                (1.12, squash, "in"), (1.3, slam, "out"), (1.9, stuck, "lin"), (2.4, idle_fn(n), "smooth")]
        tp = tent_seq(n, t, keys, 0.018)
        bd = sample([(0, {}, "lin"), (0.85, dict(body_up=0.3, base_p=-4, mid_p=-6, top_p=-8, root_yaw=8 * sign, jaw=6), "smooth"),
                     (1.12, dict(body_up=-0.2, base_p=6, mid_p=8, top_p=10, root_yaw=4 * sign, jaw=36, eye_s=0.1), "in"),
                     (1.9, dict(body_up=-0.1, base_p=4, mid_p=5, top_p=6, root_yaw=4 * sign, jaw=14), "smooth"),
                     (2.4, {}, "smooth")], t)
        return full(t, bd, {n: tp})
    return fn


CLIPS_EVENTS_SLAM = {"windup": 0.0, "hitStart": 1.0, "impact": 1.12, "hitEnd": 1.35, "recoverStart": 1.9, "end": 2.4}
clip("Attack_Slam_L", 2.4, events=CLIPS_EVENTS_SLAM)(slam_clip("FL", +1))
clip("Attack_Slam_R", 2.4, events=CLIPS_EVENTS_SLAM)(slam_clip("FR", -1))


@clip("Attack_Sweep", 2.8, events={"hitStart": 0.72, "hitEnd": 1.55, "end": 2.8})
def c_sweep(t):
    n = "FR"
    a0, a1 = aim(n, -160), aim(n, -35)
    low = [0, 50, 40, 0, -2, -2, 0, 5]
    coil = [0, 0, 0, 10, 15, 20, 20, 20]
    tw = sample([(0, 6 * math.sin(W * t + PHASE[n]), "lin"), (0.7, a0, "smooth"), (1.45, a1, "smooth"),
                 (1.62, a1 + 10, "out"), (1.8, a1, "smooth")], t)
    # twist velocity for drag
    dt = 1 / 120
    tw2 = sample([(0, 6 * math.sin(W * (t + dt) + PHASE[n]), "lin"), (0.7, a0, "smooth"), (1.45, a1, "smooth"),
                  (1.62, a1 + 10, "out"), (1.8, a1, "smooth")], t + dt)
    vel = (tw2 - tw) / dt
    keys = [(0, idle_fn(n), "lin"), (0.7, T(low, coil, a0, 0.45), "smooth"), (1.45, T(low, None, a1, 0.45), "smooth"),
            (1.8, T(low, None, a1, 0.45), "lin"), (2.8, idle_fn(n), "smooth")]
    tp = tent_seq(n, t, keys, 0.02)
    if t < 1.8:
        tp["twist"] = tw
    else:
        tp["twist"] = lerp(a1, idle_tent(n, t)["twist"], EASE["smooth"](cl((t - 1.8) / 1.0)))
    for i in range(NB):
        tp["side"][i] += max(-35, min(35, 0.11 * vel * (i / (NB - 1))))
    bd = sample([(0, {}, "lin"), (0.7, dict(root_yaw=-12, base_p=3, mid_p=4, top_p=2, jaw=10), "smooth"),
                 (1.45, dict(root_yaw=12, base_p=5, mid_p=6, top_p=4, jaw=30), "smooth"),
                 (1.8, dict(root_yaw=10, base_p=3, mid_p=3, top_p=2, jaw=12), "smooth"), (2.8, {}, "smooth")], t)
    return full(t, bd, {n: tp})


@clip("Attack_Grab", 3.4, events={"lunge": 0.55, "grabCheck": 1.15, "release": 2.68, "end": 3.4})
def c_grab(t):
    n = "FL"
    a = aim(n, -84)
    raise_ = T([-4, -8, -6, -4, 4, 18, 26, 30], twist=a, lift=1.5)
    reach = T([0, 40, 45, 6, 0, 0, -6, -10], twist=a, lift=2.0)
    coil = T([0, 40, 45, 6, 10, 60, 70, 70], twist=a, lift=2.0)
    lift = T([0, 15, 5, -20, -25, 60, 70, 70], twist=a, lift=2.2)
    back = T([-5, -15, -20, -15, 0, 60, 70, 70], twist=a, lift=2.2)
    throw = T([0, 35, 30, 10, 5, 0, -10, -10], twist=a, lift=1.1)
    keys = [(0, idle_fn(n), "lin"), (0.55, raise_, "smooth"), (0.95, reach, "in2"), (1.25, coil, "out"),
            (1.9, lift, "smooth"), (2.35, lambda tt: with_wiggle(lift, tt, 4, 6, range(2, 5)), "lin"),
            (2.6, back, "smooth"), (2.8, throw, "in"), (3.4, idle_fn(n), "smooth")]
    tp = tent_seq(n, t, keys, 0.02)
    bd = sample([(0, {}, "lin"), (0.55, dict(body_up=0.2, base_p=-3, top_p=-4, root_yaw=6, jaw=8), "smooth"),
                 (0.95, dict(base_p=5, mid_p=6, top_p=5, root_yaw=4, jaw=20), "smooth"),
                 (1.9, dict(body_up=0.3, base_p=-2, top_p=-3, root_yaw=6, jaw=28, eye_s=0.1), "smooth"),
                 (2.6, dict(body_up=0.3, base_p=-5, mid_p=-6, top_p=-8, root_yaw=10, jaw=12), "smooth"),
                 (2.8, dict(base_p=6, mid_p=8, top_p=8, root_yaw=0, jaw=40), "in"), (3.4, {}, "smooth")], t)
    return full(t, bd, {n: tp})


@clip("Attack_Ink", 2.6, events={"shot1": 1.0, "shot2": 1.25, "shot3": 1.5, "end": 2.6})
def c_ink(t):
    inhale = dict(body_up=0.4, base_p=-8, mid_p=-8, top_p=-6, top_s=0.1, jaw=10, eye_s=0.05)
    spit = dict(body_up=-0.1, base_p=10, mid_p=8, top_p=6, top_s=-0.06, jaw=50, eye_s=0.12)
    bd = sample([(0, {}, "lin"), (0.9, inhale, "smooth"), (1.0, spit, "in"), (1.6, spit, "lin"), (2.6, {}, "smooth")], t)
    for tp_ in (1.25, 1.5):
        k = math.exp(-((t - tp_) / 0.06) ** 2)
        bd = add(bd, dict(jaw=12 * k, base_p=3 * k, top_s=-0.03 * k))
    ts = {}
    for n in ("FL", "FR", "SL", "SR"):
        sp = lambda tt, n=n: mod(idle_tent(n, tt), bend=[b + (14 if i < 2 else 4) for i, b in enumerate(idle_tent(n, tt)["bend"])])
        ts[n] = sample([(0, idle_fn(n), "lin"), (0.9, sp, "smooth"), (1.6, sp, "lin"), (2.6, idle_fn(n), "smooth")], t)
    return full(t, bd, ts)


@clip("Attack_Summon", 5.0, events={"submerged": 0.8, "spawnStart": 1.0, "spawnEnd": 3.8, "rise": 4.0, "end": 5.0})
def c_summon(t):
    low = dict(body_up=-1.9, base_p=4, jaw=0)
    bd = sample([(0, {}, "lin"), (0.8, low, "in2"), (4.0, low, "lin"), (5.0, {}, "back")], t)
    e = win(t, 0.9, 3.9)
    bd = add(bd, dict(eye_look=22 * e * math.sin(TAU * t / 1.2)))
    ts = {}
    for k, n in enumerate(TENT_ORDER):
        dive = T([25] * NB, lift=-7.0)
        keys = [(0, idle_fn(n), "lin"), (0.75 + 0.03 * k, dive, "in2"), (4.0, dive, "lin"), (4.9, idle_fn(n), "back")]
        ts[n] = tent_seq(n, t, keys, 0.03)
    return full(t, bd, ts)


STUN_BODY = dict(body_up=-0.5, body_fwd=2.2, base_p=34, mid_p=12, top_p=6, jaw=16, eye_sz=-0.55)


def stun_tents(t):
    ts = {}
    for n in TENT_ORDER:
        if n in ("FL", "FR"):
            a = aim(n, -90 + (14 if n == "FL" else -14))
            p = T(LIMP_GROUND, twist=a, lift=0.55)
        else:
            p = T(LIMP_WATER, lift=-0.7)
        ts[n] = with_wiggle(p, t, 2.0, 1.0, range(4, NB), PHASE[n])
    return ts


def stun_body_abs(t, extra=None):
    b = add(BODY0, STUN_BODY)
    b = add(b, dict(body_up=0.08 * math.sin(TAU * t / 2), top_s=0.03 * math.sin(TAU * t / 1.0),
                    core_s=0.18 * math.sin(TAU * t / 1.0), eye_spin=360 * t / 2.0, jaw=4 * math.sin(TAU * t / 2)))
    return add(b, extra or {})


@clip("Stunned_Enter", 1.4, events={"groundHit": 0.9, "end": 1.4})
def c_stun_enter(t):
    target = lambda tt: stun_body_abs(tt)
    body = sample([(0, idle_body, "lin"), (0.9, target, "in2"), (1.05, lambda tt: add(stun_body_abs(tt), dict(body_up=0.35, base_p=-5)), "out"),
                   (1.4, target, "in2")], t)
    st = stun_tents(t)
    ts = {}
    for n in TENT_ORDER:
        ts[n] = tent_seq(n, t, [(0, idle_fn(n), "lin"), (0.95, lambda tt, n=n: stun_tents(tt)[n], "in2"),
                                (1.1, lambda tt, n=n: mod(stun_tents(tt)[n], lift=stun_tents(tt)[n]["lift"] + 0.3), "out"),
                                (1.4, lambda tt, n=n: stun_tents(tt)[n], "in2")], 0.02)
    return body, ts


@clip("Stunned_Loop", 2.0, loop=True)
def c_stun_loop(t):
    return stun_body_abs(t), stun_tents(t)


@clip("Stunned_Hit", 0.6, events={"end": 0.6})
def c_stun_hit(t):
    k = sample([(0, 0.0, "lin"), (0.07, 1.0, "out"), (0.6, 0.0, "smooth")], t)
    return stun_body_abs(t, dict(base_p=-9 * k, mid_p=-3 * k, eye_sz=-0.35 * k, jaw=26 * k, core_s=-0.25 * k)), stun_tents(t)


@clip("Stunned_Exit", 1.3, events={"end": 1.3})
def c_stun_exit(t):
    shake = win(t, 0.25, 0.7)
    body = sample([(0, lambda tt: stun_body_abs(tt), "lin"),
                   (0.3, lambda tt: stun_body_abs(tt, dict(body_up=0.2, eye_sz=0.5, jaw=20)), "smooth"),
                   (1.3, idle_body, "back")], t)
    body = add(body, dict(top_p=3 * shake * math.sin(TAU * 10 * t), root_roll=2 * shake * math.sin(TAU * 8 * t)))
    ts = {n: tent_seq(n, t, [(0, lambda tt, n=n: stun_tents(tt)[n], "lin"), (0.3, lambda tt, n=n: stun_tents(tt)[n], "lin"),
                             (1.25, idle_fn(n), "back")], 0.03) for n in TENT_ORDER}
    return body, ts


@clip("Death", 6.0, events={"shriek": 0.1, "collapse": 1.2, "coreFade": 2.0, "sink": 2.4, "end": 6.0})
def c_death(t):
    rear = dict(root_up=0.5, base_p=-8, mid_p=-12, top_p=-14, jaw=50, eye_s=0.2)
    slump = dict(root_up=-1.0, base_p=12, mid_p=8, top_p=6, jaw=25, eye_sz=-0.92, root_roll=6)
    sink = dict(root_up=-10.5, base_p=16, mid_p=10, top_p=8, jaw=25, eye_sz=-0.92, root_roll=15, root_yaw=20)
    bd = sample([(0, {}, "lin"), (0.25, rear, "out"), (1.2, rear, "lin"), (2.4, slump, "smooth"), (6.0, sink, "in2")], t)
    fl = win(t, 0.15, 1.3)
    bd = add(bd, dict(top_p=4 * fl * math.sin(TAU * 8 * t), root_roll=3 * fl * math.sin(TAU * 5 * t)))
    bd["core_s"] = -sample([(0, 0.0, "lin"), (2.0, 0.0, "lin"), (3.0, 1.0, "in2")], t)
    body = add(BODY0, bd)
    body["eye_look"] = 0
    ts = {}
    for n in TENT_ORDER:
        flail = lambda tt, n=n: with_wiggle(T(SPREAD, lift=0.8), tt, 14 * (1 - 0.6 * cl((tt - 0.2) / 1.0)), 4.5, ph=PHASE[n])
        keys = [(0, idle_fn(n), "lin"), (0.25, flail, "out"), (1.2, flail, "lin"), (2.4, T(LIMP_WATER, lift=-0.8), "smooth"),
                (6.0, T(LIMP_WATER, lift=-9.0), "in2")]
        ts[n] = tent_seq(n, t, keys, 0.03)
    return body, ts


# ---------------------------------------------------------------- pose -> bone channels
def to_channels(body, tents):
    """Returns dict bone -> (rot_deg xyz, loc xyz, scale xyz)."""
    b = body
    ch = {}
    ch["Root"] = ((b["root_pitch"], b["root_yaw"], b["root_roll"]), (b["root_x"], b["root_up"], b["root_fwd"]), (1, 1, 1))
    ch["Body_Base"] = ((b["base_p"], 0, 0), (0, b["body_up"], b["body_fwd"]), (1, 1, 1))
    ch["Body_Mid"] = ((b["mid_p"], 0, 0), (0, 0, 0), (1, 1, 1))
    ts_ = b["top_s"]
    ch["Body_Top"] = ((b["top_p"], 0, 0), (0, 0, 0), (ts_, ts_, ts_))
    es = b["eye_s"]
    ez = max(0.05, min(2.0, b["eye_sz"]))
    ch["Eye_L"] = ((0, b["eye_spin"], b["eye_look"]), (0, 0, 0), (es, es, es * ez))
    ch["Eye_R"] = ((0, -b["eye_spin"], b["eye_look"]), (0, 0, 0), (es, es, es * ez))
    ch["Jaw"] = ((-max(-5, b["jaw"]), 0, 0), (0, 0, 0), (1, 1, 1))
    cs = max(0.001, b["core_s"])
    ch["WeakPoint"] = ((0, 0, 0), (0, 0, 0), (cs, cs, cs))
    ch["Mouth_Socket"] = ((0, 0, 0), (0, 0, 0), (1, 1, 1))
    for n, p in tents.items():
        for i in range(NB):
            rot = (p["bend"][i], p["twist"] if i == 0 else 0.0, p["side"][i])
            loc = (0, p["lift"], 0) if i == 0 else (0, 0, 0)
            ch[f"T_{n}_{i:02d}"] = (rot, loc, (1, 1, 1))
    return ch


def evaluate(name, t):
    fn, dur, loop = CLIPS[name]
    body, tents = fn(t)
    return to_channels(body, tents)
