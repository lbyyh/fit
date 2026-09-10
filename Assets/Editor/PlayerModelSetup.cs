#if UNITY_EDITOR
using Fit.Player;
using UnityEditor;
using UnityEngine;

namespace Fit.Editor
{
    /// <summary>
    /// 把外部角色模型装配成可直接用的 PlayerBody prefab。
    ///
    /// 菜单：Fit / 玩家 / 装配角色模型
    ///
    /// 【为什么需要这个脚本】
    /// AI 生成的模型直接拖进场景是不能用的，有三件事必须处理：
    ///
    ///   1. 尺寸:生成器的单位五花八门，模型可能是 100 米高或者 5 厘米高。
    ///      这里按包围盒自动缩放到 1.75 米，并把脚底对齐到 y=0
    ///      （不对齐的话角色会有一半埋在地下，或者悬空）。
    ///
    ///   2. 材质:OBJ / FBX 导入时 Unity 生成的是 Built-in 的 Standard 材质，
    ///      本项目是 URP，不转就会显示成洋红或者丢贴图。
    ///
    ///   3. 组件:要挂上 PlayerBody 才能让第一人称下本地自动隐藏、
    ///      队友可见、无骨骼时也有走路摆动。
    ///
    /// 【.glb 的处理】
    /// Unity 6 原生不支持 glb，需要 glTFast 包。本项目不引入联网依赖，
    /// 所以流程是:先用 Tools/decimate_model.py 把 glb 减面并转成 obj，
    /// 再走这个脚本。如果目录下只有 glb，脚本会给出明确提示而不是默默失败。
    /// </summary>
    public static class PlayerModelSetup
    {
        private const string ModelDir = "Assets/Models/Player";
        private const string PrefabDir = "Assets/Prefabs/Player";
        private const string MatDir = "Assets/Materials/Player";
        private const string PrefabPath = PrefabDir + "/PlayerBody.prefab";

        [Tooltip("角色目标身高（米）。与 FPSController 的相机高度 1.6 配套。")]
        private const float TargetHeight = 1.75f;

        [MenuItem("Fit/玩家/装配角色模型")]
        public static void Build()
        {
            string modelPath = FindModel();
            if (string.IsNullOrEmpty(modelPath))
            {
                Debug.LogError(
                    $"[Fit] 在 {ModelDir} 下没找到可用模型。\n" +
                    "支持 .obj / .fbx。如果是 .glb，请先执行:\n" +
                    "    python Tools/decimate_model.py <输入.glb> Assets/Models/Player/player_body.obj\n" +
                    "（Unity 原生不支持 glb，必须先转成 obj）");
                return;
            }

            ConfigureImporter(modelPath);

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (asset == null)
            {
                Debug.LogError($"[Fit] 模型加载失败: {modelPath}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            ConvertMaterials(instance);
            Normalize(instance);

            // 套一层空父节点：将来换模型只要替换子节点，PlayerBody 的引用结构不变
            var body = new GameObject("Body");
            instance.transform.SetParent(body.transform, true);

            var pb = body.AddComponent<PlayerBody>();
            var so = new SerializedObject(pb);
            FitEditorUtils.SetRef(so, "_modelRoot", instance.transform);
            so.ApplyModifiedProperties();

            FitEditorUtils.EnsureDir(PrefabDir);
            var prefab = PrefabUtility.SaveAsPrefabAsset(body, PrefabPath);
            Object.DestroyImmediate(body);

            AssetDatabase.SaveAssets();

            Debug.Log($"[Fit] 角色模型装配完成:\n" +
                      $"  源模型  {modelPath}\n" +
                      $"  Prefab  {PrefabPath}\n" +
                      $"  身高    {TargetHeight}m（脚底对齐 y=0）\n" +
                      "  注意    若角色背对你，手动把 Body 的 Y 旋转改成 180。", prefab);

            Selection.activeObject = prefab;
        }

        /// <summary>在模型目录里找第一个可用模型。glb 会被识别出来并单独提示。</summary>
        private static string FindModel()
        {
            foreach (string guid in AssetDatabase.FindAssets("", new[] { ModelDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/_raw/")) continue;   // 原始高模不参与构建

                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext is ".obj" or ".fbx") return path;
            }

            // 没找到 obj/fbx，看看是不是只有 glb
            foreach (string guid in AssetDatabase.FindAssets("", new[] { ModelDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetExtension(path).ToLowerInvariant() == ".glb")
                {
                    Debug.LogWarning(
                        $"[Fit] 发现 glb 模型 {path}，但 Unity 原生不支持导入。\n" +
                        "请先转成 obj 再用本菜单:\n" +
                        $"    python Tools/decimate_model.py \"{path}\" {ModelDir}/player_body.obj");
                    return null;
                }
            }

            return null;
        }

        private static void ConfigureImporter(string modelPath)
        {
            if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer) return;

            // 材质必须提取成外部 .mat 文件，否则改不了 shader（内嵌材质是只读的）
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.External;
            importer.materialName = ModelImporterMaterialName.BasedOnModelNameAndMaterialName;

            importer.animationType = ModelImporterAnimationType.None;   // 当前模型无骨骼
            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.isReadable = false;                 // 关掉可省一半内存
            importer.meshCompression = ModelImporterMeshCompression.High;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.generateSecondaryUV = false;        // 用不到 lightmap，省时间
            importer.addCollider = false;                // 碰撞由 CharacterController 负责

            importer.SaveAndReimport();
        }

        private static void ConvertMaterials(GameObject instance)
        {
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;

                    // 已经是 URP 的就不动
                    if (mats[i].shader != null && mats[i].shader.name.Contains("Universal Render Pipeline"))
                        continue;

                    mats[i] = FitEditorUtils.ToUrp(mats[i], MatDir, $"{instance.name}_Mat{i}");
                    changed = true;
                }

                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>按包围盒缩放到目标身高，并把脚底对齐到 y=0、水平居中。</summary>
        private static void Normalize(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            static Bounds Union(Renderer[] rs)
            {
                Bounds b = default;
                bool first = true;
                foreach (var r in rs)
                {
                    if (first) { b = r.bounds; first = false; }
                    else b.Encapsulate(r.bounds);
                }
                return b;
            }

            var bounds = Union(renderers);
            float height = bounds.size.y;
            if (height <= 0.0001f) return;

            float scale = TargetHeight / height;
            instance.transform.localScale = Vector3.one * scale;

            // 缩放后重新取一次包围盒，否则算出来的偏移是缩放前的
            bounds = Union(renderers);

            instance.transform.position = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }
    }
}
#endif
