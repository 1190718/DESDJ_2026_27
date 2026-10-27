"""Builds the Mist Crab and Shore Guardian: low-poly mesh, rigid-skinned armature,
animations, FBX export for Unity. Run: python3 build_crab.py <outdir> [crab|guardian]"""
import sys, math
import bpy, bmesh
from mathutils import Vector, Matrix, Quaternion

OUT = sys.argv[1]
VARIANT = sys.argv[2] if len(sys.argv) > 2 else "crab"
G = VARIANT == "guardian"
FPS = 30


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    return sc


def mat(name, rgb, emit=None, rough=0.7):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = 2.0
    m.diffuse_color = (*rgb, 1)
    return m


def hexc(h):
    h = h.lstrip('#')
    c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


if G:
    PAL = dict(shell="#3D4F63", belly="#C9C2A8", claw="#2F4052", tip="#1B242E", eye="#0E1114",
               spike="#E3DCC4", core="#3FE0C8")
else:
    PAL = dict(shell="#7FA6B0", belly="#E8DCC0", claw="#5F8792", tip="#2E3E44", eye="#101214",
               spike="#E8DCC0", core="#3FE0C8")

reset()
MATS = {}


def M(key):
    if key not in MATS:
        if key == "core":
            MATS[key] = mat("Core", hexc(PAL[key]), emit=hexc(PAL[key]), rough=0.3)
        else:
            MATS[key] = mat(key.capitalize(), hexc(PAL[key]), rough=0.55 if key == "shell" else 0.75)
    return MATS[key]


PARTS = []  # (object, bone)


def finish(bm, name, bone, matkey):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    me.materials.append(M(matkey))
    for p in me.polygons:
        p.use_smooth = False
    PARTS.append((ob, bone))
    return ob


def ellipsoid(name, bone, matkey, center, scale, seg=10, rings=6, flatten_bottom=1.0):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    for v in bm.verts:
        if v.co.z < 0:
            v.co.z *= flatten_bottom
        v.co = Vector((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2])) + Vector(center)
    return finish(bm, name, bone, matkey)


def ico(name, bone, matkey, center, r, sub=1):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=r)
    for v in bm.verts:
        v.co += Vector(center)
    return finish(bm, name, bone, matkey)


