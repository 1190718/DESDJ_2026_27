using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace MistIsles.Bosses.Kraken.EditorTools
{
    /// <summary>
    /// Tools > Mist Isles > Kraken > Setup Kraken Assets
    /// Configures the FBX importers (Generic rig, clip names, loops), creates URP materials, Animator Controllers
    /// and the prefabs: Kraken_Boss, Kraken_EruptTentacle, Kraken_InkBlob, Kraken_InkPuddle. Safe to run again.
    /// </summary>
    public static class KrakenSetup
    {
        const string Menu = "Tools/Mist Isles/Kraken/";

        // name -> (sRGB hex, emission intensity, smoothness)
        static readonly Dictionary<string, (string hex, float emission, float smooth)> Palette = new Dictionary<string, (string, float, float)>
        {
            { "Kraken_Body", ("6A3A9E", 0f, 0.25f) },
            { "Kraken_BodyDark", ("46206E", 0f, 0.25f) },
            { "Kraken_Belly", ("D9A6DA", 0f, 0.2f) },
            { "Kraken_Sucker", ("F6B3C4", 0f, 0.35f) },
            { "Kraken_Eye", ("FFC83A", 1.6f, 0.6f) },
            { "Kraken_Pupil", ("120614", 0f, 0.6f) },
            { "Kraken_Mouth", ("2A0B24", 0f, 0.1f) },
            { "Kraken_Teeth", ("F5EDD8", 0f, 0.4f) },
            { "Kraken_Crystal", ("7FF2FF", 2.5f, 0.8f) },
            { "Kraken_Ink", ("1E0B2B", 0f, 0.85f) },
            { "Kraken_Foam", ("EAF8FF", 0.4f, 0.3f) },
        };

        [MenuItem(Menu + "Setup Kraken Assets")]
        public static void Setup()
        {
            string root = FindRoot();
            if (root == null)
            {
                EditorUtility.DisplayDialog("Kraken setup", "Could not find Models/Kraken.fbx. Keep the folder structure from the package.", "OK");
                return;
            }
            string models = root + "/Models";
            string matDir = EnsureFolder(root, "Materials");
            string genDir = EnsureFolder(root, "Generated");
            string prefabDir = EnsureFolder(root, "Prefabs");

            try
            {
                EditorUtility.DisplayProgressBar("Kraken setup", "Materials", 0.1f);
                var mats = CreateMaterials(matDir);

                EditorUtility.DisplayProgressBar("Kraken setup", "Importing models", 0.3f);
                ConfigureModel(models + "/Kraken.fbx", true, new[] { "Idle", "Hidden", "Stunned_Loop" }, mats);
                ConfigureModel(models + "/Kraken_EruptTentacle.fbx", true, new[] { "Erupt_Hidden" }, mats);
                ConfigureModel(models + "/Kraken_InkBlob.fbx", false, null, mats);
                ConfigureModel(models + "/Kraken_InkPuddle.fbx", false, null, mats);
                ConfigureModel(models + "/Kraken_WarningRipple.fbx", false, null, mats);

                EditorUtility.DisplayProgressBar("Kraken setup", "Animator controllers", 0.55f);
                var krakenCtrl = BuildController(models + "/Kraken.fbx", genDir + "/Kraken.controller", "Hidden");
                var eruptCtrl = BuildController(models + "/Kraken_EruptTentacle.fbx", genDir + "/Kraken_EruptTentacle.controller", "Erupt_Hidden");

                EditorUtility.DisplayProgressBar("Kraken setup", "Prefabs", 0.75f);
                var puddle = BuildSimplePrefab<KrakenInkPuddle>(models + "/Kraken_InkPuddle.fbx", prefabDir + "/Kraken_InkPuddle.prefab");
                var blob = BuildSimplePrefab<KrakenInkBlob>(models + "/Kraken_InkBlob.fbx", prefabDir + "/Kraken_InkBlob.prefab");
                var erupt = BuildEruptPrefab(models, eruptCtrl, prefabDir + "/Kraken_EruptTentacle.prefab");
                var boss = BuildBossPrefab(models + "/Kraken.fbx", krakenCtrl, prefabDir + "/Kraken_Boss.prefab", blob, puddle, erupt);

                AssetDatabase.SaveAssets();
                Selection.activeObject = boss;
                EditorGUIUtility.PingObject(boss);
                Debug.Log("[Kraken] Setup complete: " + prefabDir + "/Kraken_Boss.prefab");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(Menu + "Create Shallow Water Zone")]
        public static void CreateWaterZone()
        {
            var go = new GameObject("Kraken_ShallowWaterZone");
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(6f, 1.5f, 4f);
            go.AddComponent<KrakenWaterZone>();
            if (SceneView.lastActiveSceneView != null) go.transform.position = SceneView.lastActiveSceneView.pivot;
            Undo.RegisterCreatedObjectUndo(go, "Create Kraken Water Zone");
            Selection.activeObject = go;
        }

        // ------------------------------------------------------------------ steps
        static string FindRoot()
        {
            foreach (var guid in AssetDatabase.FindAssets("Kraken t:Model"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (p.EndsWith("/Models/Kraken.fbx")) return p.Substring(0, p.Length - "/Models/Kraken.fbx".Length);
            }
            return null;
        }

        static string EnsureFolder(string parent, string name)
        {
            string p = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(p)) AssetDatabase.CreateFolder(parent, name);
            return p;
        }

        static Dictionary<string, Material> CreateMaterials(string dir)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var result = new Dictionary<string, Material>();
            foreach (var kv in Palette)
            {
                string path = dir + "/" + kv.Key + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null)
                {
                    m = new Material(shader);
                    AssetDatabase.CreateAsset(m, path);
                }
                else if (m.shader != shader) m.shader = shader;
                ColorUtility.TryParseHtmlString("#" + kv.Value.hex, out var c);
                m.SetColor("_BaseColor", c);
                m.SetColor("_Color", c);
                m.SetFloat("_Smoothness", kv.Value.smooth);
                m.SetFloat("_Glossiness", kv.Value.smooth);
                if (kv.Value.emission > 0f)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", c * kv.Value.emission);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                else
                {
                    m.DisableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", Color.black);
                }
                EditorUtility.SetDirty(m);
                result[kv.Key] = m;
            }
            return result;
        }

        static void ConfigureModel(string path, bool animated, string[] loops, Dictionary<string, Material> mats)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null)
            {
                Debug.LogWarning("[Kraken] Missing model: " + path);
                return;
            }
            imp.importCameras = false;
            imp.importLights = false;
            imp.importBlendShapes = false;
            imp.importVisibility = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var kv in mats)
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);

            if (animated)
            {
                imp.animationType = ModelImporterAnimationType.Generic;
                imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                imp.importAnimation = true;
                imp.SaveAndReimport();
                var clips = imp.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.name = ShortName(c.takeName);
                    c.loopTime = loops != null && loops.Contains(c.name);
                    c.loopPose = false;
                }
                imp.clipAnimations = clips;
            }
            else
            {
                imp.animationType = ModelImporterAnimationType.None;
                imp.importAnimation = false;
            }
            imp.SaveAndReimport();
        }

        static string ShortName(string take)
        {
            int i = take.LastIndexOf('|');
            return i >= 0 ? take.Substring(i + 1) : take;
        }

        static AnimatorController BuildController(string fbxPath, string ctrlPath, string defaultState)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            if (File.Exists(ctrlPath)) AssetDatabase.DeleteAsset(ctrlPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
            var sm = ctrl.layers[0].stateMachine;
            int i = 0;
            foreach (var clip in clips)
            {
                var st = sm.AddState(clip.name, new Vector3(300f + (i % 3) * 220f, 60f * (i / 3), 0f));
                st.motion = clip;
                st.writeDefaultValues = true;
                if (clip.name == defaultState) sm.defaultState = st;
                i++;
            }
            if (clips.Length == 0) Debug.LogWarning("[Kraken] No clips found in " + fbxPath);
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static GameObject Instantiate(string fbxPath, Transform parent)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            go.transform.SetParent(parent, false);
            return go;
        }

        static T BuildSimplePrefab<T>(string fbxPath, string prefabPath) where T : Component
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            Instantiate(fbxPath, root.transform);
            root.AddComponent<T>();
            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return saved.GetComponent<T>();
        }

        static KrakenEruptTentacle BuildEruptPrefab(string models, AnimatorController ctrl, string prefabPath)
        {
            var root = new GameObject("Kraken_EruptTentacle");
            var model = Instantiate(models + "/Kraken_EruptTentacle.fbx", root.transform);
            var anim = SetupAnimator(model, ctrl);
            var ripple = Instantiate(models + "/Kraken_WarningRipple.fbx", root.transform);
            ripple.name = "WarningRipple";
            ripple.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ripple.SetActive(false);
            var comp = root.AddComponent<KrakenEruptTentacle>();
            comp.animator = anim;
            comp.warningRipple = ripple;
            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return saved.GetComponent<KrakenEruptTentacle>();
        }

        static Animator SetupAnimator(GameObject model, AnimatorController ctrl)
        {
            var anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = ctrl;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            return anim;
        }

        static GameObject BuildBossPrefab(string fbxPath, AnimatorController ctrl, string prefabPath,
            KrakenInkBlob blob, KrakenInkPuddle puddle, KrakenEruptTentacle erupt)
        {
            var root = new GameObject("Kraken_Boss");
            var model = Instantiate(fbxPath, root.transform);
            var anim = SetupAnimator(model, ctrl);

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var boss = root.AddComponent<KrakenBoss>();
            boss.animator = anim;
            boss.inkBlobPrefab = blob;
            boss.inkPuddlePrefab = puddle;
            boss.eruptTentaclePrefab = erupt;

            // body blockers (solid capsules following the body bones)
            string[] bodyBones = { "Body_Base", "Body_Mid", "Body_Top" };
            for (int i = 0; i < bodyBones.Length; i++)
            {
                var bone = KrakenGeometry.FindDeep(model.transform, bodyBones[i]);
                if (bone == null) continue;
                Vector3 a = root.transform.TransformPoint(KrakenAnimData.BodyAxisRest[i]);
                Vector3 b = root.transform.TransformPoint(KrakenAnimData.BodyAxisRest[i + 1]);
                float r = KrakenAnimData.BodyRadii[i];
                var go = new GameObject("Blocker_" + bodyBones[i]);
                go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation((b - a).normalized, Vector3.forward));
                go.transform.SetParent(bone, true);
                var cap = go.AddComponent<CapsuleCollider>();
                float s = Mathf.Max(0.0001f, go.transform.lossyScale.x);
                cap.direction = 2;
                cap.radius = r / s;
                cap.height = ((b - a).magnitude + 2f * r) / s;
            }

            // weak point
            var wpBone = KrakenGeometry.FindDeep(model.transform, "WeakPoint");
            if (wpBone != null)
            {
                var wp = new GameObject("WeakPoint_Collider");
                wp.transform.position = root.transform.TransformPoint(KrakenAnimData.WeakPointCenterRest);
                wp.transform.SetParent(wpBone, true);
                var sc = wp.AddComponent<SphereCollider>();
                sc.isTrigger = true;
                sc.radius = KrakenAnimData.WeakPointRadius / Mathf.Max(0.0001f, wp.transform.lossyScale.x);
                var wrb = wp.AddComponent<Rigidbody>();
                wrb.isKinematic = true;
                wrb.useGravity = false;
                wp.AddComponent<KrakenWeakPoint>().boss = boss;
            }
            else Debug.LogWarning("[Kraken] WeakPoint bone not found.");

            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }
    }
}
