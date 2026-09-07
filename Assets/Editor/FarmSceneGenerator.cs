#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.AI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Fit.Combat;
using Fit.Combat.Weapon;
using Fit.Enemies;
using Fit.Player;
using Fit.World;

namespace Fit.Editor
{
    /// <summary>
    /// 主地图 · 农场（Farm Hub）生成器 —— 菜单 Fit / 农场 / ...
    ///
    /// 【本次范围（已拍板 H1-H4）】
    ///   H1 不做出发传送门 / 选关界面   → 场景里不放任何出发装置
    ///   H2 蔬菜不掉落资源             → 不挂任何掉落组件，也不用改存档结构
    ///   H3 只做 3 种蔬菜              → 胡萝卜兵（近战）/ 番茄投手（远程）/ 玉米粒射手（弹幕）
    ///   H4 番茄不要抛物线             → 直线慢速弹，不改 Projectile（省 0.5 人天）
    ///   本次侧重：场地 + 角色模型
    ///
    /// 【蔬菜为什么用程序化建模而不是导入 fbx】
    /// 项目是 Q1 低分辨率 3D（640×360 放大）+ 低多边形风格，
    /// 而蔬菜的形状本质就是基本几何体的组合（胡萝卜=锥、番茄=球、玉米=胶囊）。
    /// 程序化生成的好处：
    ///   1. 零外部资产依赖，仓库里不用塞二进制，改形状就是改几个数
    ///   2. 尺寸/比例可以精确对齐碰撞体与 NavMeshAgent，不会出现"模型浮空"
    ///   3. 后面美术出正式模型时，直接替换 Model 子节点即可，AI 与绑定不用动
    ///
    /// 【模型层级约定】
    ///   Enemy_Xxx（根节点：EnemyBase / Health / Telegraph / NavMeshAgent / CapsuleCollider）
    ///     ├─ Model（VeggieIdle 摇摆 —— 与 AI 的朝向控制解耦，见 VeggieIdle 注释）
    ///     │    └─ Body / 叶 / 腿 / 眼 ...
    ///     └─ Muzzle（弹幕发射点）
    /// </summary>
    public sealed class FarmSceneGenerator
    {
        private const string ScenePath = "Assets/Scenes/FarmHub.unity";
        private const string PrefabDir = "Assets/Prefabs/Veggies";
        private const string PatternDir = "Assets/ScriptableObjects/BulletPatterns";
        private const string WeaponDir = "Assets/ScriptableObjects/Weapons";

        // Unity 内置基本体缩放基准：Cube/Sphere 直径 1；Capsule/Cylinder/Cone 直径 1、高 2；Plane 10×10

        // ------------------------------------------------------------------ 菜单

