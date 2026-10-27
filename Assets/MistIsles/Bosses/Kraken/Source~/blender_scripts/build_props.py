"""Erupting tentacle (rigged + animated) and static props. usage: python build_props.py OUTDIR"""
import sys, os, math, json, random
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector

OUT = sys.argv[1]
FPS = 30


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export(path, objs, anim):
    for o in bpy.context.scene.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", add_leaf_bones=False, armature_nodetype="NULL",
        bake_anim=anim, bake_anim_use_all_actions=anim, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0)


# ---------------------------------------------------------------- erupt tentacle
reset()
import kmodel, kanim
kmodel.TENTACLES = {"ER": (-90.0, 0.0, -1.0, 6.5, 0.62)}
kmodel.RNG = random.Random(11)
mats = kmodel.make_materials()
mb = kmodel.MB()
kmodel.build_tentacle(mb, "ER")
mesh = mb.build("Kraken_EruptTentacle_Mesh", mats)
arm = bpy.data.armatures.new("EruptTentacle_Rig")
rig = bpy.data.objects.new("EruptTentacle_Rig", arm)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
base, out, tan, L, r0 = kmodel.tent_frame("ER")
r = arm.edit_bones.new("Root")
r.head, r.tail = Vector((0, 0, 0)), Vector((0, 0, 0.6))
r.align_roll(Vector((0, -1, 0)))
r.use_deform = False
prev = r
for i in range(kmodel.NB):
    b = arm.edit_bones.new(f"T_ER_{i:02d}")
    b.head = base + Vector((0, 0, i * L / kmodel.NB))
    b.tail = base + Vector((0, 0, (i + 1) * L / kmodel.NB))
    b.align_roll(out)
    b.parent = prev
    b.use_connect = i > 0
    prev = b
bpy.ops.object.mode_set(mode="OBJECT")
for pb in rig.pose.bones:
    pb.rotation_mode = "XYZ"
mesh.parent = rig
mesh.modifiers.new("Armature", "ARMATURE").object = rig
rig.animation_data_create()

E = kanim.EASE


def er_pose(name, t):
    if name == "Erupt_Hidden":
        return [20] * 8, [0] * 8, -8.0
    # Erupt
    if t < 0.18:
        u = E["back"](t / 0.18)
        lift = -8 + 8 * u
        bend = [4 * (1 - u)] * 8
        side = [0] * 8
    elif t < 2.2:
        k = min(1.0, (t - 0.18) / 0.3)
        bend = [kanim.S_CURVE[i] * 0.6 * k + (5 + 3 * i) * k * math.sin(kanim.TAU * 1.6 * t - 0.6 * i) for i in range(8)]
        side = [(3 + 2.5 * i) * k * math.sin(kanim.TAU * 1.1 * t + 1 - 0.5 * i) for i in range(8)]
        lift = 0.0
    else:
        u = E["in2"](min(1.0, (t - 2.2) / 0.8))
        bend = [kanim.S_CURVE[i] * 0.6 * (1 - u) + 25 * u for i in range(8)]
        side = [0] * 8
        lift = -8 * u
    return bend, side, lift


er_clips = {"Erupt_Hidden": 1.0, "Erupt": 3.0}
for name, dur in er_clips.items():
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    nf = int(round(dur * FPS))
    rows = [er_pose(name, f / FPS) for f in range(nf + 1)]
    for i in range(8):
        bn = f"T_ER_{i:02d}"
        for idx in range(3):
            fc = act.fcurve_ensure_for_datablock(rig, f'pose.bones["{bn}"].rotation_euler', index=idx, group_name=bn)
            vals = [math.radians(row[0][i]) if idx == 0 else (math.radians(row[1][i]) if idx == 2 else 0.0) for row in rows]
            fc.keyframe_points.add(len(vals))
            fc.keyframe_points.foreach_set("co", [c for f, v in enumerate(vals) for c in (f, v)])
            fc.update()
        if i == 0:
            for idx in range(3):
                fc = act.fcurve_ensure_for_datablock(rig, f'pose.bones["{bn}"].location', index=idx, group_name=bn)
                vals = [row[2] if idx == 1 else 0.0 for row in rows]
                fc.keyframe_points.add(len(vals))
                fc.keyframe_points.foreach_set("co", [c for f, v in enumerate(vals) for c in (f, v)])
                fc.update()
    act.frame_range = (0, nf)
