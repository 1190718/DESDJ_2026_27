using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MistIsles.Core;
using MistIsles.Economy;
using MistIsles.Enemies;
using MistIsles.Guardian;
using MistIsles.Player;
using MistIsles.Rules;
using MistIsles.UI;
using MistIsles.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MistIsles.EditorTools
{
    /// <summary>
    /// Tools > Mist Isles menu. Builds everything the Still Shore slice needs from code, so the
    /// work arrives as scripts and assets instead of hand-edited scene YAML:
    ///   1. Build Assets: rule asset, materials, animator controllers, crab / guardian / shell prefabs.
    ///   2. Set Up Still Shore: adds the systems, player components, NavMesh, crabs, guardian,
    ///      mist, raft and card text to the open GameScene. Safe to run again (it reuses what exists).
    ///   3. Create Main Menu Scene, if Assets/Scenes/MainMenu.unity is missing.
    /// </summary>
    public static class MistIslesSetup
    {
        private const string Root = "Assets/MistIsles";
        private const string Gen = Root + "/Generated";
        private const string CrabFbx = Root + "/Art/Creatures/MistCrab.fbx";
        private const string GuardianFbx = Root + "/Art/Creatures/ShoreGuardian.fbx";
        private const string AudioFolder = Root + "/Audio";
        private const string RulePath = Gen + "/Rules/StillShore_Rule.asset";
        private const string CrabPrefabPath = Gen + "/Prefabs/MistCrab.prefab";
        private const string GuardianPrefabPath = Gen + "/Prefabs/ShoreGuardian.prefab";
        private const string ShellPrefabPath = Gen + "/Prefabs/ShellPickup.prefab";
        private const string PlayerControllerPath = Gen + "/Animators/MistIsles_Player.controller";
        private const string PlayerMaskPath = Gen + "/Animators/UpperBody.mask";
        private const string StarterControllerPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
        private const string HeroAttackFbx = "Assets/RPG Tiny Hero Duo/Animation/SwordAndShield/Attack01_SwordAndShiled.fbx";
        private const string RaftFbx = "Assets/EmaceArt - raft on the desert/Meshes/EA04_Wehicule_Raft_01a.fbx";

        private const int WaterLayer = 4;
        private const int IgnoreRaycastLayer = 2;
        private const int UILayer = 5;

        // ================================================================== 1. assets

        [MenuItem("Tools/Mist Isles/1. Build Assets", priority = 1)]
        public static void BuildAssets()
        {
            foreach (string f in new[] { Gen, Gen + "/Rules", Gen + "/Prefabs", Gen + "/Animators", Gen + "/Materials" })
                EnsureFolder(f);

            IslandRule rule = BuildRule();
            Dictionary<string, Material> crabMats = BuildCreatureMaterials("Crab", false);
            Dictionary<string, Material> guardMats = BuildCreatureMaterials("Guardian", true);
            GameObject shell = BuildShellPrefab();
            AnimatorController crabCtrl = BuildCreatureController(CrabFbx, Gen + "/Animators/MistCrab.controller", false);
            AnimatorController guardCtrl = BuildCreatureController(GuardianFbx, Gen + "/Animators/ShoreGuardian.controller", true);
            GameObject crab = BuildCrabPrefab(crabCtrl, crabMats, shell);
            BuildGuardianPrefab(guardCtrl, guardMats, crab);
            BuildPlayerController();

            AssetDatabase.SaveAssets();
            Debug.Log($"[Mist Isles] Assets built in {Gen}. Rule: {rule.RuleName}. Next: open GameScene and run Tools > Mist Isles > 2.");
        }

        private static IslandRule BuildRule()
        {
            var rule = AssetDatabase.LoadAssetAtPath<StillShoreRule>(RulePath);
            if (rule == null)
            {
                rule = ScriptableObject.CreateInstance<StillShoreRule>();
                AssetDatabase.CreateAsset(rule, RulePath);
            }
            rule.Configure(
                "Still Shore: they move only when you move",
                "The crabs watch you closely... try standing completely still.",
                "The hidden rule: on Still Shore, nothing moves unless you do. Stand still to freeze them!");
            EditorUtility.SetDirty(rule);
            return rule;
        }

        private static Dictionary<string, Material> BuildCreatureMaterials(string prefix, bool guardian)
        {
            var colours = guardian
                ? new Dictionary<string, Color> { ["Shell"] = Hex("3D4F63"), ["Belly"] = Hex("C9C2A8"), ["Claw"] = Hex("2F4052"), ["Tip"] = Hex("1B242E"), ["Eye"] = Hex("0E1114"), ["Spike"] = Hex("E3DCC4"), ["Core"] = Hex("3FE0C8") }
                : new Dictionary<string, Color> { ["Shell"] = Hex("7FA6B0"), ["Belly"] = Hex("E8DCC0"), ["Claw"] = Hex("5F8792"), ["Tip"] = Hex("2E3E44"), ["Eye"] = Hex("101214"), ["Spike"] = Hex("E8DCC0"), ["Core"] = Hex("3FE0C8") };
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            var result = new Dictionary<string, Material>();
            foreach (var kv in colours)
            {
                // The guardian's weak-point material must start with "Core" (ShoreGuardian looks it up by name).
                string name = kv.Key == "Core" ? $"Core_{prefix}" : $"{prefix}_{kv.Key}";
                string path = $"{Gen}/Materials/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(lit);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.SetColor("_BaseColor", kv.Value);
                mat.SetFloat("_Smoothness", kv.Key == "Shell" ? 0.45f : 0.25f);
                if (kv.Key == "Core")
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", kv.Value * 1.5f);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                EditorUtility.SetDirty(mat);
                result[kv.Key] = mat;
            }
            return result;
        }

        private static GameObject BuildShellPrefab()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "ShellPickup";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(0.38f, 0.14f, 0.32f);
            string matPath = Gen + "/Materials/ShellPickup.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetColor("_BaseColor", Hex("F7DCA0"));
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Hex("F7DCA0") * 0.35f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<CurrencyPickup>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, ShellPrefabPath);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static AnimatorController BuildCreatureController(string fbxPath, string path, bool guardian)
        {
            Dictionary<string, AnimationClip> clips = ClipsIn(fbxPath);
            if (clips.Count == 0)
                throw new System.Exception($"No animation clips found in {fbxPath}. Is Git LFS installed (git lfs pull)?");

            AssetDatabase.DeleteAsset(path);
            AnimatorController c = AnimatorController.CreateAnimatorControllerAtPath(path);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            foreach (string t in guardian ? new[] { "Attack", "Hit", "Die", "Slam", "Roar" } : new[] { "Attack", "Hit", "Die" })
                c.AddParameter(t, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = c.layers[0].stateMachine;
            AnimatorState idle = AddState(sm, "Idle", clips, new Vector3(300, 0));
            AnimatorState walk = AddState(sm, "Walk", clips, new Vector3(300, 100));
            sm.defaultState = idle;

            AnimatorStateTransition toWalk = idle.AddTransition(walk);
            toWalk.hasExitTime = false;
            toWalk.duration = 0.15f;
            toWalk.AddCondition(AnimatorConditionMode.Greater, 0.08f, "Speed");
            AnimatorStateTransition toIdle = walk.AddTransition(idle);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.15f;
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.08f, "Speed");

            int row = 0;
            foreach (string action in guardian ? new[] { "Attack", "Hit", "Slam", "Roar" } : new[] { "Attack", "Hit" })
            {
                AnimatorState s = AddState(sm, action, clips, new Vector3(600, row++ * 70));
                AnimatorStateTransition enter = sm.AddAnyStateTransition(s);
                enter.AddCondition(AnimatorConditionMode.If, 0f, action);
                enter.duration = 0.08f;
                enter.canTransitionToSelf = false;
                AnimatorStateTransition exit = s.AddTransition(idle);
                exit.hasExitTime = true;
                exit.exitTime = 0.92f;
                exit.duration = 0.12f;
            }
            AnimatorState die = AddState(sm, "Die", clips, new Vector3(600, row * 70));
            AnimatorStateTransition toDie = sm.AddAnyStateTransition(die);
            toDie.AddCondition(AnimatorConditionMode.If, 0f, "Die");
            toDie.duration = 0.1f;
            toDie.canTransitionToSelf = false;

            EditorUtility.SetDirty(c);
            return c;
        }

        private static AnimatorState AddState(AnimatorStateMachine sm, string name, Dictionary<string, AnimationClip> clips, Vector3 pos)
        {
            AnimatorState s = sm.AddState(name, pos);
            if (clips.TryGetValue(name, out AnimationClip clip))
                s.motion = clip;
            else
                Debug.LogWarning($"[Mist Isles] Clip '{name}' not found for state '{name}'.");
            return s;
        }

        private static Dictionary<string, AnimationClip> ClipsIn(string fbxPath)
        {
            var result = new Dictionary<string, AnimationClip>();
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    result[clip.name.Substring(clip.name.LastIndexOf('|') + 1)] = clip;
            return result;
        }

        private static GameObject InstantiateModel(string fbxPath, Transform parent, RuntimeAnimatorController controller, Dictionary<string, Material> mats, float scale)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (asset == null)
                throw new System.Exception($"Model not found: {fbxPath}");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            model.name = "Model";
            model.transform.SetParent(parent, false);
            model.transform.localScale = Vector3.one * scale;
            Animator anim = model.GetComponent<Animator>();
            if (anim == null)
                anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                Material[] slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string key = slots[i] != null ? slots[i].name.Split('.')[0] : "";
                    if (mats.TryGetValue(key, out Material m))
                        slots[i] = m;
                }
                r.sharedMaterials = slots;
            }
            return model;
        }

        private static GameObject BuildCrabPrefab(AnimatorController ctrl, Dictionary<string, Material> mats, GameObject shell)
        {
            var root = new GameObject("MistCrab");
            InstantiateModel(CrabFbx, root.transform, ctrl, mats, 1f);

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.55f;
            agent.height = 0.8f;
            agent.speed = 3.6f;
            agent.angularSpeed = 540f;
            agent.acceleration = 14f;
            agent.stoppingDistance = 0.6f;
            agent.autoBraking = true;

            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.direction = 0; // along X: a crab is wider than it is long
            col.radius = 0.35f;
            col.height = 1.3f;
            col.center = new Vector3(0f, 0.35f, 0f);

            Health health = root.AddComponent<Health>();
            SetField(health, "maxHealth", 3);
            EnemyBrain brain = root.AddComponent<EnemyBrain>();
            SetField(brain, "lootPrefab", shell);
            SetField(brain, "rule", AssetDatabase.LoadAssetAtPath<IslandRule>(RulePath));

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, CrabPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void BuildGuardianPrefab(AnimatorController ctrl, Dictionary<string, Material> mats, GameObject minion)
        {
            var root = new GameObject("ShoreGuardian");
            InstantiateModel(GuardianFbx, root.transform, ctrl, mats, 2.8f);

            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 1.3f;
            agent.height = 2.2f;
            agent.speed = 3.2f;
            agent.angularSpeed = 240f;
            agent.acceleration = 10f;
            agent.stoppingDistance = 2.2f;

            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.direction = 0;
            col.radius = 1.0f;
            col.height = 3.6f;
            col.center = new Vector3(0f, 1.0f, 0f);

            Health health = root.AddComponent<Health>();
            SetField(health, "maxHealth", 10);
            ShoreGuardian guardian = root.AddComponent<ShoreGuardian>();
            SetField(guardian, "minionPrefab", minion);
            SetField(guardian, "rule", AssetDatabase.LoadAssetAtPath<IslandRule>(RulePath));

            PrefabUtility.SaveAsPrefabAsset(root, GuardianPrefabPath);
            Object.DestroyImmediate(root);
        }

        /// <summary>
        /// Copies the Starter Assets controller and adds an upper-body "Combat" layer with the
        /// Tiny Hero sword swing, triggered by the "Attack" parameter. The original file is untouched.
        /// </summary>
        private static void BuildPlayerController()
        {
            AssetDatabase.DeleteAsset(PlayerControllerPath);
            if (!AssetDatabase.CopyAsset(StarterControllerPath, PlayerControllerPath))
            {
                Debug.LogWarning($"[Mist Isles] Could not copy {StarterControllerPath}; the player attack animation will be missing.");
                return;
            }
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
            if (c.parameters.All(p => p.name != "Attack"))
                c.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(PlayerMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                    mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
                foreach (AvatarMaskBodyPart part in new[] { AvatarMaskBodyPart.Root, AvatarMaskBodyPart.LeftLeg, AvatarMaskBodyPart.RightLeg, AvatarMaskBodyPart.LeftFootIK, AvatarMaskBodyPart.RightFootIK })
                    mask.SetHumanoidBodyPartActive(part, false);
                AssetDatabase.CreateAsset(mask, PlayerMaskPath);
            }

            c.AddLayer("Combat");
            AnimatorControllerLayer[] layers = c.layers;
            AnimatorControllerLayer combat = layers[layers.Length - 1];
            combat.defaultWeight = 1f;
            combat.avatarMask = mask;
            combat.blendingMode = AnimatorLayerBlendingMode.Override;
            c.layers = layers;

            AnimatorStateMachine sm = combat.stateMachine;
            AnimatorState empty = sm.AddState("Empty", new Vector3(300, 0));
            sm.defaultState = empty;
            AnimatorState attack = sm.AddState("Attack", new Vector3(300, 100));
            AnimationClip swing = AssetDatabase.LoadAllAssetsAtPath(HeroAttackFbx).OfType<AnimationClip>().FirstOrDefault(a => !a.name.StartsWith("__preview__"));
            if (swing == null)
                Debug.LogWarning($"[Mist Isles] No clip in {HeroAttackFbx} (Git LFS?). The attack still works, without animation.");
            attack.motion = swing;
            attack.speed = 1.4f;
            AnimatorStateTransition enter = sm.AddAnyStateTransition(attack);
            enter.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            enter.duration = 0.05f;
            enter.canTransitionToSelf = false;
            AnimatorStateTransition exit = attack.AddTransition(empty);
            exit.hasExitTime = true;
            exit.exitTime = 0.85f;
            exit.duration = 0.15f;
            EditorUtility.SetDirty(c);
        }

        // ================================================================== 2. scene

        [MenuItem("Tools/Mist Isles/2. Set Up Still Shore (Island 1) In Open Scene", priority = 2)]
        public static void SetUpStillShore()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CrabPrefabPath) == null)
                BuildAssets();

            GameObject island = GameObject.Find("Island_01");
            GameObject island2 = GameObject.Find("Island_02");
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Checkpoint card = island != null ? island.GetComponentInChildren<Checkpoint>(true) : null;
            if (island == null || player == null || card == null)
            {
                EditorUtility.DisplayDialog("Mist Isles", "Open GameScene first. Island_01 (with its Checkpoint) and the Player must exist.", "OK");
                return;
            }

            var log = new StringBuilder("[Mist Isles] Still Shore setup:\n");
            var rule = AssetDatabase.LoadAssetAtPath<IslandRule>(RulePath);

            SetUpSystems(log);
            SetUpPlayer(player, log);
            SetUpOcean(log);
            SetUpCamera(log);
            SetUpCard(card, log);

            Bounds area = IslandBounds(island);
            NavMeshSurface surface = BakeNavMesh(island, area, log);

            Vector3 anchor = NearestNavMeshPoint(card.transform.position, 40f) ?? NearestNavMeshPoint(player.transform.position, 10f) ?? island.transform.position;
            Vector3 playerStart = player.transform.position;

            Transform enemies = Child(island.transform, "MistIsles_Enemies");
            var crabPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrabPrefabPath);
            ClearChildren(enemies);
            List<Vector3> spots = FindSpots(anchor, 6f, 24f, 5, 5f, playerStart, 8f);
            foreach (Vector3 p in spots)
            {
                var crab = (GameObject)PrefabUtility.InstantiatePrefab(crabPrefab, enemies);
                crab.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                Undo.RegisterCreatedObjectUndo(crab, "Mist Crab");
            }
            log.AppendLine($"  Mist Crabs placed: {spots.Count}");

            Transform guardianParent = Child(island.transform, "MistIsles_Guardian");
            ClearChildren(guardianParent);
            Vector3 arena = FindArena(anchor, playerStart) ?? anchor + Vector3.forward * 10f;
            var guardianGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GuardianPrefabPath), guardianParent);
            Vector3 face = playerStart - arena;
            face.y = 0f;
            guardianGo.transform.SetPositionAndRotation(arena, face.sqrMagnitude > 0.01f ? Quaternion.LookRotation(face) : Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(guardianGo, "Shore Guardian");
            log.AppendLine($"  Guardian arena: {arena}");

            Transform respawn = Child(island.transform, "MistIsles_Respawn");
            respawn.SetPositionAndRotation(playerStart, player.transform.rotation);

            IslandController controller = GetOrAdd<IslandController>(island);
            IslandMist mist = SetUpMist(island, area);
            RaftTravel raft = SetUpRaft(island, island2, area, log);

            Undo.RecordObject(controller, "Configure island");
            controller.Configure("Still Shore", rule, guardianGo.GetComponent<ShoreGuardian>(), mist, raft, respawn, area);
            EditorUtility.SetDirty(controller);

            EditorSceneManager.MarkSceneDirty(island.scene);
            Selection.activeGameObject = island;
            log.AppendLine("Done. Save the scene (Ctrl+S), then press Play. Move crabs/guardian/raft in the scene view if a spot looks wrong.");
            Debug.Log(log.ToString());
        }

        private static void SetUpSystems(StringBuilder log)
        {
            GameObject sys = GameObject.Find("MistIsles_Systems");
            if (sys == null)
            {
                sys = new GameObject("MistIsles_Systems");
                Undo.RegisterCreatedObjectUndo(sys, "Systems");
            }
            AudioManager audio = GetOrAdd<AudioManager>(sys);
            GetOrAdd<Wallet>(sys);
            GetOrAdd<GameHUD>(sys);
            GetOrAdd<PauseMenu>(sys);
            GetOrAdd<ShopUI>(sys);

            var so = new SerializedObject(audio);
            SerializedProperty effects = so.FindProperty("effects");
            var ids = System.Enum.GetValues(typeof(Sfx)).Cast<Sfx>().ToArray();
            effects.arraySize = ids.Length;
            int missing = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                SerializedProperty e = effects.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("id").enumValueIndex = (int)ids[i];
                AudioClip clip = LoadClip(SnakeCase(ids[i].ToString()));
                if (clip == null)
                    missing++;
                e.FindPropertyRelative("clip").objectReferenceValue = clip;
                e.FindPropertyRelative("volume").floatValue = 1f;
            }
            so.FindProperty("ambience").objectReferenceValue = LoadClip("ambient_ocean");
            so.FindProperty("islandMusic").objectReferenceValue = LoadClip("music_island");
            so.FindProperty("guardianMusic").objectReferenceValue = LoadClip("music_guardian");
            so.ApplyModifiedProperties();
            log.AppendLine($"  Systems object ready (audio clips missing: {missing}).");
        }

        private static void SetUpPlayer(GameObject player, StringBuilder log)
        {
            Health h = GetOrAdd<Health>(player);
            SetField(h, "maxHealth", 5);
            SetField(h, "invulnerableAfterHit", 0.8f);
            GetOrAdd<PlayerMotionTracker>(player);
            GetOrAdd<PlayerCombat>(player);
            GetOrAdd<PlayerRespawn>(player);
            if (player.GetComponent<StarterAssets.StarterAssetsInputs>() != null)
                GetOrAdd<PlayerInputBlocker>(player);

            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath);
            Animator visual = player.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.isActiveAndEnabled && a.avatar != null && a.avatar.isHuman);
            if (visual != null && ctrl != null)
            {
                Undo.RecordObject(visual, "Player controller");
                visual.runtimeAnimatorController = ctrl;
                log.AppendLine($"  Player animator '{visual.name}' now uses {Path.GetFileName(PlayerControllerPath)} (adds the sword swing).");
            }
            else
                log.AppendLine("  WARNING: no humanoid Animator under the player; attack animation not assigned.");

            Transform sword = player.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("OHS"));
            if (sword != null && !sword.gameObject.activeSelf)
            {
                Undo.RecordObject(sword.gameObject, "Show sword");
                sword.gameObject.SetActive(true);
                log.AppendLine($"  Enabled the hero's sword ({sword.name}).");
            }
        }

        private static void SetUpOcean(StringBuilder log)
        {
            GameObject ocean = GameObject.Find("Ocean");
            if (ocean != null && ocean.layer != WaterLayer)
            {
                Undo.RecordObject(ocean, "Ocean layer");
                ocean.layer = WaterLayer;
                log.AppendLine("  Ocean moved to the Water layer (kept out of the NavMesh).");
            }
        }

        private static void SetUpCamera(StringBuilder log)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null && !data.renderPostProcessing)
            {
                Undo.RecordObject(data, "Post-processing");
                data.renderPostProcessing = true;
                log.AppendLine("  Main Camera post-processing enabled (needed for the mist colour effect).");
            }
        }

        private static void SetUpCard(Checkpoint card, StringBuilder log)
        {
            var so = new SerializedObject(card);
            so.FindProperty("question").stringValue = "The cartographer's card asks:\nWhat hidden rule governs Still Shore?";
            SerializedProperty answers = so.FindProperty("answers");
            string[] text =
            {
                "The crabs only move when you move",
                "The crabs only attack at night",
                "The crabs are afraid of the water",
                "The crabs follow the sound of your sword",
            };
            answers.arraySize = text.Length;
            for (int i = 0; i < text.Length; i++)
                answers.GetArrayElementAtIndex(i).stringValue = text[i];
            so.FindProperty("correctAnswerIndex").intValue = 0;
            so.FindProperty("hint").stringValue = "Stop walking and watch the crabs closely.";
            so.FindProperty("wrongAnswerLockSeconds").floatValue = 3f;
            so.ApplyModifiedProperties();
            log.AppendLine($"  Card '{card.name}' text set (answer 1 is correct).");
        }

        private static Bounds IslandBounds(GameObject island)
        {
            var b = new Bounds(island.transform.position, Vector3.one);
            bool first = true;
            foreach (Renderer r in island.GetComponentsInChildren<Renderer>())
            {
                if (r.gameObject.name.StartsWith("MistIsles") || r.GetComponentInParent<Checkpoint>() != null || r.GetComponentInParent<NavMeshAgent>() != null)
                    continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            b.Expand(new Vector3(12f, 40f, 12f));
            return b;
        }

        private static NavMeshSurface BakeNavMesh(GameObject island, Bounds area, StringBuilder log)
        {
            NavMeshSurface surface = GetOrAdd<NavMeshSurface>(island);
            Undo.RecordObject(surface, "NavMesh");
            surface.collectObjects = CollectObjects.Volume;
            surface.center = island.transform.InverseTransformPoint(area.center);
            surface.size = area.size;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.layerMask = ~((1 << WaterLayer) | (1 << IgnoreRaycastLayer) | (1 << UILayer));
            surface.BuildNavMesh();

            // Save the baked data as an asset next to the scene so it persists.
            string scenePath = island.scene.path;
            string dir = Path.Combine(Path.GetDirectoryName(scenePath) ?? "Assets/Scenes", Path.GetFileNameWithoutExtension(scenePath)).Replace('\\', '/');
            EnsureFolder(dir);
            string assetPath = dir + "/NavMesh-StillShore.asset";
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
            EditorUtility.SetDirty(surface);

            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            log.AppendLine($"  NavMesh baked over Still Shore ({tri.indices.Length / 3} triangles) → {assetPath}");
            return surface;
        }

        private static IslandMist SetUpMist(GameObject island, Bounds area)
        {
            Transform t = Child(island.transform, "MistIsles_Mist");
            t.position = area.center;
            t.gameObject.layer = 0; // URP cameras only blend volumes on the Default layer (volume layer mask)
            BoxCollider box = GetOrAdd<BoxCollider>(t.gameObject);
            box.isTrigger = true;
            box.center = Vector3.zero;
            box.size = area.size;
            GetOrAdd<Volume>(t.gameObject);
            IslandMist mist = GetOrAdd<IslandMist>(t.gameObject);
            SetField(mist, "island", island.GetComponent<IslandController>());
            return mist;
        }

        private static RaftTravel SetUpRaft(GameObject island, GameObject island2, Bounds area, StringBuilder log)
        {
            Transform raftRoot = Child(island.transform, "MistIsles_Raft");
            ClearChildren(raftRoot);
            Vector3 centre = area.center;
            Vector3 target = island2 != null ? island2.transform.position : centre + Vector3.forward * 60f;
            Vector3 dir = target - centre;
            dir.y = 0f;
            dir.Normalize();

            // Walk from the island centre toward Island_02; the last walkable point is the shore.
            Vector3 shore = centre;
            for (float d = 0f; d < 120f; d += 1f)
            {
                Vector3 p = centre + dir * d;
                if (NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                    shore = hit.position;
                else if (d > 10f && Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(shore.x, 0, shore.z)) > 6f)
                    break;
            }
            raftRoot.position = shore;
            raftRoot.rotation = Quaternion.LookRotation(dir);

            GameObject visual;
            var raftModel = AssetDatabase.LoadAssetAtPath<GameObject>(RaftFbx);
            if (raftModel != null)
            {
                visual = (GameObject)PrefabUtility.InstantiatePrefab(raftModel, raftRoot);
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.transform.SetParent(raftRoot, false);
                visual.transform.localScale = new Vector3(2.4f, 0.25f, 3.2f);
                Object.DestroyImmediate(visual.GetComponent<Collider>());
            }
            visual.name = "Visual";
            float waterY = GameObject.Find("Ocean") != null ? GameObject.Find("Ocean").transform.position.y : 0f;
            visual.transform.position = new Vector3(shore.x, waterY + 0.15f, shore.z) + dir * 3f;
            visual.transform.rotation = Quaternion.LookRotation(dir);

            Transform landing = Child(island.transform, "MistIsles_Island2_Landing");
            landing.position = FindLanding(island2, centre) ?? target + Vector3.up * 3f;
            Vector3 back = centre - landing.position;
            back.y = 0f;
            landing.rotation = back.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-back) : Quaternion.identity;

            RaftTravel raft = GetOrAdd<RaftTravel>(raftRoot.gameObject);
            Undo.RecordObject(raft, "Raft");
            raft.Configure(landing, "Whispering Pines", visual);
            EditorUtility.SetDirty(raft);
            log.AppendLine($"  Raft at {shore}; landing on Island_02 at {landing.position} (move 'MistIsles_Island2_Landing' if needed).");
            return raft;
        }

        private static Vector3? FindLanding(GameObject island2, Vector3 fromCentre)
        {
            if (island2 == null)
                return null;
            Physics.SyncTransforms();
            Vector3 c = island2.transform.position;
            foreach (Renderer r in island2.GetComponentsInChildren<Renderer>())
                c = Vector3.Lerp(c, r.bounds.center, 0.02f);
            Vector3 dir = fromCentre - c;
            dir.y = 0f;
            dir.Normalize();
            Vector3? best = null;
            for (float d = 0f; d < 80f; d += 1f)
            {
                Vector3 top = c + dir * d + Vector3.up * 200f;
                foreach (RaycastHit hit in Physics.RaycastAll(top, Vector3.down, 400f, ~(1 << WaterLayer), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                {
                    if (hit.transform.IsChildOf(island2.transform) && hit.normal.y > 0.7f)
                    {
                        best = hit.point + Vector3.up * 0.2f;
                        break;
                    }
                }
            }
            return best;
        }

        // ================================================================== 3. main menu

        [MenuItem("Tools/Mist Isles/3. Create Main Menu Scene (if missing)", priority = 3)]
        public static void CreateMainMenu()
        {
            const string path = "Assets/Scenes/MainMenu.unity";
            if (File.Exists(path))
            {
                EditorUtility.DisplayDialog("Mist Isles", "Assets/Scenes/MainMenu.unity already exists. Nothing to do.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = new Color(0.07f, 0.12f, 0.16f);

            var menuGo = new GameObject("MainMenu");
            MainMenu menu = menuGo.AddComponent<MainMenu>();
            Canvas canvas = UIFactory.CreateCanvas("Canvas", 0);
            var title = UIFactory.Text("Title", canvas.transform, "Mist Isles", 120, TMPro.TextAlignmentOptions.Center, UIFactory.Accent, TMPro.FontStyles.Bold | TMPro.FontStyles.SmallCaps);
            title.rectTransform.offsetMin = new Vector2(0f, 300f);
            var sub = UIFactory.Text("Subtitle", canvas.transform, "Every island hides a rule.", 36, TMPro.TextAlignmentOptions.Center, UIFactory.InkQuiet, TMPro.FontStyles.Italic);
            sub.rectTransform.offsetMin = new Vector2(0f, 120f);

            UnityEngine.UI.Button play = UIFactory.Button("Play", canvas.transform, "Play", null, new Vector2(360f, 80f), UIFactory.Accent);
            ((RectTransform)play.transform).anchoredPosition = new Vector2(0f, -80f);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(play.onClick, menu.PlayGame);
            UnityEngine.UI.Button quit = UIFactory.Button("Quit", canvas.transform, "Quit", null, new Vector2(360f, 80f));
            ((RectTransform)quit.transform).anchoredPosition = new Vector2(0f, -190f);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(quit.onClick, menu.QuitGame);

            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            EditorSceneManager.SaveScene(scene, path);
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Mist Isles] Created " + path + " and put it first in Build Settings.");
        }

        // ================================================================== helpers

        private static List<Vector3> FindSpots(Vector3 anchor, float minR, float maxR, int count, float spacing, Vector3 avoid, float avoidRadius)
        {
            var result = new List<Vector3>();
            var rng = new System.Random(1234);
            for (int attempt = 0; attempt < 400 && result.Count < count; attempt++)
            {
                double a = rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(minR, maxR, (float)rng.NextDouble());
                Vector3 p = anchor + new Vector3(Mathf.Cos((float)a), 0f, Mathf.Sin((float)a)) * r;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                    continue;
                if (!Reachable(anchor, hit.position))
                    continue;
                if (Vector3.Distance(hit.position, avoid) < avoidRadius)
                    continue;
                if (result.Any(q => Vector3.Distance(q, hit.position) < spacing))
                    continue;
                result.Add(hit.position);
            }
            return result;
        }

        private static Vector3? FindArena(Vector3 anchor, Vector3 avoid)
        {
            Vector3? best = null;
            float bestRoom = 0f;
            var rng = new System.Random(99);
            for (int i = 0; i < 300; i++)
            {
                double a = rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(9f, 18f, (float)rng.NextDouble());
                Vector3 p = anchor + new Vector3(Mathf.Cos((float)a), 0f, Mathf.Sin((float)a)) * r;
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, 4f, NavMesh.AllAreas) || !Reachable(anchor, hit.position))
                    continue;
                if (Vector3.Distance(hit.position, avoid) < 12f)
                    continue;
                if (NavMesh.FindClosestEdge(hit.position, out NavMeshHit edge, NavMesh.AllAreas) && edge.distance > bestRoom)
                {
                    bestRoom = edge.distance;
                    best = hit.position;
                }
            }
            return best;
        }

        private static bool Reachable(Vector3 from, Vector3 to)
        {
            var path = new NavMeshPath();
            return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        private static Vector3? NearestNavMeshPoint(Vector3 p, float radius)
        {
            if (NavMesh.SamplePosition(p, out NavMeshHit hit, radius, NavMesh.AllAreas))
                return hit.position;
            return null;
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null)
                return t;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(t.GetChild(i).gameObject);
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : Undo.AddComponent<T>(go);
        }

        private static void SetField(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogWarning($"[Mist Isles] Field '{field}' not found on {target.GetType().Name}.");
                return;
            }
            switch (value)
            {
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case bool b: p.boolValue = b; break;
                case string s: p.stringValue = s; break;
                case Object o: p.objectReferenceValue = o; break;
                case null: p.objectReferenceValue = null; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static AudioClip LoadClip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioFolder}/{name}.wav");

        private static string SnakeCase(string pascal)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                if (i > 0 && char.IsUpper(pascal[i]))
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(pascal[i]));
            }
            return sb.ToString();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out Color c);
            return c;
        }
    }
}