def tube(name, bone, matkey, a, b, r1, r2, sides=6):
    """Tapered cylinder from a to b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=sides, radius1=r1, radius2=r2, depth=d.length)
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    for v in bm.verts:
        v.co = rot @ (v.co + Vector((0, 0, d.length / 2))) + a
    return finish(bm, name, bone, matkey)


def cone(name, bone, matkey, base, tip, r, sides=5):
    return tube(name, bone, matkey, base, tip, r, 0.0, sides)


# ---------------------------------------------------------------- geometry
CS = 1.9 if G else 1.35  # claw scale

# Front of the crab faces -Y (Blender front view) -> +Z in Unity.
BODY_Z = 0.32
ellipsoid("Shell", "body", "shell", (0, 0, BODY_Z), (0.5, 0.4, 0.2), seg=12, rings=6, flatten_bottom=0.45)
ellipsoid("Belly", "body", "belly", (0, 0.01, BODY_Z - 0.06), (0.40, 0.31, 0.07), seg=10, rings=4)
# shell ridge bumps
for sx in (-1, 1):
    ellipsoid(f"Ridge{sx}", "body", "shell", (sx * 0.2, -0.05, BODY_Z + 0.12), (0.12, 0.16, 0.06), seg=6, rings=4)
# mouth plate
ellipsoid("Mouth", "body", "claw", (0, -0.33, BODY_Z - 0.03), (0.12, 0.05, 0.06), seg=6, rings=4)

# eyes
for side, sx in (("L", 1), ("R", -1)):
    base = (sx * 0.11, -0.27, BODY_Z + 0.08)
    top = (sx * 0.14, -0.31, BODY_Z + 0.24)
    tube(f"EyeStalk{side}", f"eye_{side}", "claw", base, top, 0.026, 0.02)
    ico(f"Eye{side}", f"eye_{side}", "eye", top, 0.045)

# legs: hips along the side, front (-Y) to back
LEG_Y = (-0.12, 0.04, 0.19)
for side, sx in (("L", 1), ("R", -1)):
    for i, y in enumerate(LEG_Y, start=1):
        spread = (i - 2) * 0.07
        hip = (sx * 0.36, y, BODY_Z - 0.02)
        knee = (sx * 0.58, y + spread, BODY_Z + 0.06)
        foot = (sx * 0.70, y + spread * 1.5, 0.0)
        tube(f"Leg{side}{i}U", f"leg_{side}{i}_u", "claw", hip, knee, 0.065, 0.05)
        tube(f"Leg{side}{i}L", f"leg_{side}{i}_l", "claw", knee, foot, 0.05, 0.02)
        ico(f"Knee{side}{i}", f"leg_{side}{i}_l", "claw", knee, 0.055, 1)

# claws
CLAW = {}
for side, sx in (("L", 1), ("R", -1)):
    sh = Vector((sx * 0.3, -0.26, BODY_Z))
    el = sh + Vector((sx * 0.14, -0.14, 0.08)) * CS
    wr = el + Vector((-sx * 0.06, -0.16, 0.06)) * CS
    tipU = wr + Vector((sx * 0.02, -0.24, 0.05)) * CS
    tipL = wr + Vector((sx * 0.0, -0.20, -0.06)) * CS
    CLAW[side] = dict(sh=sh, el=el, wr=wr, tipU=tipU, tipL=tipL)
    tube(f"Arm{side}", f"claw_{side}_arm", "claw", sh, el, 0.05 * CS, 0.045 * CS)
    ico(f"Elbow{side}", f"claw_{side}_fore", "claw", el, 0.05 * CS, 1)
    tube(f"Fore{side}", f"claw_{side}_fore", "claw", el, wr, 0.045 * CS, 0.06 * CS)
    ellipsoid(f"Hand{side}", f"claw_{side}_hand", "shell", tuple(wr + Vector((0, -0.06, 0.01)) * 1.0),
              (0.11 * CS, 0.15 * CS, 0.1 * CS), seg=8, rings=5)
    cone(f"FingerU{side}", f"claw_{side}_hand", "tip", wr + Vector((0, -0.12, 0.03)) * CS, tipU, 0.07 * CS)
    cone(f"FingerL{side}", f"claw_{side}_jaw", "tip", wr + Vector((0, -0.04, -0.03)) * CS, tipL, 0.055 * CS)

if G:
    # barnacle spikes and the glowing core (the guardian's weak point)
    for k, (x, y) in enumerate([(-0.28, 0.05), (0.28, 0.05), (-0.18, -0.15), (0.18, -0.15), (-0.16, 0.22), (0.16, 0.22)]):
        base = Vector((x, y, BODY_Z + 0.12))
        cone(f"Spike{k}", "body", "spike", base, base + Vector((x * 0.4, y * 0.3, 0.2)), 0.06)
    ico("CoreGem", "body", "core", (0, 0.08, BODY_Z + 0.2), 0.1, 1)
    # core housing ring
    tube("CoreRim", "body", "shell", (0, 0.08, BODY_Z + 0.12), (0, 0.08, BODY_Z + 0.17), 0.14, 0.12, 8)

# ---------------------------------------------------------------- armature
arm_data = bpy.data.armatures.new("CrabRig")
rig = bpy.data.objects.new("Armature", arm_data)
bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones


def bone(name, head, tail, parent=None):
    b = eb.new(name)
    b.head, b.tail = Vector(head), Vector(tail)
    b.roll = 0
    if parent:
        b.parent = eb[parent]
    return b


bone("root", (0, 0, 0), (0, 0.2, 0))
bone("body", (0, 0, BODY_Z - 0.08), (0, 0, BODY_Z + 0.12), "root")
for side, sx in (("L", 1), ("R", -1)):
    bone(f"eye_{side}", (sx * 0.11, -0.27, BODY_Z + 0.08), (sx * 0.14, -0.31, BODY_Z + 0.24), "body")
    for i, y in enumerate(LEG_Y, start=1):
        spread = (i - 2) * 0.07
        hip = (sx * 0.36, y, BODY_Z - 0.02)
        knee = (sx * 0.58, y + spread, BODY_Z + 0.06)
        foot = (sx * 0.70, y + spread * 1.5, 0.0)
        bone(f"leg_{side}{i}_u", hip, knee, "body")
        bone(f"leg_{side}{i}_l", knee, foot, f"leg_{side}{i}_u")
    c = CLAW[side]
    bone(f"claw_{side}_arm", c["sh"], c["el"], "body")
    bone(f"claw_{side}_fore", c["el"], c["wr"], f"claw_{side}_arm")
    bone(f"claw_{side}_hand", c["wr"], c["tipU"], f"claw_{side}_fore")
    bone(f"claw_{side}_jaw", c["wr"], c["tipL"], f"claw_{side}_fore")
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------- skin (rigid)
for ob, bname in PARTS:
    vg = ob.vertex_groups.new(name=bname)
    vg.add(list(range(len(ob.data.vertices))), 1.0, 'REPLACE')
bpy.ops.object.select_all(action='DESELECT')
for ob, _ in PARTS:
    ob.select_set(True)
body = PARTS[0][0]
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()
body.name = "ShoreGuardian_Mesh" if G else "MistCrab_Mesh"
body.data.name = body.name
mod = body.modifiers.new("Armature", 'ARMATURE')
mod.object = rig
body.parent = rig

# ---------------------------------------------------------------- animation helpers
pb = rig.pose.bones
for p in pb:
    p.rotation_mode = 'QUATERNION'
REST = {b.name: b.matrix_local.to_3x3() for b in arm_data.bones}

AX = {"x": Vector((1, 0, 0)), "y": Vector((0, 1, 0)), "z": Vector((0, 0, 1))}


def q(bname, axis, deg):
    """Rotation about an armature-space axis, expressed in the bone's rest frame."""
    a = AX[axis] if isinstance(axis, str) else Vector(axis)
    if bname.startswith("claw_") and axis == "x":
        deg = -deg  # claw bones point forward (-Y): positive = raise
    m = REST[bname]
    local_axis = (m.inverted() @ a).normalized()
    return Quaternion(local_axis, math.radians(deg))


