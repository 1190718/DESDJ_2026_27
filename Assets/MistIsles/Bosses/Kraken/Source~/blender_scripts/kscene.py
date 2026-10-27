"""Preview scene: water, a shore slab, a hero-sized stand-in, camera, lights."""
import bpy, math
from mathutils import Vector


def mat(name, rgb, rough=0.8, alpha=1.0, emit=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    if alpha < 1:
        b.inputs["Alpha"].default_value = alpha
    if emit:
        b.inputs["Emission Color"].default_value = (*rgb, 1)
        b.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = (*rgb, alpha)
    return m


def add_mesh(name, verts, faces, m):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def setup(res=(640, 400), cam_loc=(15, -24, 9), target=(0, -4, 2.5), lens=32, engine="BLENDER_EEVEE", shore=True):
    sc = bpy.context.scene
    sc.render.engine = engine
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    if engine == "BLENDER_EEVEE":
        try:
            sc.eevee.taa_render_samples = 16
        except Exception:
            pass
    if engine == "BLENDER_WORKBENCH":
        sh = sc.display.shading
        sh.light = "STUDIO"
        sh.color_type = "MATERIAL"
        sh.show_shadows = True
        sh.show_cavity = False
    w = bpy.data.worlds.new("Sky")
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.42, 0.62, 0.92, 1)
    bg.inputs["Strength"].default_value = 0.9
    sc.world = w
    sc.view_settings.view_transform = "Standard"

    # water
    water = mat("Water", (0.18, 0.36, 0.78), 0.15, alpha=0.82)
    add_mesh("Water", [(-60, -60, 0), (60, -60, 0), (60, 60, 0), (-60, 60, 0)], [(0, 1, 2, 3)], water)
    if shore:
        sand = mat("Sand", (0.80, 0.74, 0.60))
        grass = mat("Grass", (0.30, 0.62, 0.22))
        # shore slab starting ~6.5 m in front, top at 0.6
        v = [(-14, -6.5, -1), (14, -6.5, -1), (14, -6.8, 0.55), (-14, -6.8, 0.55),
             (-14, -30, 0.9), (14, -30, 0.9), (14, -30, -1), (-14, -30, -1)]
        add_mesh("Shore", v, [(0, 1, 2, 3), (3, 2, 5, 4), (1, 6, 5, 2), (0, 3, 4, 7), (4, 5, 6, 7)], sand)
        add_mesh("Grass", [(-14, -16, 0.86), (14, -16, 0.86), (14, -30, 0.95), (-14, -30, 0.95)], [(0, 1, 2, 3)], grass)
        # hero stand-in 1.8 m
        hero = mat("Hero", (0.95, 0.45, 0.12))
        bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.35, depth=1.8, location=(1.5, -10.0, 0.62 + 0.9))
        h = bpy.context.object
        h.data.materials.append(hero)
        h.name = "Hero"
    cam_d = bpy.data.cameras.new("Cam")
    cam_d.lens = lens
    cam = bpy.data.objects.new("Cam", cam_d)
    sc.collection.objects.link(cam)
    cam.location = cam_loc
    d = Vector(target) - Vector(cam_loc)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    sun_d = bpy.data.lights.new("Sun", "SUN")
    sun_d.energy = 3.5
    sun_d.color = (1.0, 0.95, 0.85)
    sun = bpy.data.objects.new("Sun", sun_d)
    sc.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(30))
    return cam


def aim(cam, loc, target):
    cam.location = loc
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