        [MenuItem("Fit/农场/生成蔬菜小怪 Prefab")]
        public static void GenerateVeggies()
        {
            var carrot = BuildCarrot();
            var tomato = BuildTomato();
            var corn = BuildCorn();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Fit] 蔬菜小怪已生成：\n  " +
                      AssetDatabase.GetAssetPath(carrot) + "\n  " +
                      AssetDatabase.GetAssetPath(tomato) + "\n  " +
                      AssetDatabase.GetAssetPath(corn));
        }

        [MenuItem("Fit/农场/生成主地图场景（农场）")]
        public static void GenerateFarm()
        {
            var carrot = BuildCarrot();
            var tomato = BuildTomato();
            var corn = BuildCorn();

            BuildScene(carrot, tomato, corn);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Fit] 农场主地图已生成并打开：" + ScenePath +
                      "\n操作：WASD 移动 / 鼠标视角 / 左键开火 / Shift 冲刺 / Ctrl 翻滚(无敌帧) / R 换弹" +
                      "\n本次按 H1-H4：无传送门、无掉落、3 种蔬菜、番茄走直线弹");
        }

        // ------------------------------------------------------------------ 蔬菜模型

        /// <summary>
        /// 胡萝卜兵 —— 近战，教学用（★）。
        /// 跳起顶撞用"极短命的近战弹"近似：现有 EnemyBase 只支持 pattern 发射，
        /// 真正的近战判定要等阶段 3 加近战攻击类型。这里先把射程压到贴脸距离。
        /// </summary>
        private static GameObject BuildCarrot()
        {
            var root = new GameObject("Enemy_Carrot");
            var model = FitEditorUtils.Node(root.transform, "Model", Vector3.zero);
            model.gameObject.AddComponent<VeggieIdle>();

            var orange = Mat("Veg_Carrot", new Color(0.95f, 0.45f, 0.10f));
            var leaf = Mat("Veg_Leaf", new Color(0.30f, 0.62f, 0.22f));
            var eye = Mat("Veg_Eye", new Color(0.06f, 0.05f, 0.05f));
            var shine = Mat("Veg_EyeShine", Color.white);

            // 身体：Cone 默认尖朝上，绕 X 转 180° 让尖朝下（胡萝卜该有的样子）
            // scale(0.62, 0.45, 0.62) → 底半径 0.31、高 0.90；局部 y=0.55 → 占 y∈[0.10, 1.00]
            var body = FitEditorUtils.Part(
                model, PrimitiveType.Cone, "Body", orange,
                0f, 0.55f, 0f, 0.62f, 0.45f, 0.62f, 180f, 0f, 0f);

            // 缨子：4 片叶子从顶部粗端向外张开
            for (int i = 0; i < 4; i++)
            {
                var pivot = FitEditorUtils.Node(model, $"LeafPivot_{i}", new Vector3(0f, 0.96f, 0f));
                pivot.localEulerAngles = new Vector3(-30f, i * 90f, 0f);
                FitEditorUtils.Part(pivot, PrimitiveType.Capsule, $"Leaf_{i}", leaf,
                    0f, 0.17f, 0f, 0.10f, 0.17f, 0.10f);
            }

            // 腿：撑住悬空的身体尖端
            FitEditorUtils.Part(model, PrimitiveType.Capsule, "Leg_L", orange,
                -0.14f, 0.15f, 0f, 0.10f, 0.075f, 0.10f);
            FitEditorUtils.Part(model, PrimitiveType.Capsule, "Leg_R", orange,
                0.14f, 0.15f, 0f, 0.10f, 0.075f, 0.10f);

            // 眼睛：贴在 y=0.62 处的锥面上（该高度锥面半径约 0.18，取 z=0.155 略微嵌入）
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Eye_L", eye,
                -0.075f, 0.62f, 0.155f, 0.11f, 0.11f, 0.11f);
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Eye_R", eye,
                0.075f, 0.62f, 0.155f, 0.11f, 0.11f, 0.11f);
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Shine_L", shine,
                -0.095f, 0.655f, 0.20f, 0.045f, 0.045f, 0.045f);
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Shine_R", shine,
                0.095f, 0.655f, 0.20f, 0.045f, 0.045f, 0.045f);

            var bump = MakeProjectile("Projectile_CarrotBump", Mat("Proj_Carrot", new Color(1f, 0.55f, 0.15f)),
                PrimitiveType.Cone, 0.24f);
            var pattern = MakePattern("Pattern_CarrotBump", PatternShape.Single, bump,
                count: 1, arc: 0f, speed: 10f, damage: 6f, lifetime: 0.22f);

            return FinalizeVeggie(root, body, pattern, new VeggieTuning
            {
                MaxHealth = 30f,
                MoveSpeed = 3.0f,
                AggroRange = 16f,
                AttackRange = 2.2f,        // 贴脸才出手
                PreferredDistance = 1.3f,
                TelegraphSeconds = 0.4f,
                AttackCooldown = 1.8f,
                CapsuleRadius = 0.30f,
                CapsuleHeight = 1.05f,
                MuzzlePos = new Vector3(0f, 0.55f, 0.38f),
                ChargeColor = new Color(1f, 0.55f, 0.15f),
            });
        }

        /// <summary>番茄投手 —— 远程单发（★★）。H4：直线慢速弹，不做抛物线。</summary>
        private static GameObject BuildTomato()
        {
            var root = new GameObject("Enemy_Tomato");
            var model = FitEditorUtils.Node(root.transform, "Model", Vector3.zero);
            model.gameObject.AddComponent<VeggieIdle>();

            var red = Mat("Veg_Tomato", new Color(0.88f, 0.20f, 0.16f));
            var stem = Mat("Veg_Stem", new Color(0.32f, 0.58f, 0.22f));
            var eye = Mat("Veg_Eye", new Color(0.06f, 0.05f, 0.05f));
            var shine = Mat("Veg_EyeShine", Color.white);

            // 身体：略扁的球，中心 y=0.50 → 占 y∈[0.15, 0.85]
            var body = FitEditorUtils.Part(
                model, PrimitiveType.Sphere, "Body", red,
                0f, 0.50f, 0f, 0.80f, 0.70f, 0.80f);

            // 蒂：5 片萼片绕顶辐射 + 一根小茎
            for (int i = 0; i < 5; i++)
            {
                var pivot = FitEditorUtils.Node(model, $"SepalPivot_{i}", new Vector3(0f, 0.82f, 0f));
                pivot.localEulerAngles = new Vector3(-14f, i * 72f, 0f);
                FitEditorUtils.Part(pivot, PrimitiveType.Cube, $"Sepal_{i}", stem,
                    0f, 0f, 0.12f, 0.07f, 0.025f, 0.22f);
            }
            FitEditorUtils.Part(model, PrimitiveType.Cylinder, "Stalk", stem,
                0f, 0.90f, 0f, 0.05f, 0.05f, 0.05f);

            // 手：梗做的细手臂，向外张开
            FitEditorUtils.Part(model, PrimitiveType.Capsule, "Arm_L", stem,
                -0.42f, 0.52f, 0.06f, 0.075f, 0.09f, 0.075f, 0f, 0f, 22f);
            FitEditorUtils.Part(model, PrimitiveType.Capsule, "Arm_R", stem,
                0.42f, 0.52f, 0.06f, 0.075f, 0.09f, 0.075f, 0f, 0f, -22f);

            // 脚
            FitEditorUtils.Part(model, PrimitiveType.Capsule, "Foot_L", stem,
                -0.17f, 0.055f, 0.04f, 0.11f, 0.045f, 0.15f);
            FitEditorUtils.Part(model, PrimitiveType.Capsule, "Foot_R", stem,
                0.17f, 0.055f, 0.04f, 0.11f, 0.045f, 0.15f);

            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Eye_L", eye,
                -0.155f, 0.60f, 0.335f, 0.115f, 0.115f, 0.115f);
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Eye_R", eye,
                0.155f, 0.60f, 0.335f, 0.115f, 0.115f, 0.115f);
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Shine_L", shine,
                -0.185f, 0.638f, 0.375f, 0.048f, 0.048f, 0.048f);
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Shine_R", shine,
                0.185f, 0.638f, 0.375f, 0.048f, 0.048f, 0.048f);

            var proj = MakeProjectile("Projectile_Tomato", Mat("Proj_Tomato", new Color(0.90f, 0.22f, 0.16f)),
                PrimitiveType.Sphere, 0.26f);
            var pattern = MakePattern("Pattern_TomatoToss", PatternShape.Single, proj,
                count: 1, arc: 0f, speed: 14f, damage: 8f, lifetime: 3f);

            return FinalizeVeggie(root, body, pattern, new VeggieTuning
            {
                MaxHealth = 40f,
                MoveSpeed = 2.6f,
                AggroRange = 22f,
                AttackRange = 16f,
                PreferredDistance = 9f,
                TelegraphSeconds = 0.5f,
                AttackCooldown = 2.4f,
                CapsuleRadius = 0.40f,
                CapsuleHeight = 0.78f,
                MuzzlePos = new Vector3(0f, 0.72f, 0.40f),
                ChargeColor = new Color(1f, 0.30f, 0.12f),
            });
        }

        /// <summary>玉米粒射手 —— 首个弹幕型（★★★），教玩家读扇形。</summary>
        private static GameObject BuildCorn()
        {
            var root = new GameObject("Enemy_Corn");
            var model = FitEditorUtils.Node(root.transform, "Model", Vector3.zero);
            model.gameObject.AddComponent<VeggieIdle>();

            var yellow = Mat("Veg_Corn", new Color(0.96f, 0.82f, 0.24f));
            var kernel = Mat("Veg_CornKernel", new Color(1f, 0.88f, 0.36f));
            var husk = Mat("Veg_Husk", new Color(0.42f, 0.66f, 0.26f));
            var eye = Mat("Veg_Eye", new Color(0.06f, 0.05f, 0.05f));
            var shine = Mat("Veg_EyeShine", Color.white);

            // 身体：胶囊天然两头收，像玉米棒。scale.y=0.40 → 总高 0.80；中心 y=0.55 → y∈[0.15, 0.95]
            var body = FitEditorUtils.Part(
                model, PrimitiveType.Capsule, "Body", yellow,
                0f, 0.55f, 0f, 0.50f, 0.40f, 0.50f);

            // 玉米粒：两圈小球点缀（低多边形下比贴图省事，也更有 Q 版颗粒感）
            for (int ring = 0; ring < 2; ring++)
            {
                float y = ring == 0 ? 0.50f : 0.70f;
                for (int i = 0; i < 3; i++)
                {
                    float yaw = i * 120f + ring * 60f;
                    var pivot = FitEditorUtils.Node(model, $"KP_{ring}_{i}", new Vector3(0f, y, 0f));
                    pivot.localEulerAngles = new Vector3(0f, yaw, 0f);
                    FitEditorUtils.Part(pivot, PrimitiveType.Sphere, $"Kernel_{ring}_{i}", kernel,
                        0f, 0f, 0.235f, 0.13f, 0.13f, 0.13f);
                }
            }

            // 苞叶：3 片贴身向外张开
            for (int i = 0; i < 3; i++)
            {
                var pivot = FitEditorUtils.Node(model, $"HuskPivot_{i}", new Vector3(0f, 0.58f, 0f));
                pivot.localEulerAngles = new Vector3(-22f, i * 120f + 30f, 0f);
                FitEditorUtils.Part(pivot, PrimitiveType.Cube, $"Husk_{i}", husk,
                    0f, -0.06f, 0.27f, 0.22f, 0.50f, 0.035f);
            }

            // 玉米须
            for (int i = 0; i < 3; i++)
            {
                var pivot = FitEditorUtils.Node(model, $"Silk_{i}", new Vector3(0f, 0.93f, 0f));
                pivot.localEulerAngles = new Vector3(-24f, i * 120f, 0f);
                FitEditorUtils.Part(pivot, PrimitiveType.Cylinder, "Silk", kernel,
                    0f, 0.07f, 0f, 0.025f, 0.07f, 0.025f);
            }

            // 手臂（苞叶做的）
            FitEditorUtils.Part(model, PrimitiveType.Capsule, "Arm_L", husk,
                -0.30f, 0.60f, 0.05f, 0.07f, 0.10f, 0.07f, 0f, 0f, 26f);
            FitEditorUtils.Part(model, PrimitiveType.Capsule, "Arm_R", husk,
                0.30f, 0.60f, 0.05f, 0.07f, 0.10f, 0.07f, 0f, 0f, -26f);

            FitEditorUtils.Part(model, PrimitiveType.Capsule, "Foot_L", husk,
                -0.13f, 0.06f, 0.03f, 0.10f, 0.05f, 0.13f);
            FitEditorUtils.Part(model, PrimitiveType.Capsule, "Foot_R", husk,
                0.13f, 0.06f, 0.03f, 0.10f, 0.05f, 0.13f);

            // 眼睛贴在圆柱段（y=0.68 处半径 0.25）
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Eye_L", eye,
                -0.105f, 0.68f, 0.215f, 0.105f, 0.105f, 0.105f);
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Eye_R", eye,
                0.105f, 0.68f, 0.215f, 0.105f, 0.105f, 0.105f);
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Shine_L", shine,
                -0.13f, 0.715f, 0.25f, 0.044f, 0.044f, 0.044f);
            FitEditorUtils.Part(model, PrimitiveType.Sphere, "Shine_R", shine,
                0.13f, 0.715f, 0.25f, 0.044f, 0.044f, 0.044f);

            var proj = MakeProjectile("Projectile_CornKernel",
                Mat("Proj_Corn", new Color(1f, 0.86f, 0.30f)), PrimitiveType.Capsule, 0.16f);
            var pattern = MakePattern("Pattern_CornSpread", PatternShape.Spread, proj,
                count: 3, arc: 24f, speed: 16f, damage: 7f, lifetime: 3f);

            return FinalizeVeggie(root, body, pattern, new VeggieTuning
            {
                MaxHealth = 50f,
                MoveSpeed = 2.4f,
                AggroRange = 24f,
                AttackRange = 18f,
                PreferredDistance = 11f,
                TelegraphSeconds = 0.45f,
                AttackCooldown = 2.6f,
                CapsuleRadius = 0.28f,
                CapsuleHeight = 0.98f,
                MuzzlePos = new Vector3(0f, 0.70f, 0.36f),
                ChargeColor = new Color(1f, 0.80f, 0.25f),
            });
        }

        // ------------------------------------------------------------------ 蔬菜装配

        private struct VeggieTuning
        {
            public float MaxHealth;
            public float MoveSpeed;
            public float AggroRange;
            public float AttackRange;
            public float PreferredDistance;
            public float TelegraphSeconds;
            public float AttackCooldown;
            public float CapsuleRadius;
            public float CapsuleHeight;
            public Vector3 MuzzlePos;
            public Color ChargeColor;
        }

        /// <summary>
        /// 给蔬菜根节点挂战斗组件、绑引用、存 prefab。
        /// 数值刻意比地牢敌人温和（SCENE_FARM.md §3.1）：主地图是练手的地方，不是受苦的地方。
        /// </summary>
        private static GameObject FinalizeVeggie(GameObject root, GameObject body,
            BulletPattern pattern, VeggieTuning t)
        {
            // 碰撞：整个蔬菜一个胶囊即可，零件上的 collider 已在 Part() 里删掉
            var col = root.AddComponent<CapsuleCollider>();
            col.radius = t.CapsuleRadius;
            col.height = t.CapsuleHeight;
            col.center = new Vector3(0f, t.CapsuleHeight * 0.5f, 0f);

            var enemy = root.AddComponent<EnemyBase>();   // RequireComponent 自动带 Health / Telegraph / NavMeshAgent

            var agent = root.GetComponent<NavMeshAgent>();
            agent.baseOffset = 0f;              // 模型脚底就在 y=0，不需要偏移
            agent.radius = t.CapsuleRadius;
            agent.height = t.CapsuleHeight;
            agent.acceleration = 12f;
            agent.angularSpeed = 240f;
            agent.stoppingDistance = 0.6f;

            var muzzle = FitEditorUtils.Node(root.transform, "Muzzle", t.MuzzlePos);

            var eso = new SerializedObject(enemy);
            FitEditorUtils.SetFloat(eso, "_aggroRange", t.AggroRange);
            FitEditorUtils.SetFloat(eso, "_attackRange", t.AttackRange);
            FitEditorUtils.SetFloat(eso, "_moveSpeed", t.MoveSpeed);
            FitEditorUtils.SetFloat(eso, "_preferredDistance", t.PreferredDistance);
            FitEditorUtils.SetFloat(eso, "_telegraphSeconds", t.TelegraphSeconds);
            FitEditorUtils.SetFloat(eso, "_attackCooldown", t.AttackCooldown);
            FitEditorUtils.SetRef(eso, "_pattern", pattern);
            FitEditorUtils.SetRef(eso, "_muzzle", muzzle);
            eso.ApplyModifiedProperties();

            var hso = new SerializedObject(root.GetComponent<Health>());
            FitEditorUtils.SetFloat(hso, "_maxHealth", t.MaxHealth);
            hso.ApplyModifiedProperties();

            // 前摇充能光效打在身体本体上（ID-007/008）。
            // 充能色按蔬菜本体色取，玩家一眼能分辨"哪个菜要动手了"
            var tso = new SerializedObject(root.GetComponent<Telegraph>());
            FitEditorUtils.SetRef(tso, "_chargeRenderer", body.GetComponent<Renderer>());
            FitEditorUtils.SetColor(tso, "_chargeColor", t.ChargeColor);
            FitEditorUtils.SetFloat(tso, "_chargeIntensity", 5f);
            tso.ApplyModifiedProperties();

            FitEditorUtils.EnsureDir(PrefabDir);
            var path = $"{PrefabDir}/{root.name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static BulletPattern MakePattern(string name, PatternShape shape,
            GameObject projectilePrefab, int count, float arc,
            float speed, float damage, float lifetime)
        {
            var p = ScriptableObject.CreateInstance<BulletPattern>();
            p.Shape = shape;
            p.Count = count;
            p.ArcDegrees = arc;
            p.ProjectilePrefab = projectilePrefab?.GetComponent<Projectile>();
            p.Speed = speed;      // 主地图比地牢更慢（14-16 vs 18），以练手为主
            p.Damage = damage;
            p.Lifetime = lifetime;
            p.AimAtTarget = true;
            p.AimJitterDegrees = 3f;

            FitEditorUtils.EnsureDir(PatternDir);
            AssetDatabase.CreateAsset(p, $"{PatternDir}/{name}.asset");
            return p;
        }

        private static GameObject MakeProjectile(string name, Material mat, PrimitiveType type, float size)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());   // 投射物用 SphereCast，不需要碰撞体
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.localScale = Vector3.one * size;

            go.AddComponent<Projectile>();          // RequireComponent 自动带 ProjectileVisual
            go.AddComponent<ProjectileVisual>();

            FitEditorUtils.EnsureDir(PrefabDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------ 场地

        private static void BuildScene(GameObject carrot, GameObject tomato, GameObject corn)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildEnvironment();
            BuildTerrain();
            BuildFence();
            BuildVeggiePlots();

            BuildFarmhouse(new Vector3(-18f, 0f, -16f), YawTo(-18f, -16f));
            BuildBarn(new Vector3(18f, 0f, -14f), YawTo(18f, -14f));
            BuildWell(new Vector3(10f, 0f, 2f));
            BuildWindmill(new Vector3(-16f, 0f, 8f), YawTo(-16f, 8f));
            BuildHayPiles();
            BuildTrees();

            var player = BuildPlayer();
            player.transform.position = new Vector3(0f, 0f, -6f);
            player.transform.rotation = Quaternion.identity;   // 面向 +Z，正对菜园

            // 蔬菜分布：按难度从近到远（胡萝卜 → 番茄 → 玉米），玩家往前走就是一趟教学曲线
            PlaceVeggie(carrot, -9.0f, 3.0f);
            PlaceVeggie(carrot, -4.5f, 5.0f);
            PlaceVeggie(carrot, 4.5f, 5.0f);
            PlaceVeggie(tomato, 9.0f, 3.0f);
            PlaceVeggie(tomato, -9.0f, 11.0f);
            PlaceVeggie(corn, -4.5f, 13.0f);
            PlaceVeggie(corn, 4.5f, 13.0f);
            PlaceVeggie(corn, 9.0f, 11.0f);

            BuildSpawnPoints();

            NavMeshBuilder.BuildNavMesh();

            FitEditorUtils.EnsureDir("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>把物体朝向院子中心（0,0,0）。</summary>
        private static float YawTo(float x, float z) => Mathf.Atan2(-x, -z) * Mathf.Rad2Deg;

        private static void BuildEnvironment()
        {
            var light = new GameObject("Sun");
            var lit = light.AddComponent<Light>();
            lit.type = LightType.Directional;
            lit.color = new Color(1f, 0.96f, 0.86f);   // 暖黄日光
            lit.intensity = 1.25f;
            lit.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 明亮户外 + 降饱和天空：天空是弹幕的背景板，
            // 太蓝太抢眼会把子弹吃掉（§5.1 冲突一 / SCENE_FARM §2）
            var sky = new Color(0.74f, 0.82f, 0.88f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.56f, 0.58f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = sky;
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 220f;
            _skyColor = sky;
        }

        private static Color _skyColor = new(0.74f, 0.82f, 0.88f);

        private static void BuildTerrain()
        {
            // 远景大地面（不标记 NavigationStatic —— NavMesh 只覆盖院子，蔬菜不会跑出篱笆）
            var field = GameObject.CreatePrimitive(PrimitiveType.Plane);
            field.name = "Field";
            field.GetComponent<MeshRenderer>().sharedMaterial = Mat("Farm_Field", new Color(0.62f, 0.60f, 0.34f));
            field.transform.localScale = new Vector3(30f, 1f, 30f);   // 300×300

            // 院子地面（可走区域，标记 NavigationStatic）
            var yard = GameObject.CreatePrimitive(PrimitiveType.Plane);
            yard.name = "Yard";
            yard.GetComponent<MeshRenderer>().sharedMaterial = Mat("Farm_Ground", new Color(0.40f, 0.58f, 0.26f));
            yard.transform.localScale = new Vector3(6f, 1f, 6f);      // 60×60
            yard.transform.position = new Vector3(0f, 0.02f, 0f);
            GameObjectUtility.SetStaticEditorFlags(yard, StaticEditorFlags.NavigationStatic);

            // 远山：纯背景，不可达
            var mountain = Mat("Farm_Mountain", new Color(0.58f, 0.63f, 0.70f));
            float[] angles = { 20f, 75f, 130f, 200f, 255f, 320f };
            foreach (var a in angles)
            {
                float rad = a * Mathf.Deg2Rad;
                var m = GameObject.CreatePrimitive(PrimitiveType.Cone);
                m.name = "Mountain";
                Object.DestroyImmediate(m.GetComponent<Collider>());
                m.GetComponent<MeshRenderer>().sharedMaterial = mountain;
                m.transform.position = new Vector3(Mathf.Sin(rad) * 130f, 0f, Mathf.Cos(rad) * 130f);
                m.transform.localScale = new Vector3(90f, 22f, 90f);
            }
        }

        private static void BuildFence()
        {
            var wood = Mat("Farm_Wood", new Color(0.55f, 0.38f, 0.22f));
            var woodDark = Mat("Farm_WoodDark", new Color(0.42f, 0.28f, 0.16f));

            var fence = new GameObject("Fence");
            const float half = 28f;      // 院子半径
            const float step = 2.5f;

            // 四条边：沿边摆木桩，桩之间架两根横杆
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float fixedCoord = side % 2 == 0 ? -half : half;

                for (float t = -half; t <= half + 0.01f; t += step)
                {
                    float x = alongX ? t : fixedCoord;
                    float z = alongX ? fixedCoord : t;

                    var post = FitEditorUtils.Part(fence.transform, PrimitiveType.Cube, "Post", wood,
                        x, 0.55f, z, 0.14f, 1.10f, 0.14f);
                    post.transform.localEulerAngles = new Vector3(0f, alongX ? 0f : 90f, 0f);

                    if (t + step <= half + 0.01f)
                    {
                        float mx = alongX ? t + step * 0.5f : fixedCoord;
                        float mz = alongX ? fixedCoord : t + step * 0.5f;
                        float len = step;

                        for (int r = 0; r < 2; r++)
                        {
                            float y = 0.42f + r * 0.40f;
                            var rail = FitEditorUtils.Part(fence.transform, PrimitiveType.Cube, "Rail", woodDark,
                                mx, y, mz,
                                alongX ? len : 0.07f, 0.09f, alongX ? 0.07f : len);
                            rail.transform.localEulerAngles = Vector3.zero;
                        }
                    }
                }
            }
        }

        private static void BuildVeggiePlots()
        {
            var soil = Mat("Farm_Soil", new Color(0.42f, 0.30f, 0.19f));
            var soilDark = Mat("Farm_SoilDark", new Color(0.32f, 0.22f, 0.13f));

            var plots = new GameObject("VeggiePlots");
            float[] xs = { -6.75f, 6.75f };
            float[] zs = { 4f, 12f };

            foreach (var px in xs)
            {
                foreach (var pz in zs)
                {
                    var plot = FitEditorUtils.Node(plots.transform, $"Plot_{px}_{pz}", new Vector3(px, 0f, pz));

                    // 畦土
                    var bed = FitEditorUtils.Part(plot, PrimitiveType.Cube, "Soil", soil,
                        0f, 0.08f, 0f, 11f, 0.16f, 5f);

                    // 田垄
                    for (int i = 0; i < 4; i++)
                    {
                        FitEditorUtils.Part(plot, PrimitiveType.Cube, "Ridge", soilDark,
                            0f, 0.17f, -1.7f + i * 1.13f, 11f, 0.10f, 0.55f);
                    }
                }
            }
        }

        private static void BuildFarmhouse(Vector3 pos, float yaw)
        {
            var house = new GameObject("Farmhouse");
            house.transform.position = pos;
            house.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var wall = Mat("Farm_Wall", new Color(0.88f, 0.84f, 0.74f));
            var roof = Mat("Farm_Roof", new Color(0.55f, 0.20f, 0.16f));
            var wood = Mat("Farm_WoodDark", new Color(0.42f, 0.28f, 0.16f));
            var glass = Mat("Farm_Glass", new Color(0.62f, 0.76f, 0.84f));

            FitEditorUtils.Part(house.transform, PrimitiveType.Cube, "Walls", wall,
                0f, 2.0f, 0f, 8f, 4f, 7f);

            // 人字屋顶：两块斜板交叉。Cylinder 改不成三棱柱，用旋转的 Box 最稳
            var roofL = FitEditorUtils.Part(house.transform, PrimitiveType.Cube, "Roof_L", roof,
                -2.05f, 5.05f, 0f, 5.2f, 0.26f, 7.6f, 0f, 0f, 38f);
            var roofR = FitEditorUtils.Part(house.transform, PrimitiveType.Cube, "Roof_R", roof,
                2.05f, 5.05f, 0f, 5.2f, 0.26f, 7.6f, 0f, 0f, -38f);

            // 门与窗（朝 +Z，也就是朝向院子中心）
            FitEditorUtils.Part(house.transform, PrimitiveType.Cube, "Door", wood,
                0f, 1.1f, 3.55f, 1.3f, 2.2f, 0.16f);
            FitEditorUtils.Part(house.transform, PrimitiveType.Cube, "Window_L", glass,
                -2.6f, 2.2f, 3.55f, 1.3f, 1.3f, 0.14f);
            FitEditorUtils.Part(house.transform, PrimitiveType.Cube, "Window_R", glass,
                2.6f, 2.2f, 3.55f, 1.3f, 1.3f, 0.14f);

            AddObstacle(house, new Vector3(8.4f, 5.4f, 7.4f), new Vector3(0f, 2.7f, 0f));
        }

        private static void BuildBarn(Vector3 pos, float yaw)
        {
            var barn = new GameObject("Barn");
            barn.transform.position = pos;
            barn.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var wall = Mat("Farm_BarnWall", new Color(0.62f, 0.17f, 0.14f));
            var roof = Mat("Farm_BarnRoof", new Color(0.36f, 0.16f, 0.14f));
            var trim = Mat("Farm_Trim", new Color(0.92f, 0.91f, 0.88f));

            FitEditorUtils.Part(barn.transform, PrimitiveType.Cube, "Walls", wall,
                0f, 2.75f, 0f, 10f, 5.5f, 8f);
            FitEditorUtils.Part(barn.transform, PrimitiveType.Cube, "Roof_L", roof,
                -2.55f, 6.85f, 0f, 6.4f, 0.30f, 8.6f, 0f, 0f, 40f);
            FitEditorUtils.Part(barn.transform, PrimitiveType.Cube, "Roof_R", roof,
                2.55f, 6.85f, 0f, 6.4f, 0.30f, 8.6f, 0f, 0f, -40f);

            // 大门 + 白色装饰框
            FitEditorUtils.Part(barn.transform, PrimitiveType.Cube, "Door", trim,
                0f, 1.8f, 4.1f, 4.2f, 3.6f, 0.20f);
            FitEditorUtils.Part(barn.transform, PrimitiveType.Cube, "DoorSplit", wall,
                0f, 1.8f, 4.22f, 0.14f, 3.6f, 0.06f);
            FitEditorUtils.Part(barn.transform, PrimitiveType.Cube, "Cross_L", trim,
                0f, 1.8f, 4.24f, 4.0f, 0.14f, 0.06f, 0f, 0f, 44f);
            FitEditorUtils.Part(barn.transform, PrimitiveType.Cube, "Cross_R", trim,
                0f, 1.8f, 4.24f, 4.0f, 0.14f, 0.06f, 0f, 0f, -44f);

            AddObstacle(barn, new Vector3(10.4f, 7f, 8.4f), new Vector3(0f, 3.5f, 0f));
        }

        private static void BuildWell(Vector3 pos)
        {
            var well = new GameObject("Well");
            well.transform.position = pos;

            var stone = Mat("Farm_Stone", new Color(0.58f, 0.57f, 0.55f));
            var water = Mat("Farm_Water", new Color(0.30f, 0.55f, 0.72f));
            var wood = Mat("Farm_Wood", new Color(0.55f, 0.38f, 0.22f));
            var roof = Mat("Farm_Roof", new Color(0.55f, 0.20f, 0.16f));

            // 石台（Cylinder 直径 1 高 2 → scale 1.7/0.35 得直径 1.7、高 0.7）
            FitEditorUtils.Part(well.transform, PrimitiveType.Cylinder, "Base", stone,
                0f, 0.35f, 0f, 1.7f, 0.35f, 1.7f);
            FitEditorUtils.Part(well.transform, PrimitiveType.Cylinder, "Water", water,
                0f, 0.66f, 0f, 1.35f, 0.02f, 1.35f);

            FitEditorUtils.Part(well.transform, PrimitiveType.Cube, "Post_L", wood,
                -0.62f, 1.35f, 0f, 0.12f, 1.9f, 0.12f);
            FitEditorUtils.Part(well.transform, PrimitiveType.Cube, "Post_R", wood,
                0.62f, 1.35f, 0f, 0.12f, 1.9f, 0.12f);
            FitEditorUtils.Part(well.transform, PrimitiveType.Cube, "Roof_L", roof,
                -0.42f, 2.48f, 0f, 1.1f, 0.14f, 1.1f, 0f, 0f, 34f);
            FitEditorUtils.Part(well.transform, PrimitiveType.Cube, "Roof_R", roof,
                0.42f, 2.48f, 0f, 1.1f, 0.14f, 1.1f, 0f, 0f, -34f);

            AddObstacle(well, new Vector3(1.8f, 2.6f, 1.8f), new Vector3(0f, 1.3f, 0f));
        }

        private static void BuildWindmill(Vector3 pos, float yaw)
        {
            var mill = new GameObject("Windmill");
            mill.transform.position = pos;
            mill.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var wall = Mat("Farm_Wall", new Color(0.88f, 0.84f, 0.74f));
            var roof = Mat("Farm_Roof", new Color(0.55f, 0.20f, 0.16f));
            var blade = Mat("Farm_Blade", new Color(0.90f, 0.88f, 0.82f));
            var wood = Mat("Farm_WoodDark", new Color(0.42f, 0.28f, 0.16f));

            FitEditorUtils.Part(mill.transform, PrimitiveType.Cylinder, "Tower", wall,
                0f, 2.4f, 0f, 2.3f, 2.4f, 2.3f);
            FitEditorUtils.Part(mill.transform, PrimitiveType.Cone, "Cap", roof,
                0f, 5.4f, 0f, 2.9f, 0.75f, 2.9f);

            // 叶片挂在独立节点上，Windmill 只转这个节点
            var hub = FitEditorUtils.Node(mill.transform, "BladeHub", new Vector3(0f, 4.1f, 1.35f));
            hub.gameObject.AddComponent<Windmill>().SetRpm(9f);

            for (int i = 0; i < 4; i++)
            {
                var arm = FitEditorUtils.Node(hub, $"Blade_{i}", Vector3.zero);
                arm.localEulerAngles = new Vector3(0f, 0f, i * 90f);
                FitEditorUtils.Part(arm, PrimitiveType.Cube, "Plank", blade,
                    0f, 1.5f, 0f, 0.42f, 2.8f, 0.07f);
                FitEditorUtils.Part(arm, PrimitiveType.Cube, "Spar", wood,
                    0f, 1.5f, -0.06f, 0.10f, 2.8f, 0.10f);
            }

            AddObstacle(mill, new Vector3(2.5f, 5.6f, 2.5f), new Vector3(0f, 2.8f, 0f));
        }

        private static void BuildHayPiles()
        {
            var hay = Mat("Farm_Hay", new Color(0.85f, 0.72f, 0.30f));
            var hayDark = Mat("Farm_HayDark", new Color(0.70f, 0.58f, 0.24f));

            // 躺倒的圆柱（绕 Z 转 90°）
            (float x, float z, float yaw, bool dark)[] piles =
            {
                (-5.0f, -3.0f, 12f, false),
                (-3.0f, -2.0f, -34f, true),
                (-6.6f, -1.2f, 48f, false),
            };

            foreach (var p in piles)
            {
                var go = new GameObject("HayPile");
                go.transform.position = new Vector3(p.x, 0f, p.z);
                go.transform.rotation = Quaternion.Euler(0f, p.yaw, 90f);

                FitEditorUtils.Part(go.transform, PrimitiveType.Cylinder, "Bale",
                    p.dark ? hayDark : hay, 0f, 0f, 0f, 1.15f, 0.85f, 1.15f);

                AddObstacle(go, new Vector3(1.9f, 1.2f, 1.9f), new Vector3(0f, 0.6f, 0f));
            }
        }

        private static void BuildTrees()
        {
            var trunk = Mat("Farm_Trunk", new Color(0.42f, 0.30f, 0.18f));
            var canopy = Mat("Farm_Canopy", new Color(0.26f, 0.50f, 0.22f));
            var canopyLight = Mat("Farm_CanopyLight", new Color(0.34f, 0.58f, 0.26f));

            (float x, float z)[] spots =
            {
                (-24f, -24f), (-25f, 20f), (24f, -22f),
                (22f, 23f), (-7f, 25f), (13f, -25f),
            };

            foreach (var s in spots)
            {
                var tree = new GameObject("Tree");
                tree.transform.position = new Vector3(s.x, 0f, s.z);

                FitEditorUtils.Part(tree.transform, PrimitiveType.Cylinder, "Trunk", trunk,
                    0f, 1.0f, 0f, 0.5f, 1.0f, 0.5f);
                FitEditorUtils.Part(tree.transform, PrimitiveType.Sphere, "Canopy", canopy,
                    0f, 2.3f, 0f, 3.2f, 2.6f, 3.2f);
                FitEditorUtils.Part(tree.transform, PrimitiveType.Sphere, "CanopyTop", canopyLight,
                    0.35f, 3.3f, -0.25f, 2.2f, 1.8f, 2.2f);

                AddObstacle(tree, new Vector3(1.2f, 3f, 1.2f), new Vector3(0f, 1.5f, 0f));
            }
        }

        /// <summary>
        /// 刷新点（H1 已拍板不做传送门，这里只摆位置，不挂任何逻辑）。
        /// 后面做"打碎后 15-20 秒原地重长"时，直接用这些坐标即可。
        /// </summary>
        private static void BuildSpawnPoints()
        {
            var root = new GameObject("VeggieSpawnPoints");
            float[] xs = { -6.75f, 6.75f };
            float[] zs = { 4f, 12f };

            int i = 0;
            foreach (var px in xs)
            {
                foreach (var pz in zs)
                {
                    FitEditorUtils.Node(root.transform, $"Spawn_{i++}", new Vector3(px - 2.5f, 0f, pz));
                    FitEditorUtils.Node(root.transform, $"Spawn_{i++}", new Vector3(px + 2.5f, 0f, pz));
                }
            }
        }

        private static void PlaceVeggie(GameObject prefab, float x, float z)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = new Vector3(x, 0f, z);
        }

        // ------------------------------------------------------------------ 玩家

        private static GameObject BuildPlayer()
        {
            var weapon = LoadOrCreateWeapon();

            var player = new GameObject("Player");
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            player.AddComponent<FPSController>();
            player.AddComponent<Health>();
            var weaponBase = player.AddComponent<WeaponBase>();
            player.AddComponent<PlayerMotor>();   // Awake 内自动取引用，无需手绑

            // 三层相机（yaw / pitch / 后坐力各占一层），详见 PlayerCamera 注释
            var holder = new GameObject("CameraHolder");
            holder.transform.SetParent(player.transform, false);
            holder.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            var pc = holder.AddComponent<PlayerCamera>();

            var cam = new GameObject("Main Camera");
            var camera = cam.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.transform.SetParent(holder.transform, false);
            cam.transform.localPosition = Vector3.zero;

            // 明亮户外用纯色天空 + 同色雾，避免默认天空盒过蓝吃掉弹幕
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = _skyColor;
            camera.farClipPlane = 400f;   // 远山在 130 处

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(cam.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, -0.1f, 0.6f);

            var wso = new SerializedObject(weaponBase);
            FitEditorUtils.SetRef(wso, "_startWeapon", weapon);
            FitEditorUtils.SetRef(wso, "_muzzle", muzzle.transform);
            FitEditorUtils.SetRef(wso, "_aimCamera", camera);
            wso.ApplyModifiedProperties();

            var pso = new SerializedObject(pc);
            FitEditorUtils.SetRef(pso, "_controller", player.GetComponent<FPSController>());
            FitEditorUtils.SetRef(pso, "_weapon", weaponBase);
            FitEditorUtils.SetRef(pso, "_camera", camera);
            pso.ApplyModifiedProperties();

            player.tag = "Player";
            return player;
        }

        private static WeaponData LoadOrCreateWeapon()
        {
            FitEditorUtils.EnsureDir(WeaponDir);
            const string path = WeaponDir + "/Weapon_TestPistol.asset";

            var existing = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            if (existing != null) return existing;

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
            AssetDatabase.CreateAsset(w, path);
            return w;
        }

        // ------------------------------------------------------------------ 工具

        /// <summary>让建筑参与 NavMesh 挖洞，否则蔬菜会穿墙追人。</summary>
        private static void AddObstacle(GameObject go, Vector3 size, Vector3 center)
        {
            var obs = go.AddComponent<NavMeshObstacle>();
            obs.shape = NavMeshObstacleShape.Box;
            obs.size = size;
            obs.center = center;
            obs.carving = true;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Material> _mats = new();

        /// <summary>取（或建）一个材质。重复运行菜单时复用已有资产，不重复创建。</summary>
        private static Material Mat(string key, Color color)
        {
            if (_mats.TryGetValue(key, out var cached) && cached != null) return cached;

            var path = $"{FitEditorUtils.MatDir}/{key}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                _mats[key] = existing;
                return existing;
            }

            var mat = FitEditorUtils.SaveMaterial(key, color);
            _mats[key] = mat;
            return mat;
        }
    }
}
#endif
