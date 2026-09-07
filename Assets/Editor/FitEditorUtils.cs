#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Fit.Editor
{
    /// <summary>
    /// 编辑器生成器的公共工具。
    ///
    /// 【为什么抽出来】
    /// 项目里有多个"一键生成"脚本（灰盒测试场景、农场主地图）。
    /// 它们都要做同一件事：建目录、造材质、给 private [SerializeField] 字段赋值。
    /// 复制三遍就会出现三份不一致的实现，尤其是材质那块的坑（见下）。
    /// </summary>
    internal static class FitEditorUtils
    {
        internal const string MatDir = "Assets/Materials";

        // -------- 目录 --------

        internal static void EnsureDir(string dir)
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

        /// <summary>
        /// 建一个纯色材质。
        ///
        /// 【为什么必须在这里开 _EMISSION】
        /// Telegraph（前摇充能光效）是靠 MaterialPropertyBlock 写 _EmissionColor 实现的。
        /// 但 URP Lit 材质**默认不开启 emission keyword**，这种情况下写 _EmissionColor
        /// 不会有任何视觉变化 —— 结果就是"前摇做完了但玩家完全看不出来"，
        /// 而 ID-008（所有攻击必须有前摇）是本项目三条铁律之一，看不见等于没做。
        ///
        /// 这里统一先把 keyword 打开、把自发光初始化为黑，
        /// 之后 Telegraph 用 PropertyBlock 覆盖时才真正生效。
        /// 顺带保留 GI 标记，否则自发光不参与烘焙光照。
        /// </summary>
        internal static Material SaveMaterial(string name, Color color, bool emissiveReady = true)
        {
            EnsureDir(MatDir);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);

            mat.SetColor("_BaseColor", color);
            mat.SetColor("_Color", color);

            if (emissiveReady)
            {
                mat.SetColor("_EmissionColor", Color.black);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            AssetDatabase.CreateAsset(mat, $"{MatDir}/{name}.mat");
            return mat;
        }

        // -------- SerializedObject 赋值 --------
        //
        // 运行时的引用字段全是 [SerializeField] private，编辑器脚本拿不到。
        // 标准做法就是走 SerializedObject，效果与 Inspector 里手动拖拽完全等价。

        internal static void SetRef(SerializedObject so, string prop, Object val)
            => Set(so, prop, p => p.objectReferenceValue = val);

        internal static void SetFloat(SerializedObject so, string prop, float val)
            => Set(so, prop, p => p.floatValue = val);

        internal static void SetInt(SerializedObject so, string prop, int val)
            => Set(so, prop, p => p.intValue = val);

        internal static void SetBool(SerializedObject so, string prop, bool val)
            => Set(so, prop, p => p.boolValue = val);

        internal static void SetColor(SerializedObject so, string prop, Color val)
            => Set(so, prop, p => p.colorValue = val);

        private static void Set(SerializedObject so, string prop, System.Action<SerializedProperty> apply)
        {
            var p = so.FindProperty(prop);
            if (p == null)
            {
                Debug.LogWarning($"[Fit] 生成器：{so.targetObject?.name} 上找不到字段 `{prop}`，" +
                                 "请核对运行时脚本是否已改名");
                return;
            }
            apply(p);
        }

        // -------- 程序化建模 --------

        /// <summary>
        /// 建一个基本体零件（低多边形 Q 版模型的最小单位）。
        ///
        /// 【为什么必须删掉自带 Collider】
        /// CreatePrimitive 会自动挂 Collider。模型零件如果各自带碰撞体：
        ///   1. 敌人自身零件会挡住 HasLineOfSight 的射线，导致永远"看不见玩家"而不攻击
        ///   2. 一堆小碰撞体互相挤，且白白增加物理开销
        /// 碰撞由根节点统一挂一个 CapsuleCollider 负责。
        ///
        /// Unity 内置基本体尺寸（缩放基准）：
        ///   Cube/Sphere 直径 1 · Capsule/Cylinder/Cone 直径 1、高 2 · Plane 10×10
        /// </summary>
        internal static GameObject Part(
            Transform parent, PrimitiveType type, string name, Material mat,
            Vector3 localPos, Vector3 localScale, Vector3 localEuler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());

            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localEulerAngles = localEuler;

            return go;
        }

        /// <summary>零件（位置用分量传，省得调用处写一堆 new Vector3）。</summary>
        internal static GameObject Part(Transform parent, PrimitiveType type, string name, Material mat,
            float px, float py, float pz, float sx, float sy, float sz,
            float rx = 0f, float ry = 0f, float rz = 0f)
            => Part(parent, type, name, mat,
                new Vector3(px, py, pz), new Vector3(sx, sy, sz), new Vector3(rx, ry, rz));

        /// <summary>空节点，用于分组或做旋转轴（风车叶片、摇摆容器）。</summary>
        internal static Transform Node(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }
    }
}
#endif