rig.animation_data.action = None
for pb in rig.pose.bones:
    pb.location = (0, 0, 0)
    pb.rotation_euler = (0, 0, 0)
export(os.path.join(OUT, "Kraken_EruptTentacle.fbx"), [rig, mesh], True)
erupt_radii = [round(kmodel.tent_radius(r0, (i + 0.5) / 8), 3) for i in range(8)]

# ---------------------------------------------------------------- static props
reset()
rng = random.Random(3)


def pmat(name, rgb, emit=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = 0.25
    if emit:
        b.inputs["Emission Color"].default_value = (*rgb, 1)
        b.inputs["Emission Strength"].default_value = emit
    return m


ink = pmat("Kraken_Ink", kmodel._lin("1E0B2B"))
foam = pmat("Kraken_Foam", kmodel._lin("EAF8FF"), 0.4)


def obj_from(name, verts, faces, m):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = False
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def ico(c, rad, jitter=0.08):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=rad, location=c)
    o = bpy.context.object
    for v in o.data.vertices:
        v.co *= 1 + rng.uniform(-jitter, jitter)
    return o


# ink blob: main + satellites, joined
parts = [ico((0, 0, 0), 0.45), ico((0.32, 0.1, 0.18), 0.2), ico((-0.28, -0.12, 0.22), 0.16), ico((0.05, 0.3, -0.25), 0.14)]
for p in parts:
    p.data.materials.append(ink)
bpy.ops.object.select_all(action="DESELECT")
for p in parts:
    p.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
blob = bpy.context.object
blob.name = "Kraken_InkBlob"
for p in blob.data.polygons:
    p.use_smooth = False
export(os.path.join(OUT, "Kraken_InkBlob.fbx"), [blob], False)
bpy.data.objects.remove(blob)

# ink puddle: irregular flat disk with thickness
N = 14
top, bot = [], []
verts = [(0, 0, 0.05)]
for j in range(N):
    a = 2 * math.pi * j / N
    rr = 1.6 * (1 + rng.uniform(-0.22, 0.18))
    verts.append((rr * math.cos(a), rr * math.sin(a), 0.04))
for j in range(N):
    a = 2 * math.pi * j / N
    x, y, _ = verts[1 + j]
    verts.append((x * 1.04, y * 1.04, 0.0))
faces = [(0, 1 + j, 1 + (j + 1) % N) for j in range(N)]
faces += [(1 + j, 1 + N + j, 1 + N + (j + 1) % N, 1 + (j + 1) % N) for j in range(N)]
puddle = obj_from("Kraken_InkPuddle", verts, faces, ink)
export(os.path.join(OUT, "Kraken_InkPuddle.fbx"), [puddle], False)
bpy.data.objects.remove(puddle)

# warning ripple: two flat rings
verts, faces = [], []
for (r_in, r_out) in ((1.25, 1.55), (0.6, 0.8)):
    s = len(verts)
    M = 16
    for j in range(M):
        a = 2 * math.pi * j / M
        verts.append((r_in * math.cos(a), r_in * math.sin(a), 0.03))
        verts.append((r_out * math.cos(a), r_out * math.sin(a), 0.03))
    for j in range(M):
        a0, b0 = s + 2 * j, s + 2 * j + 1
        a1, b1 = s + 2 * ((j + 1) % M), s + 2 * ((j + 1) % M) + 1
        faces.append((a0, b0, b1, a1))
ripple = obj_from("Kraken_WarningRipple", verts, faces, foam)
export(os.path.join(OUT, "Kraken_WarningRipple.fbx"), [ripple], False)

with open(os.path.join(OUT, "erupt_data.json"), "w") as f:
    json.dump({"length": 6.5, "radii": erupt_radii, "clips": er_clips, "events": {"hitStart": 0.02, "hitEnd": 0.55}}, f)
print("props done", os.listdir(OUT))