def side_sign(side):
    return 1 if side == "L" else -1


def new_action(name, frames):
    act = bpy.data.actions.new(name)
    rig.animation_data_create()
    rig.animation_data.action = act
    act.use_fake_user = True
    bpy.context.scene.frame_start = 0
    bpy.context.scene.frame_end = frames
    return act


def key_all(frame, pose):
    """pose: dict bone -> (Quaternion or None, location Vector or None). Unlisted bones go to rest."""
    for p in pb:
        rot, loc = pose.get(p.name, (None, None))
        p.rotation_quaternion = rot if rot is not None else Quaternion()
        p.location = loc if loc is not None else Vector()
        p.keyframe_insert("rotation_quaternion", frame=frame)
        p.keyframe_insert("location", frame=frame)


def compose(*qs):
    r = Quaternion()
    for x in qs:
        r = x @ r
    return r


def body_up(dz):
    # body bone points +Z in armature space, so local +Y = up
    return Vector((0, dz, 0))


def claws_ready(pose, raise_deg=12, jaw=10):
    for s in "LR":
        sx = side_sign(s)
        pose[f"claw_{s}_arm"] = (q(f"claw_{s}_arm", "x", raise_deg), None)
        pose[f"claw_{s}_jaw"] = (q(f"claw_{s}_jaw", (sx, 0, 0), -jaw * 0 - jaw), None)
    return pose


ACTIONS = []

# Idle ---------------------------------------------------------------
new_action("Idle", 40)
for f, ph in ((0, 0), (10, 1), (20, 0), (30, -1), (40, 0)):
    pose = {"body": (None, body_up(0.012 * ph))}
    for s in "LR":
        sx = side_sign(s)
        pose[f"claw_{s}_arm"] = (q(f"claw_{s}_arm", "x", 10 + 4 * ph), None)
        pose[f"claw_{s}_jaw"] = (q(f"claw_{s}_jaw", "x", -8 - 10 * abs(ph)), None)
        pose[f"eye_{s}"] = (q(f"eye_{s}", "y", 8 * ph * sx), None)
    key_all(f, pose)
ACTIONS.append("Idle")

# Walk (tripod gait) ---------------------------------------------------
new_action("Walk", 20)
for f in range(0, 21, 5):
    t = f / 20.0
    pose = {"body": (None, body_up(0.015 * abs(math.sin(t * 2 * math.pi * 2))))}
    for s in "LR":
        sx = side_sign(s)
        for i in (1, 2, 3):
            grp = (i % 2 == 1) == (s == "L")
            ph = t * 2 * math.pi + (0 if grp else math.pi)
            lift = max(0.0, math.sin(ph)) * 22
            swing = math.cos(ph) * 14
            u = f"leg_{s}{i}_u"
            pose[u] = (compose(q(u, "y", -sx * lift), q(u, "z", swing)), None)
        pose[f"claw_{s}_arm"] = (q(f"claw_{s}_arm", "x", 18), None)
        pose[f"claw_{s}_jaw"] = (q(f"claw_{s}_jaw", "x", -6), None)
    key_all(f, pose)
ACTIONS.append("Walk")

