#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.AI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Fit.Combat;
using Fit.Combat.Weapon;
using Fit.Player;
using Fit.Enemies;
using Fit.Feedback;

namespace Fit.Editor
{
    /// <summary>
    /// 灰盒测试场景一键生成器。
    ///
    /// 【为什么需要它】
    /// 阶段 1 的战斗骨架（FPS 控制器 / 武器 / 敌人 AI / 弹幕 / 可读性提示）全部是
    /// 纯 MonoBehaviour，但还没有一个能"跑起来调手感"的场景。
    /// 手动搭：地面 + 玩家三层相机 + 武器 SO + 投射物 prefab + 敌人 prefab +
    /// 弹幕模式 SO + 刷怪点 + 烘焙 NavMesh……至少 20 步，且引用极易漏绑。
    ///
    /// 这个菜单（Fit / 生成灰盒测试场景）把上述全部自动化：
    ///   - 生成所有 ScriptableObject（Weapon / BulletPattern）与 prefab（Projectile / Enemy / ThreatIcon）
    ///   - 按文档要求的三层相机结构搭玩家，并正确绑定所有 private 引用
    ///   - 在玩家正面扇形内放 4 个方块敌人 + 一组刷怪点
    ///   - 烘焙 NavMesh、加灯光与环境光
    ///   - 保存并打开场景
    ///
    /// 装好 Unity 后点一次菜单即可进 Play 调手感，无需任何手动配置。
    /// 所有 private [SerializeField] 引用通过 SerializedObject 设置，与 Inspector 手动拖拽等价。
    /// </summary>
    public sealed class GreyboxSceneGenerator
    {
        private const string ScenePath = "Assets/Scenes/GreyboxTest.unity";
        private const string MatDir = "Assets/Materials";
        private const string PrefabDir = "Assets/Prefabs";
        private const string WeaponDir = "Assets/ScriptableObjects/Weapons";
        private const string PatternDir = "Assets/ScriptableObjects/BulletPatterns";

        [MenuItem("Fit/生成灰盒测试场景")]
        public static void Generate()
        {
            EnsureDir(MatDir);
            EnsureDir(PrefabDir);
            EnsureDir(WeaponDir);
            EnsureDir(PatternDir);

            var groundMat = SaveMaterial("M_Ground", new Color(0.22f, 0.22f, 0.26f));
            var enemyMat = SaveMaterial("M_Enemy", new Color(0.82f, 0.26f, 0.30f));
            var playerMat = SaveMaterial("M_Player", new Color(0.30f, 0.62f, 0.92f));
            var bulletMat = SaveMaterial("M_Bullet", new Color(1f, 0.62f, 0.16f));

            var weapon = CreateWeaponData();
            var projectilePrefab = CreateProjectilePrefab(bulletMat);
            var pattern = CreateBulletPattern(projectilePrefab);
            var enemyPrefab = CreateEnemyPrefab(enemyMat, pattern);
            var iconPrefab = CreateThreatIconPrefab();

            BuildScene(weapon, enemyPrefab, iconPrefab, groundMat, playerMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Fit] 灰盒测试场景已生成并打开：" + ScenePath +
                      "\n操作：WASD 移动 / 鼠标视角 / 左键开火 / Shift 冲刺 / Ctrl 翻滚(无敌帧) / R 换弹 / E 救援队友");
        }

        // -------- 目录 --------

