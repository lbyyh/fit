using System.Collections.Generic;
using UnityEngine;

namespace Fit.Gameplay.Farming
{
    /// <summary>
    /// 可耕种地面（挂在每块菜畦上）。锄地、播种、成长全部由它管理。
    ///
    /// 【为什么按网格对齐，而不是在命中点直接锄】
    /// 不对齐的话玩家能锄出满地歪歪扭扭的小块，既难看又难判定
    /// （"我明明对着那块地按的"）。按固定格子对齐后：
    ///   - 每格一个状态，判定简单可预测
    ///   - 视觉整齐，符合低多边形 Q 版风格
    ///   - 网格数据天然适合将来做联机同步（发格子索引即可，不用发浮点坐标）
    ///
    /// 【⚠️ 联机归属（阶段 3 必做，现在先本地跑）】
    /// Listen Server（房主即服务器）拓扑下，耕地与作物的状态必须由房主权威：
    ///   - 玩家锄地 / 播种 → 客户端发 ServerRpc，房主校验距离后改状态
    ///   - 作物成长计时跑在房主，客户端只表现
    /// 否则客户端改内存就能瞬间催熟一片地。
    /// 现在阶段 1 没有联机，下面几个公开方法就是将来 Rpc 的落点，
    /// 接入时把方法体包一层 ServerRpc 即可，调用方不用改。
    /// </summary>
    public sealed class Farmland : MonoBehaviour
    {
        private enum PlotState
        {
            Untilled,   // 生地
            Tilled,     // 已锄，可播种
            Growing,    // 已播种，成长中
        }

        private sealed class Plot
        {
            public PlotState State;
            public CropDef Crop;
            public int Stage;
            public float StageTimer;
            public float WateredUntil;
            public GameObject Visual;
        }

        [Header("网格")]
        [SerializeField, Tooltip("格子边长（米）。1 米一格，和蔬菜个头匹配。")]
        private float _cellSize = 1f;
        [SerializeField, Tooltip("沿 X 方向的格数")]
        private int _width = 11;
        [SerializeField, Tooltip("沿 Z 方向的格数")]
        private int _depth = 5;

        [Header("表现")]
        [SerializeField, Tooltip("锄好的耕地外观。留空则运行时生成一个深色土块兜底。")]
        private GameObject _tilledVisualPrefab;

        [Header("调试")]
        [SerializeField] private bool _drawGizmos = true;

        private readonly Dictionary<Vector2Int, Plot> _plots = new();

        public int Width => _width;
        public int Depth => _depth;
        public float CellSize => _cellSize;

        /// <summary>地块发生变化。UI / 音效监听。</summary>
        public event System.Action<Farmland, Vector2Int> OnPlotChanged;

        /// <summary>
        /// 自带碰撞体。
        ///
        /// 【为什么必须自己加】
        /// 菜畦的视觉是用编辑器生成器拼的，而 Part() 会删掉每个零件自带的 Collider
        /// （不删会挡住敌人的视线射线，见 FitEditorUtils 注释）。
        /// 结果就是整块畦没有碰撞体，锄头的射线直接穿过去 —— 锄了个寂寞。
        /// 这里统一补一个覆盖整畦的 BoxCollider，任何地方挂上本组件都能立刻用。
        /// </summary>
        private void Awake()
        {
            if (GetComponent<Collider>() != null) return;

            var box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(_width * _cellSize, 0.2f, _depth * _cellSize);
            box.center = new Vector3(0f, 0.1f, 0f);
        }

        // ---------------------------------------------------------------- 工具作用入口

        /// <summary>锄地。返回是否真的锄了一格（已经是耕地则返回 false）。</summary>
        public bool Till(Vector3 worldPoint)
        {
            if (!TryGetCell(worldPoint, out var cell, out var center)) return false;

            if (_plots.TryGetValue(cell, out var plot) && plot.State != PlotState.Untilled)
                return false;

            if (plot == null)
            {
                plot = new Plot();
                _plots[cell] = plot;
            }

            plot.State = PlotState.Tilled;
            plot.Crop = null;
            plot.Stage = 0;
            plot.StageTimer = 0f;
            ReplaceVisual(plot, center, BuildTilledVisual(), Vector3.zero);

            OnPlotChanged?.Invoke(this, cell);
            return true;
        }

        /// <summary>播种。只有锄过的地能种。</summary>
        public bool Plant(Vector3 worldPoint, CropDef crop)
        {
            if (crop == null || crop.StageCount <= 0) return false;
            if (!TryGetCell(worldPoint, out var cell, out var center)) return false;
            if (!_plots.TryGetValue(cell, out var plot)) return false;
            if (plot.State != PlotState.Tilled) return false;

            plot.State = PlotState.Growing;
            plot.Crop = crop;
            plot.Stage = 0;
            plot.StageTimer = 0f;
            ReplaceVisual(plot, center, InstantiateStage(crop, 0), new Vector3(0f, 0.02f, 0f));

            OnPlotChanged?.Invoke(this, cell);
            return true;
        }

        /// <summary>浇水。返回是否浇到了正在生长的作物。</summary>
        public bool Water(Vector3 worldPoint)
        {
            if (!TryGetCell(worldPoint, out var cell, out _)) return false;
            if (!_plots.TryGetValue(cell, out var plot)) return false;
            if (plot.State != PlotState.Growing) return false;

            plot.WateredUntil = Time.time + (plot.Crop?.WateredDuration ?? 30f);
            return true;
        }

