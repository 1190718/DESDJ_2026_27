import sys, bpy, math
from mathutils import Vector
blend, out = sys.argv[1], sys.argv[2]
bpy.ops.wm.open_mainfile(filepath=blend)
sc = bpy.context.scene
rig = [o for o in sc.objects if o.type == 'ARMATURE'][0]
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'
sc.display.shading.color_type = 'MATERIAL'
sc.display.shading.show_shadows = True
sc.render.resolution_x = 360; sc.render.resolution_y = 300
sc.render.film_transparent = False
w = bpy.data.worlds.new("W"); sc.world = w
cam = bpy.data.cameras.new("C"); co = bpy.data.objects.new("C", cam); sc.collection.objects.link(co); sc.camera = co
cam.type = 'ORTHO'; cam.ortho_scale = 2.2
# camera in front-right-above (front is -Y)
pos = Vector((-1.3, -2.6, 1.4)); co.location = pos
co.rotation_euler = (Vector((0, 0, 0.25)) - pos).to_track_quat('-Z', 'Y').to_euler()
# ground
bpy.ops.mesh.primitive_plane_add(size=6); 
acts = [t.name for t in rig.animation_data.nla_tracks]
for a in acts:
    act = bpy.data.actions[a]
    rig.animation_data.action = act
    fr = int(act.frame_range[1])
    for k, f in enumerate([0, fr // 3, fr // 2, (2 * fr) // 3]):
        sc.frame_set(f)
        sc.render.filepath = f"{out}/{a}_{k}.png"
        bpy.ops.render.render(write_still=True)
print("ACTS", acts)
