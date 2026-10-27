"""usage: python render_previews.py BLEND OUTDIR [engine] [clips...]"""
import sys, os, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
blend, outdir = sys.argv[1], sys.argv[2]
engine = sys.argv[3] if len(sys.argv) > 3 else "BLENDER_WORKBENCH"
only = sys.argv[4:]
bpy.ops.wm.open_mainfile(filepath=blend)
import kscene
# brighten less: emission down for preview exposure
cam = kscene.setup(res=(480, 300), engine=engine)
sc = bpy.context.scene
for l in [o for o in sc.objects if o.type == 'LIGHT']:
    l.data.energy = 2.4
rig = bpy.data.objects["Kraken_Rig"]
import kanim
os.makedirs(outdir, exist_ok=True)
VIEWS = {"main": ((16, -25, 9.5), (0, -4, 2.5))}
for name in (only or list(kanim.CLIPS)):
    act = bpy.data.actions[name]
    rig.animation_data.action = act
    fn, dur, loop = kanim.CLIPS[name]
    n = int(round(dur * 30))
    picks = sorted(set([0] + [int(round(n * k / 7)) for k in range(1, 8)]))
    ev = kanim.EVENTS.get(name, {})
    for k, v in ev.items():
        f = int(round(v * 30))
        if 0 < f < n and all(abs(f - p) > 3 for p in picks):
            picks.append(f)
    picks = sorted(picks)[:10]
    for f in picks:
        sc.frame_set(f)
        sc.render.filepath = os.path.join(outdir, f"{name}__{f:03d}.png")
        bpy.ops.render.render(write_still=True)
    print("done", name, picks, flush=True)