        /// <summary>该点所在格是否已锄好（给准星提示用）。</summary>
        public bool IsTilled(Vector3 worldPoint)
        {
            return TryGetCell(worldPoint, out var cell, out _)
                   && _plots.TryGetValue(cell, out var plot)
                   && plot.State == PlotState.Tilled;
        }

        // ---------------------------------------------------------------- 成长

        private void Update()
        {
            foreach (var pair in _plots)
            {
                var plot = pair.Value;
                if (plot.State != PlotState.Growing || plot.Crop == null) continue;

                float speed = Time.time < plot.WateredUntil
                    ? plot.Crop.WateredGrowthMultiplier
                    : 1f;

                plot.StageTimer += Time.deltaTime * speed;

                float need = plot.Crop.SecondsForStage(plot.Stage);
                if (plot.StageTimer < need) continue;

                plot.StageTimer = 0f;
                plot.Stage++;

                if (plot.Stage >= plot.Crop.StageCount)
                {
                    Mature(pair.Key, plot);
                    continue;
                }

                // 换下一阶段的模型
                if (TryGetCellCenter(pair.Key, out var center))
                    ReplaceVisual(plot, center, InstantiateStage(plot.Crop, plot.Stage), new Vector3(0f, 0.02f, 0f));
            }
        }

        /// <summary>
        /// 成熟。按 CropDef 生成一只蔬菜小怪，地块退回"已锄"状态 ——
        /// 这样"锄地→播种→长出怪→打掉→再锄"的循环能一直转下去。
        /// 注意：**不产出任何资源**，理由见 CropDef 的类注释（H2 / SCENE_FARM §4）。
        /// </summary>
        private void Mature(Vector2Int cell, Plot plot)
        {
            var crop = plot.Crop;
            TryGetCellCenter(cell, out var center);

            if (crop.SpawnsEnemyOnMature)
                Instantiate(crop.MatureEnemyPrefab, center, Quaternion.identity);

            // 作物本体已经"变成"小怪独立存在了，地块回到可播种状态
            ReplaceVisual(plot, center, BuildTilledVisual(), Vector3.zero);
            plot.State = PlotState.Tilled;
            plot.Crop = null;
            plot.Stage = 0;
            plot.StageTimer = 0f;

            OnPlotChanged?.Invoke(this, cell);
        }

        // ---------------------------------------------------------------- 坐标换算

        private bool TryGetCell(Vector3 worldPoint, out Vector2Int cell, out Vector3 worldCenter)
        {
            cell = default;
            worldCenter = default;

            Vector3 local = transform.InverseTransformPoint(worldPoint);
            float halfW = _width * _cellSize * 0.5f;
            float halfD = _depth * _cellSize * 0.5f;

            int cx = Mathf.FloorToInt((local.x + halfW) / _cellSize);
            int cz = Mathf.FloorToInt((local.z + halfD) / _cellSize);

            if (cx < 0 || cx >= _width || cz < 0 || cz >= _depth) return false;

            cell = new Vector2Int(cx, cz);
            worldCenter = transform.TransformPoint(new Vector3(
                (cx + 0.5f) * _cellSize - halfW,
                0f,
                (cz + 0.5f) * _cellSize - halfD));
            return true;
        }

        private bool TryGetCellCenter(Vector2Int cell, out Vector3 worldCenter)
        {
            float halfW = _width * _cellSize * 0.5f;
            float halfD = _depth * _cellSize * 0.5f;

            worldCenter = transform.TransformPoint(new Vector3(
                (cell.x + 0.5f) * _cellSize - halfW,
                0f,
                (cell.y + 0.5f) * _cellSize - halfD));
            return true;
        }

        // ---------------------------------------------------------------- 视觉

        private GameObject BuildTilledVisual()
        {
            if (_tilledVisualPrefab != null)
                return Instantiate(_tilledVisualPrefab);

            // 兜底：运行时造一个深色土块，避免忘记配 prefab 时什么都看不到
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = new Vector3(_cellSize * 0.9f, 0.12f, _cellSize * 0.9f);

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            r.sharedMaterial.color = new Color(0.30f, 0.21f, 0.13f);
            return go;
        }

        private static GameObject InstantiateStage(CropDef crop, int stage)
        {
            var prefab = crop.StagePrefabs[Mathf.Clamp(stage, 0, crop.StagePrefabs.Length - 1)];
            return prefab != null ? Instantiate(prefab) : null;
        }

        private void ReplaceVisual(Plot plot, Vector3 worldPos, GameObject next, Vector3 localOffset)
        {
            if (plot.Visual != null)
                Destroy(plot.Visual);

            plot.Visual = next;
            if (next == null) return;

            next.transform.SetParent(transform, true);
            next.transform.position = worldPos + localOffset;
        }

        private void OnDrawGizmosSelected()
        {
            if (!_drawGizmos) return;

            Gizmos.color = new Color(0.45f, 0.75f, 0.35f, 0.6f);
            float halfW = _width * _cellSize * 0.5f;
            float halfD = _depth * _cellSize * 0.5f;

            Vector3 c = transform.position;
            Gizmos.DrawWireCube(c + transform.up * 0.02f,
                new Vector3(_width * _cellSize, 0.04f, _depth * _cellSize));

            for (int x = 0; x < _width; x++)
            {
                for (int z = 0; z < _depth; z++)
                {
                    var p = transform.TransformPoint(new Vector3(
                        (x + 0.5f) * _cellSize - halfW, 0f, (z + 0.5f) * _cellSize - halfD));
                    Gizmos.DrawWireCube(p, Vector3.one * _cellSize * 0.9f);
                }
            }
        }
    }
}
