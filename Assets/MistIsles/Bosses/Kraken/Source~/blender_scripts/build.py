"""Build kraken, bake all clips, export FBX + .blend + anim data JSON.
usage: python build.py OUTDIR"""
import sys, os, json, math, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
from mathutils import Vector

OUT = sys.argv[1] if len(sys.argv) > 1 else "/home/claude/kraken/out"
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
import kmodel, kanim

rig, mesh = kmodel.build_kraken()
rig.animation_data_create()

SCALE_BONES = {"Body_Top", "Eye_L", "Eye_R", "WeakPoint"}
LOC_BONES = {"Root", "Body_Base"} | {f"T_{n}_00" for n in kmodel.TENT_ORDER}


def bake(name):
    fn, dur, loop = kanim.CLIPS[name]
    nf = int(round(dur * kanim.FPS))
    frames = list(range(nf + 1))
    data = {}
    for f in frames:
        ch = kanim.evaluate(name, f / kanim.FPS)
        for bone, (rot, loc, sc) in ch.items():
            d = data.setdefault(bone, ([], [], []))
            d[0].append([math.radians(r) for r in rot])
            d[1].append(loc)
            d[2].append(sc)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    for bone, (rots, locs, scs) in data.items():
        chans = [("rotation_euler", rots)]
        if bone in LOC_BONES:
            chans.append(("location", locs))
        if bone in SCALE_BONES:
            chans.append(("scale", scs))
        for prop, vals in chans:
            for idx in range(3):
                fc = act.fcurve_ensure_for_datablock(rig, f'pose.bones["{bone}"].{prop}', index=idx, group_name=bone)
                kp = fc.keyframe_points
                kp.add(len(frames))
                co = []
                for f, v in zip(frames, vals):
                    co += [f, v[idx]]
                kp.foreach_set("co", co)
                for k in kp:
                    k.interpolation = "LINEAR"
                fc.update()
    act.frame_range = (0, nf)
    try:
        act.use_frame_range = True
    except Exception:
        pass
    return act, nf


t0 = time.time()
actions = {}
for name in kanim.CLIPS:
    actions[name] = bake(name)
    print("baked", name, actions[name][1], "frames")
print("bake time", round(time.time() - t0, 1))


# ---------------------------------------------------------------- FK probes (Unity space)
def to_unity(v):
    return [round(-v.x, 3), round(v.z, 3), round(-v.y, 3)]


def bone_head(b):
    pb = rig.pose.bones[b]
    return rig.matrix_world @ pb.head


def bone_tail(b):
    pb = rig.pose.bones[b]
    return rig.matrix_world @ pb.tail


def at(name, tsec):
    rig.animation_data.action = actions[name][0]
    bpy.context.scene.frame_set(int(round(tsec * kanim.FPS)))
    bpy.context.view_layer.update()


probes = {}
for nm, tent in (("Attack_Slam_L", "FL"), ("Attack_Slam_R", "FR")):
    at(nm, 1.3)
    pts = [bone_head(f"T_{tent}_{i:02d}") for i in range(2, 8)] + [bone_tail(f"T_{tent}_07")]
    probes[nm] = {"impactPoints": [to_unity(p) for p in pts],
                  "heights": [round(p.z, 2) for p in pts]}
# sweep arc: sample tip heading over hit window
arc = []
for k in range(0, 13):
    tt = 0.72 + k * (1.55 - 0.72) / 12
    at("Attack_Sweep", tt)
    p = bone_tail("T_FR_07")
    m = bone_head("T_FR_04")
    arc.append({"t": round(tt, 3), "tip": to_unity(p), "mid": to_unity(m)})
probes["Attack_Sweep"] = {"arc": arc}
at("Attack_Grab", 1.15)
probes["Attack_Grab"] = {"grabPoint": to_unity((bone_head("T_FL_06") + bone_tail("T_FL_07")) / 2)}
at("Attack_Ink", 1.0)
probes["Attack_Ink"] = {"mouth": to_unity(bone_head("Mouth_Socket"))}
at("Stunned_Loop", 0.0)
probes["Stunned_Loop"] = {"weakPoint": to_unity(bone_head("WeakPoint")),
                          "frontTentacleL": [to_unity(bone_head(f"T_FL_{i:02d}")) for i in (2, 4, 6)]}
at("Idle", 0.0)
probes["Idle"] = {"weakPoint": to_unity(bone_head("WeakPoint")), "mouth": to_unity(bone_head("Mouth_Socket"))}

# tentacle radii per bone (for hurt capsules)
radii = {}
for n in kmodel.TENT_ORDER:
    h, rd, bz, L, r0 = kmodel.TENTACLES[n]
    radii[n] = [round(kmodel.tent_radius(r0, (i + 0.5) / kmodel.NB), 3) for i in range(kmodel.NB)]

c_core, n_core = kmodel.core_frame()
rest = {
    "weakPointCenter": to_unity(c_core + n_core * 0.55), "weakPointRadius": 1.15,
    "bodyAxis": [to_unity(Vector(v)) for v in ((0, 0, -1.0), (0, 0, 2.6), (0, kmodel.body_cy(4.6), 4.6), (0, kmodel.body_cy(7.4), 7.4))],
    "bodyRadii": [2.7, 2.35, 2.3],
}
animdata = {
    "rest": rest,
    "fps": kanim.FPS,
    "clips": {n: {"length": round(actions[n][1] / kanim.FPS, 4), "loop": kanim.CLIPS[n][2], "events": kanim.EVENTS[n]} for n in kanim.CLIPS},
    "probes": probes,
    "tentacleRadii": radii,
}
with open(os.path.join(OUT, "kraken_animdata.json"), "w") as f:
    json.dump(animdata, f, indent=1)

# export from a clean bind pose (prefab default pose in Unity = rest pose)
rig.animation_data.action = None
for pb in rig.pose.bones:
    pb.location = (0, 0, 0)
    pb.rotation_euler = (0, 0, 0)
    pb.scale = (1, 1, 1)
bpy.context.scene.frame_set(0)
bpy.context.scene.render.fps = kanim.FPS

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Kraken.blend"))

# ---------------------------------------------------------------- FBX
for o in bpy.context.scene.objects:
    o.select_set(o in (rig, mesh))
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "Kraken.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
    mesh_smooth_type="FACE", add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
    armature_nodetype="NULL", bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True, bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0, path_mode="AUTO")
print("exported", os.path.getsize(os.path.join(OUT, "Kraken.fbx")) // 1024, "KB")
print(json.dumps(probes, indent=0)[:3000])