        private static void EnsureDir(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            var parts = dir.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string parent = cur;
                cur = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(cur))
                    AssetDatabase.CreateFolder(parent, parts[i]);
            }
        }

        // -------- 材质 --------

        private static Material SaveMaterial(string name, Color c)
        {
            // 优先 URP Lit（项目是 URP），回退 Standard；两种都尝试写主色属性
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", c);
            mat.SetColor("_Color", c);
            AssetDatabase.CreateAsset(mat, $"{MatDir}/{name}.mat");
            return mat;
        }

        // -------- ScriptableObject --------

        private static WeaponData CreateWeaponData()
        {
            var w = ScriptableObject.CreateInstance<WeaponData>();
            w.Id = "test_pistol";
            w.DisplayName = "测试手枪（Hitscan）";
            w.Mode = FireMode.Hitscan;
            w.Trigger = TriggerType.Auto;
            w.Damage = 25f;
            w.FireRate = 6f;
            w.MagazineSize = 30;
            w.ReloadSeconds = 1.6f;
            w.SpreadDegrees = 1.5f;
            w.Range = 120f;
            AssetDatabase.CreateAsset(w, $"{WeaponDir}/Weapon_TestPistol.asset");
            return w;
        }

        private static BulletPattern CreateBulletPattern(GameObject projectilePrefab)
        {
            var p = ScriptableObject.CreateInstance<BulletPattern>();
            p.Shape = PatternShape.Spread;
            p.Count = 5;
            p.ArcDegrees = 50f;
            p.ProjectilePrefab = projectilePrefab.GetComponent<Projectile>();
            p.Speed = 18f;      // 慢速可预判（见 BulletPattern 类说明）
            p.Damage = 12f;
            p.Lifetime = 5f;
            p.AimAtTarget = true;
            p.AimJitterDegrees = 2f;
            AssetDatabase.CreateAsset(p, $"{PatternDir}/Pattern_TestSpread.asset");
            return p;
        }

        // -------- Prefab --------

        private static GameObject CreateProjectilePrefab(Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Projectile_Test";
            Object.DestroyImmediate(go.GetComponent<Collider>());   // 投射物用 SphereCast，不需要碰撞体
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * 0.3f;

            go.AddComponent<Projectile>();        // RequireComponent 会自动挂 ProjectileVisual
            go.AddComponent<ProjectileVisual>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/Projectile_Test.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateEnemyPrefab(Material mat, BulletPattern pattern)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Enemy_TestBlock";
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            go.transform.localScale = new Vector3(1.2f, 1.6f, 1.2f);

            var enemy = go.AddComponent<EnemyBase>();   // RequireComponent 自动加 Health / Telegraph / NavMeshAgent
            var telegraph = enemy.GetComponent<Telegraph>();

            // 枪口锚点
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(go.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 1f, 0.9f);

            // EnemyBase 需要手工绑定的引用
            var eso = new SerializedObject(enemy);
            SetRef(eso, "_pattern", pattern);
            SetRef(eso, "_muzzle", muzzle.transform);
            eso.ApplyModifiedProperties();

            // 让前摇充能光效打在方块本体上（ID-008 可读性）
            var tso = new SerializedObject(telegraph);
            SetRef(tso, "_chargeRenderer", renderer);
            tso.ApplyModifiedProperties();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/Enemy_TestBlock.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static GameObject CreateThreatIconPrefab()
        {
            var go = new GameObject("ThreatIcon");
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 0.22f, 0.22f, 0.9f);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(40f, 40f);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/ThreatIcon.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // -------- 场景 --------

        private static void BuildScene(
            WeaponData weapon, GameObject enemyPrefab, GameObject iconPrefab,
            Material groundMat, Material playerMat)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 灯光 + 环境光（空场景默认全黑）
            var light = new GameObject("Directional Light");
            var lit = light.AddComponent<Light>();
            lit.type = LightType.Directional;
            lit.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.40f, 0.40f, 0.45f);

            // 地面（标记 Navigation Static 供 NavMesh 烘焙）
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            ground.transform.localScale = new Vector3(8f, 1f, 8f);   // Plane 默认 10x10 → 80x80
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.NavigationStatic);

            // 玩家
            var player = BuildPlayer(weapon, playerMat);
            player.transform.position = new Vector3(0f, 1.1f, 0f);
            player.tag = "Player";

            // 敌人：玩家正前方一字排开（正面扇形内，符合 §5.1 冲突一）
            for (int i = 0; i < 4; i++)
            {
                var e = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab);
                e.transform.position = new Vector3((i - 1.5f) * 6f, 1f, 12f);
            }

            // 刷怪点（空 Transform，供未来 CombatRoom 接入；目前直接放了敌人）
            var spawnRoot = new GameObject("SpawnPoints");
            for (int i = 0; i < 4; i++)
            {
                var sp = new GameObject($"Spawn_{i}");
                sp.transform.SetParent(spawnRoot.transform, false);
                sp.transform.position = new Vector3((i - 1.5f) * 6f, 1f, 14f);
            }

            // 屏幕边缘威胁提示（ID-009）
            BuildThreatIndicator(iconPrefab);

            // 烘焙 NavMesh（若项目用 NavMeshComponents，可能需手动补 NavMeshSurface；失败不阻断）
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
        }

        private static GameObject BuildPlayer(WeaponData weapon, Material playerMat)
        {
            var player = new GameObject("Player");
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            player.AddComponent<FPSController>();
            player.AddComponent<Health>();
            player.AddComponent<DownedState>();
            var weaponBase = player.AddComponent<WeaponBase>();
            player.AddComponent<PlayerMotor>();   // Awake 内自动 GetComponent 引用，无需手绑

            // 三层相机结构（见 PlayerCamera 类注释）：yaw / pitch / 后坐力各占一层
            var holder = new GameObject("CameraHolder");
            holder.transform.SetParent(player.transform, false);
            holder.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var pc = holder.AddComponent<PlayerCamera>();

            var cam = new GameObject("Main Camera");
            cam.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.transform.SetParent(holder.transform, false);
            cam.transform.localPosition = Vector3.zero;

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(cam.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, -0.1f, 0.6f);

            // WeaponBase 引用（private，用 SerializedObject 设置）
            var wso = new SerializedObject(weaponBase);
            SetRef(wso, "_startWeapon", weapon);
            SetRef(wso, "_muzzle", muzzle.transform);
            SetRef(wso, "_aimCamera", cam.GetComponent<Camera>());
            wso.ApplyModifiedProperties();

            // PlayerCamera 引用（它挂 CameraHolder，GetComponent<Camera> 拿不到，必须手绑）
            var pso = new SerializedObject(pc);
            SetRef(pso, "_controller", player.GetComponent<FPSController>());
            SetRef(pso, "_weapon", weaponBase);
            SetRef(pso, "_camera", cam.GetComponent<Camera>());
            pso.ApplyModifiedProperties();

            return player;
        }

        private static void BuildThreatIndicator(GameObject iconPrefab)
        {
            var canvasGO = new GameObject("ThreatCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var ti = canvasGO.AddComponent<ThreatIndicator>();
            var mainCam = GameObject.FindWithTag("MainCamera");
            var so = new SerializedObject(ti);
            SetRef(so, "_canvas", canvas);
            SetRef(so, "_indicatorPrefab", iconPrefab);
            SetRef(so, "_camera", mainCam != null ? mainCam.GetComponent<Camera>() : null);
            so.ApplyModifiedProperties();
        }

        // -------- 工具 --------

        private static void SetRef(SerializedObject so, string prop, Object val)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.objectReferenceValue = val;
            else Debug.LogWarning($"[Fit] 生成器：找不到字段 {prop}，请核对运行时脚本字段名");
        }
    }
}
#endif
