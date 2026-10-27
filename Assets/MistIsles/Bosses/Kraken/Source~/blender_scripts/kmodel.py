"""Kraken geometry + armature. Blender coords: Z up, kraken faces -Y. Water surface at z=0."""
import bpy, math, random
from mathutils import Vector, Matrix

RNG = random.Random(7)

# ---------------------------------------------------------------- palette
def _lin(hexs):
    c = [int(hexs[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(((x + 0.055) / 1.055) ** 2.4 if x > 0.04045 else x / 12.92 for x in c)


# Same sRGB values as the Unity setup tool (KrakenSetup.Palette)
PALETTE = {
    "Kraken_Body":      (_lin("6A3A9E"), 0.0),
    "Kraken_BodyDark":  (_lin("46206E"), 0.0),
    "Kraken_Belly":     (_lin("D9A6DA"), 0.0),
    "Kraken_Sucker":    (_lin("F6B3C4"), 0.0),
    "Kraken_Eye":       (_lin("FFC83A"), 1.6),
    "Kraken_Pupil":     (_lin("120614"), 0.0),
    "Kraken_Mouth":     (_lin("2A0B24"), 0.0),
    "Kraken_Teeth":     (_lin("F5EDD8"), 0.0),
    "Kraken_Crystal":   (_lin("7FF2FF"), 2.5),
}
MAT_ORDER = list(PALETTE.keys())


def make_materials():
    mats = {}
    for name, (rgb, emit) in PALETTE.items():
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        m.use_nodes = True
        nt = m.node_tree
        bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Base Color"].default_value = (*rgb, 1)
        bsdf.inputs["Roughness"].default_value = 0.75
        if emit > 0:
            bsdf.inputs["Emission Color"].default_value = (*rgb, 1)
            bsdf.inputs["Emission Strength"].default_value = emit
        m.diffuse_color = (*rgb, 1)
        mats[name] = m
    return mats


# ---------------------------------------------------------------- mesh builder
class MB:
    def __init__(self):
        self.v, self.f, self.fm, self.w = [], [], [], []

    def vert(self, co, weights):
        self.v.append(tuple(co))
        self.w.append(weights)
        return len(self.v) - 1

    def face(self, idx, mat):
        self.f.append(tuple(idx))
        self.fm.append(MAT_ORDER.index(mat))

    def build(self, name, mats):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.v, [], self.f)
        me.update()
        for m in MAT_ORDER:
            me.materials.append(mats[m])
        for p, mi in zip(me.polygons, self.fm):
            p.material_index = mi
            p.use_smooth = False
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        groups = {}
        for vi, ws in enumerate(self.w):
            for g, wt in ws:
                if wt <= 0.001:
                    continue
                if g not in groups:
                    groups[g] = ob.vertex_groups.new(name=g)
                groups[g].add([vi], wt, "REPLACE")
        return ob


def jit(a):
    return RNG.uniform(-a, a)


# ---------------------------------------------------------------- body
BODY_PROFILE = [(-2.4, 2.2), (-1.0, 2.7), (0.2, 2.85), (1.2, 2.78), (2.2, 2.52), (3.1, 2.3),
                (4.0, 2.45), (4.9, 2.6), (5.8, 2.45), (6.6, 2.0), (7.2, 1.3), (7.55, 0.5)]
BODY_TOP_Z = 7.68
BODY_SIDES = 12


def body_cy(z):
    return 0.07 * max(0.0, z - 2.6) ** 2


def body_r(z):
    p = BODY_PROFILE
    if z <= p[0][0]:
        return p[0][1]
    for (z0, r0), (z1, r1) in zip(p, p[1:]):
        if z0 <= z <= z1:
            u = (z - z0) / (z1 - z0)
            return r0 + (r1 - r0) * u
    return p[-1][1]


def body_weights(z):
    def bl(a, b, u):
        return [(a, 1 - u), (b, u)]
    if z < 2.0:
        return [("Body_Base", 1.0)]
    if z < 3.0:
        return bl("Body_Base", "Body_Mid", (z - 2.0) / 1.0)
    if z < 4.2:
        return [("Body_Mid", 1.0)]
    if z < 5.2:
        return bl("Body_Mid", "Body_Top", (z - 4.2) / 1.0)
    return [("Body_Top", 1.0)]


def front_surface(z, ang_from_front_deg=0.0, inset=0.0):
    """Point on body surface at height z, angle measured from front (-Y) toward +X (kraken's left)."""
    a = math.radians(-90 + ang_from_front_deg)
    r = body_r(z) - inset
    return Vector((r * math.cos(a), body_cy(z) + r * math.sin(a), z)), Vector((math.cos(a), math.sin(a), 0))


def build_body(mb):
    rings = []
    step = 2 * math.pi / BODY_SIDES
    for (z, r) in BODY_PROFILE:
        ring = []
        for j in range(BODY_SIDES):
            a = -math.pi / 2 + step * 0.5 + step * j
            rr = r * (1 + jit(0.035))
            zz = z + (jit(0.08) if -2.0 < z < 7.4 else 0)
            co = (rr * math.cos(a), body_cy(zz) + rr * math.sin(a), zz)
            ring.append(mb.vert(co, body_weights(zz)))
        rings.append(ring)
    top = mb.vert((0, body_cy(BODY_TOP_Z), BODY_TOP_Z), body_weights(BODY_TOP_Z))
    bot = mb.vert((0, 0, -2.6), body_weights(-2.6))
    n = BODY_SIDES
    for ri in range(len(rings) - 1):
        a, b = rings[ri], rings[ri + 1]
        zc = (BODY_PROFILE[ri][0] + BODY_PROFILE[ri + 1][0]) / 2
        for j in range(n):
            jn = (j + 1) % n
            ang = math.degrees(step * 0.5 + step * j + step / 2)  # 0 = front
            ang = (ang + 180) % 360 - 180
            mat = "Kraken_Body"
            if abs(ang) < 50 and -1.5 < zc < 1.6:
                mat = "Kraken_Belly"
            elif zc > 3.0 and RNG.random() < 0.2:
                mat = "Kraken_BodyDark"
            mb.face([a[j], a[jn], b[jn], b[j]], mat)
    for j in range(n):
        mb.face([rings[-1][j], rings[-1][(j + 1) % n], top], "Kraken_BodyDark" if RNG.random() < 0.3 else "Kraken_Body")
        mb.face([rings[0][(j + 1) % n], rings[0][j], bot], "Kraken_Body")


# ---------------------------------------------------------------- generic primitives
def uv_sphere(mb, c, rad, us, vs, weights, mat_fn, scale=(1, 1, 1), basis=None):
    basis = basis or Matrix.Identity(3)
    ids = []
    top = mb.vert(c + basis @ Vector((0, 0, rad * scale[2])), weights)
    for i in range(1, vs):
        th = math.pi * i / vs
        ring = []
        for j in range(us):
            ph = 2 * math.pi * j / us
            p = Vector((rad * math.sin(th) * math.cos(ph) * scale[0], rad * math.sin(th) * math.sin(ph) * scale[1], rad * math.cos(th) * scale[2]))
            ring.append(mb.vert(c + basis @ p, weights))
        ids.append(ring)
    bot = mb.vert(c + basis @ Vector((0, 0, -rad * scale[2])), weights)

    def fc(vs_):
        return sum((Vector(mb.v[k]) for k in vs_), Vector()) / len(vs_)
    for j in range(us):
        f = [top, ids[0][j], ids[0][(j + 1) % us]]
        mb.face(f, mat_fn(fc(f)))
    for i in range(len(ids) - 1):
        for j in range(us):
            f = [ids[i][j], ids[i + 1][j], ids[i + 1][(j + 1) % us], ids[i][(j + 1) % us]]
            mb.face(f, mat_fn(fc(f)))
    for j in range(us):
        f = [bot, ids[-1][(j + 1) % us], ids[-1][j]]
        mb.face(f, mat_fn(fc(f)))


def box(mb, c, half, basis, weights, mat):
    corners = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            for sz in (-1, 1):
                corners.append(mb.vert(c + basis @ Vector((sx * half[0], sy * half[1], sz * half[2])), weights))
    i = lambda sx, sy, sz: corners[(sx > 0) * 4 + (sy > 0) * 2 + (sz > 0)]
    for f in ([i(-1, -1, -1), i(-1, -1, 1), i(-1, 1, 1), i(-1, 1, -1)],
              [i(1, -1, -1), i(1, 1, -1), i(1, 1, 1), i(1, -1, 1)],
              [i(-1, -1, -1), i(1, -1, -1), i(1, -1, 1), i(-1, -1, 1)],
              [i(-1, 1, -1), i(-1, 1, 1), i(1, 1, 1), i(1, 1, -1)],
              [i(-1, -1, -1), i(-1, 1, -1), i(1, 1, -1), i(1, -1, -1)],
              [i(-1, -1, 1), i(1, -1, 1), i(1, 1, 1), i(-1, 1, 1)]):
        mb.face(f, mat)


def look_basis(fwd, up=Vector((0, 0, 1))):
    """Matrix whose columns are (right, up', fwd) -> local X right, Y up, Z forward."""
    f = fwd.normalized()
    r = up.cross(f).normalized()
    u = f.cross(r).normalized()
    return Matrix((r, u, f)).transposed()


def bipyramid(mb, base_c, axis, rad, h, weights, mat, sides=6):
    axis = axis.normalized()
    b = look_basis(axis, Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((0, 1, 0)))
    ring = []
    for j in range(sides):
        a = 2 * math.pi * j / sides
        ring.append(mb.vert(base_c + axis * (h * 0.25) + b @ Vector((rad * math.cos(a), rad * math.sin(a), 0)), weights))
    tip = mb.vert(base_c + axis * h, weights)
    bot = mb.vert(base_c - axis * (h * 0.15), weights)
    for j in range(sides):
        mb.face([ring[j], ring[(j + 1) % sides], tip], mat)
        mb.face([ring[(j + 1) % sides], ring[j], bot], mat)


# ---------------------------------------------------------------- face details
EYE_Z = 2.15
EYE_ANG = 30.0
EYE_R = 0.74


def eye_frame(side):
    s = 1 if side == "L" else -1  # L = +X
    surf, n = front_surface(EYE_Z, s * EYE_ANG)
    c = surf - n * 0.28
    fwd = (n + Vector((0, -0.8, 0))).normalized()
    return c, fwd


def build_eyes(mb):
    for side in ("L", "R"):
        c, fwd = eye_frame(side)
        g = [("Eye_" + side, 1.0)]
        basis = look_basis(fwd)  # local Z = fwd

        def matfn(fc, c=c, fwd=fwd):
            return "Kraken_Eye" if (fc - c).normalized().dot(fwd) > 0.05 else "Kraken_BodyDark"
        # sphere with its poles along local Z(fwd): build in basis where Z is fwd
        uv_sphere(mb, c, EYE_R, 10, 7, g, matfn, basis=basis)
        # slit pupil
        box(mb, c + fwd * (EYE_R * 0.97), (0.075, 0.34, 0.06), basis, g, "Kraken_Pupil")
        # highlight
        s = 1 if side == "L" else -1
        box(mb, c + fwd * (EYE_R * 0.9) + basis @ Vector((0.28 * s, 0.3, 0)), (0.07, 0.07, 0.05), basis, g, "Kraken_Teeth")
        # angry brow: inner end lower
        bc = c + Vector((0, 0, EYE_R * 0.95)) + fwd * 0.35
        bb = look_basis(fwd) @ Matrix.Rotation(math.radians(-24 * s), 3, "Z")
        box(mb, bc, (0.62, 0.16, 0.26), bb, body_weights(bc.z), "Kraken_BodyDark")


MOUTH_Z = 0.72


def build_mouth(mb):
    surf, n = front_surface(MOUTH_Z, 0)
    c = surf + Vector((0, -0.04, 0))
    hw, hh = 0.95, 0.36
    N = 12
    rim = []
    for j in range(N):
        a = 2 * math.pi * j / N
        x, z = hw * math.cos(a), hh * math.sin(a)
        p = c + Vector((x, 0.18 * (abs(x) / hw) ** 2, z))  # follow curvature
        w = [("Body_Base", 1.0)] if z > 0.01 else ([("Jaw", 1.0)] if z < -0.01 else [("Body_Base", 0.5), ("Jaw", 0.5)])
        rim.append(mb.vert(p, w))
    inner_top = mb.vert(c + Vector((0, 0.55, 0.05)), [("Body_Base", 1.0)])
    inner_bot = mb.vert(c + Vector((0, 0.55, -0.05)), [("Jaw", 1.0)])
    for j in range(N):
        jn = (j + 1) % N
        upper = mb.v[rim[j]][2] + mb.v[rim[jn]][2] > 2 * c.z
        mb.face([rim[jn], rim[j], inner_top if upper else inner_bot], "Kraken_Mouth")
    # teeth
    for k, x in enumerate((-0.6, -0.3, 0.0, 0.3, 0.6)):
        zt = c.z + hh * math.sqrt(max(0, 1 - (x / hw) ** 2)) * 0.92
        bipyramid(mb, Vector((x, c.y - 0.02 + 0.18 * (abs(x) / hw) ** 2, zt)), Vector((0, -0.25, -1)), 0.09, 0.32, [("Body_Base", 1.0)], "Kraken_Teeth", 4)
    for x in (-0.45, -0.15, 0.15, 0.45):
        zt = c.z - hh * math.sqrt(max(0, 1 - (x / hw) ** 2)) * 0.92
        bipyramid(mb, Vector((x, c.y - 0.02 + 0.18 * (abs(x) / hw) ** 2, zt)), Vector((0, -0.25, 1)), 0.08, 0.26, [("Jaw", 1.0)], "Kraken_Teeth", 4)
    # lower lip band (jaw volume)
    pts = []
    for k in range(9):
        u = -1 + 2 * k / 8
        x = u * hw * 1.05
        z = c.z - hh * 1.05 * math.sqrt(max(0.0, 1 - u * u)) - 0.12
        pts.append(Vector((x, c.y + 0.05 + 0.2 * u * u, z)))
    prev = None
    for p in pts:
        q = [mb.vert(p + Vector(d), [("Jaw", 1.0)]) for d in ((0, -0.12, 0.1), (0, -0.12, -0.12), (0, 0.25, -0.12), (0, 0.25, 0.1))]
        if prev:
            for j in range(4):
                mb.face([prev[j], prev[(j + 1) % 4], q[(j + 1) % 4], q[j]], "Kraken_Belly")
        prev = q


CORE_Z = 5.05


def core_frame():
    surf, n = front_surface(CORE_Z, 0)
    nrm = (n + Vector((0, 0, 0.35))).normalized()
    return surf - n * 0.15, nrm


def build_core(mb):
    c, nrm = core_frame()
    g = [("WeakPoint", 1.0)]
    specs = [((0, 0, 0), 0.30, 1.5, (0, 0, 0)), ((0.45, 0.0, -0.2), 0.2, 1.0, (0.5, 0, 0.1)),
             ((-0.5, 0.0, -0.15), 0.22, 1.1, (-0.5, 0, 0.15)), ((0.2, 0.0, 0.42), 0.17, 0.85, (0.2, 0, 0.6)),
             ((-0.25, 0.0, 0.38), 0.16, 0.8, (-0.35, 0, 0.5))]
    for off, r, h, tilt in specs:
        axis = (nrm + Vector(tilt) * 0.7).normalized()
        bipyramid(mb, c + Vector(off), axis, r, h, g, "Kraken_Crystal", 6)
    # dark socket rim
    b = look_basis(nrm)
    ring = [mb.vert(c + b @ Vector((0.85 * math.cos(a), 0.7 * math.sin(a), -0.05)), body_weights(c.z)) for a in [2 * math.pi * j / 8 for j in range(8)]]
    ctr = mb.vert(c + nrm * 0.12, body_weights(c.z))
    for j in range(8):
        mb.face([ring[j], ring[(j + 1) % 8], ctr], "Kraken_BodyDark")


# ---------------------------------------------------------------- tentacles
NB = 8
# name: (heading deg CCW from +X, radial distance, base z, length, base radius)
TENTACLES = {
    "FL":  (-55.0, 4.2, -1.6, 10.0, 0.78),
    "FR":  (-125.0, 4.2, -1.6, 10.0, 0.78),
    "SL":  (-5.0, 4.7, -1.3, 8.0, 0.58),
    "SR":  (-175.0, 4.7, -1.3, 8.0, 0.58),
    "BL":  (40.0, 4.2, -1.3, 7.5, 0.55),
    "BR":  (140.0, 4.2, -1.3, 7.5, 0.55),
    "BBL": (75.0, 3.7, -1.3, 7.0, 0.5),
    "BBR": (105.0, 3.7, -1.3, 7.0, 0.5),
}
TENT_ORDER = list(TENTACLES.keys())


def tent_frame(name):
    h, rd, bz, L, r0 = TENTACLES[name]
    a = math.radians(h)
    out = Vector((math.cos(a), math.sin(a), 0))
    tan = Vector((0, 0, 1)).cross(out)
    base = out * rd + Vector((0, 0, bz))
    return base, out, tan, L, r0


def tent_radius(r0, s):
    return r0 * (1 - s) ** 0.8 * 0.94 + 0.05


def tent_weights(name, s):
    f = s * NB - 0.5
    i0 = max(0, min(NB - 1, math.floor(f)))
    i1 = max(0, min(NB - 1, i0 + 1))
    u = max(0.0, min(1.0, f - i0))
    if i0 == i1:
        return [(f"T_{name}_{i0:02d}", 1.0)]
    return [(f"T_{name}_{i0:02d}", 1 - u), (f"T_{name}_{i1:02d}", u)]


def build_tentacle(mb, name):
    base, out, tan, L, r0 = tent_frame(name)
    up = Vector((0, 0, 1))
    sides = 6
    nr = NB * 2 + 1
    rings = []
    for k in range(nr):
        s = k / (nr - 1) * 0.96
        rr = tent_radius(r0, s)
        ring = []
        for j in range(sides):
            a = 2 * math.pi * j / sides + math.pi / sides
            d = out * math.cos(a) + tan * math.sin(a)
            ring.append(mb.vert(base + up * (s * L) + d * rr * (1 + jit(0.04)), tent_weights(name, s)))
        rings.append(ring)
    tip = mb.vert(base + up * L, tent_weights(name, 1.0))
    capc = mb.vert(base - up * 0.1, tent_weights(name, 0))
    for k in range(nr - 1):
        for j in range(sides):
            jn = (j + 1) % sides
            # face j..jn centre angle = 2pi(j+.5)/6 + pi/6 ; face 5 -> 0deg (out)
            ca = 2 * math.pi * (j + 0.5) / sides + math.pi / sides
            facing = math.cos(ca)
            mat = "Kraken_Belly" if facing > 0.7 else ("Kraken_BodyDark" if facing < -0.7 and RNG.random() < 0.35 else "Kraken_Body")
            mb.face([rings[k][j], rings[k][jn], rings[k + 1][jn], rings[k + 1][j]], mat)
    for j in range(sides):
        mb.face([rings[-1][j], rings[-1][(j + 1) % sides], tip], "Kraken_Body")
        mb.face([rings[0][(j + 1) % sides], rings[0][j], capc], "Kraken_Body")
    # suckers on the out face, alternating
    for k in range(2, nr - 2):
        s = k / (nr - 1) * 0.96 + 0.5 / (nr - 1)
        rr = tent_radius(r0, s)
        apo = rr * math.cos(math.pi / sides)
        side_off = (0.28 if k % 2 else -0.28) * rr
        c = base + up * (s * L) + out * apo + tan * side_off
        sr = rr * 0.38
        w = tent_weights(name, s)
        b = look_basis(out)
        outer = [mb.vert(c + b @ Vector((sr * math.cos(a), sr * math.sin(a), 0.0)), w) for a in [2 * math.pi * j / 6 for j in range(6)]]
        inner = [mb.vert(c + b @ Vector((sr * 0.6 * math.cos(a), sr * 0.6 * math.sin(a), sr * 0.45)), w) for a in [2 * math.pi * j / 6 for j in range(6)]]
        ctr = mb.vert(c + out * (sr * 0.15), w)
        for j in range(6):
            jn = (j + 1) % 6
            mb.face([outer[j], outer[jn], inner[jn], inner[j]], "Kraken_Sucker")
            mb.face([inner[j], inner[jn], ctr], "Kraken_Mouth" if j % 2 else "Kraken_Sucker")


# ---------------------------------------------------------------- armature
def build_armature():
    arm = bpy.data.armatures.new("Kraken_Rig")
    ob = bpy.data.objects.new("Kraken_Rig", arm)
    bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.edit_bones
    FWD = Vector((0, -1, 0))
    UP = Vector((0, 0, 1))

    def bone(name, h, t, parent=None, roll_to=None, deform=True):
        b = eb.new(name)
        b.head, b.tail = Vector(h), Vector(t)
        if roll_to is not None:
            b.align_roll(roll_to)
        if parent:
            b.parent = eb[parent]
        b.use_deform = deform
        return b

    bone("Root", (0, 0, 0), (0, 0, 1.0), roll_to=FWD, deform=False)
    bone("Body_Base", (0, 0, -1.0), (0, 0, 2.6), "Root", FWD)
    bone("Body_Mid", (0, 0, 2.6), (0, body_cy(4.6), 4.6), "Body_Base", FWD)
    eb["Body_Mid"].use_connect = True
    bone("Body_Top", (0, body_cy(4.6), 4.6), (0, body_cy(7.4), 7.4), "Body_Mid", FWD)
    eb["Body_Top"].use_connect = True
    for side in ("L", "R"):
        c, fwd = eye_frame(side)
        bone("Eye_" + side, c, c + fwd * 0.6, "Body_Base", UP)
    ms, _ = front_surface(MOUTH_Z, 0)
    bone("Jaw", (0, ms.y + 0.9, MOUTH_Z + 0.05), (0, ms.y - 0.15, MOUTH_Z - 0.15), "Body_Base", UP)
    bone("Mouth_Socket", (0, ms.y - 0.3, MOUTH_Z), (0, ms.y - 0.9, MOUTH_Z), "Body_Base", UP, deform=False)
    c, nrm = core_frame()
    bone("WeakPoint", c, c + nrm * 0.6, "Body_Top", UP)
    for name in TENT_ORDER:
        base, out, tan, L, r0 = tent_frame(name)
        bl = L / NB
        prev = "Root"
        for i in range(NB):
            n = f"T_{name}_{i:02d}"
            bone(n, base + UP * (i * bl), base + UP * ((i + 1) * bl), prev, out)
            if i > 0:
                eb[n].use_connect = True
            prev = n
    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in ob.pose.bones:
        pb.rotation_mode = "XYZ"
    return ob


def build_kraken():
    mats = make_materials()
    mb = MB()
    build_body(mb)
    build_eyes(mb)
    build_mouth(mb)
    build_core(mb)
    for n in TENT_ORDER:
        build_tentacle(mb, n)
    mesh = mb.build("Kraken_Mesh", mats)
    rig = build_armature()
    mesh.parent = rig
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    return rig, mesh