# Attack (right claw snap, left follows) -------------------------------
new_action("Attack", 27)
seq = [(0, 10, 0, 8), (9, 70, -30, 45), (14, -25, 25, 0), (20, -15, 12, 0), (27, 10, 0, 8)]
for f, up, fore, jaw in seq:
    pose = {"body": (q("body", "x", -0.15 * up), body_up(0.01 if up > 30 else 0))}
    for s, k in (("R", 1.0), ("L", 0.45)):
        pose[f"claw_{s}_arm"] = (q(f"claw_{s}_arm", "x", up * k), None)
        pose[f"claw_{s}_fore"] = (q(f"claw_{s}_fore", "x", fore * k), None)
        pose[f"claw_{s}_jaw"] = (q(f"claw_{s}_jaw", "x", -jaw * k), None)
    key_all(f, pose)
ACTIONS.append("Attack")

# Hit ---------------------------------------------------------------
new_action("Hit", 12)
for f, k in ((0, 0), (3, 1), (12, 0)):
    pose = {"body": (q("body", "x", -14 * k), body_up(-0.02 * k))}
    for s in "LR":
        sx = side_sign(s)
        pose[f"claw_{s}_arm"] = (q(f"claw_{s}_arm", "z", sx * 20 * k), None)
        pose[f"eye_{s}"] = (q(f"eye_{s}", "x", 25 * k), None)
    key_all(f, pose)
ACTIONS.append("Hit")

# Die ---------------------------------------------------------------
new_action("Die", 36)
for f, k in ((0, 0), (8, 0.5), (20, 1.0), (36, 1.0)):
    pose = {"body": (q("body", "y", 18 * k), body_up(-0.2 * k))}
    for s in "LR":
        sx = side_sign(s)
        for i in (1, 2, 3):
            u, l = f"leg_{s}{i}_u", f"leg_{s}{i}_l"
            pose[u] = (q(u, "y", sx * 30 * k), None)
            pose[l] = (q(l, "y", sx * 45 * k), None)
        pose[f"claw_{s}_arm"] = (q(f"claw_{s}_arm", "x", -25 * k), None)
        pose[f"claw_{s}_jaw"] = (q(f"claw_{s}_jaw", "x", -20 * k), None)
        pose[f"eye_{s}"] = (q(f"eye_{s}", "x", 60 * k), None)
    key_all(f, pose)
ACTIONS.append("Die")

if G:
    # Slam: both claws up high, then crash down
    new_action("Slam", 36)
    for f, up, fore, btilt in ((0, 10, 0, 0), (14, 75, -35, -12), (20, -30, 25, 8), (28, -25, 20, 6), (36, 10, 0, 0)):
        pose = {"body": (q("body", "x", btilt), body_up(0.04 if up > 50 else 0))}
        for s in "LR":
            pose[f"claw_{s}_arm"] = (q(f"claw_{s}_arm", "x", up), None)
            pose[f"claw_{s}_fore"] = (q(f"claw_{s}_fore", "x", fore), None)
            pose[f"claw_{s}_jaw"] = (q(f"claw_{s}_jaw", "x", -30 if up > 50 else 0), None)
        key_all(f, pose)
    ACTIONS.append("Slam")
    # Roar: rear up with claws wide, used on emerge and phase change
    new_action("Roar", 40)
    for f, k in ((0, 0), (10, 1), (30, 1), (40, 0)):
        pose = {"body": (q("body", "x", -18 * k), body_up(0.05 * k))}
        for s in "LR":
            sx = side_sign(s)
            pose[f"claw_{s}_arm"] = (compose(q(f"claw_{s}_arm", "x", 50 * k), q(f"claw_{s}_arm", "z", sx * 25 * k)), None)
            pose[f"claw_{s}_jaw"] = (q(f"claw_{s}_jaw", "x", -35 * k), None)
            pose[f"eye_{s}"] = (q(f"eye_{s}", "x", -20 * k), None)
        key_all(f, pose)
    ACTIONS.append("Roar")

# Push each action to its own NLA track so the exporter writes one take per action
rig.animation_data.action = None
for name in ACTIONS:
    tr = rig.animation_data.nla_tracks.new()
    tr.name = name
    act = bpy.data.actions[name]
    st = tr.strips.new(name, 0, act)
    tr.mute = True

name = "ShoreGuardian" if G else "MistCrab"
rig.name = name
bpy.ops.wm.save_as_mainfile(filepath=f"{OUT}/{name}.blend")
bpy.ops.export_scene.fbx(
    filepath=f"{OUT}/{name}.fbx",
    object_types={'ARMATURE', 'MESH'},
    add_leaf_bones=False,
    bake_anim=True,
    bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=True,
    bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.0,
    apply_scale_options='FBX_SCALE_UNITS',
    axis_forward='-Z', axis_up='Y',
    mesh_smooth_type='FACE',
    use_armature_deform_only=False,
)
print("ACTIONS", ACTIONS, "VERTS", len(body.data.vertices), "BONES", len(arm_data.bones))
