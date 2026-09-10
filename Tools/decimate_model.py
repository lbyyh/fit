#!/usr/bin/env python
"""
把 AI 生成的高面数模型加工成游戏可用的轻量资产。

用法:
    python Tools/decimate_model.py <输入.glb> <输出.obj> [--faces 80000]

为什么需要这一步
----------------
3D 生成器（Tripo / Meshy 等）导出的模型通常是这样:

    面数   100 万 ~ 200 万
    体积   几十 MB
    骨骼   无
    动画   无

而本项目是「低分辨率 3D」风格（640x360 渲染后放大）+ 低多边形美术，
这些面数在最终画面里完全看不出来 —— 640x360 屏幕上总共才 23 万像素，
一个角色就算占满屏幕也用不到 144 万个三角形。

所以减面是纯赚:视觉几乎无差别，内存和 GPU 开销降一个数量级。

另一个现实问题:.glb 在 Unity 里不能直接导入
---------------------------------------------
Unity 6 原生只支持 FBX / OBJ / DAE，glb / glTF 需要装 glTFast 包。
glTFast 要从 Unity Registry 联网下载，而本项目的原则是不引入联网依赖
（依赖解析失败会直接导致工程打不开）。所以这里统一转成 OBJ。

依赖
----
    pip install pymeshlab
"""

import argparse
import sys
from pathlib import Path


def human(n: float) -> str:
    for unit in ("B", "KB", "MB", "GB"):
        if abs(n) < 1024.0:
            return f"{n:.1f} {unit}"
        n /= 1024.0
    return f"{n:.1f} TB"


def main() -> int:
    ap = argparse.ArgumentParser(description="AI 高模 -> 游戏可用轻量资产")
    ap.add_argument("input", help="输入模型（.glb / .gltf）")
    ap.add_argument("output", help="输出模型（.obj）")
    ap.add_argument("--faces", type=int, default=80_000,
                    help="目标面数，默认 80000。低多边形风格 3~8 万足够。")
    ap.add_argument("--quality", type=float, default=1.0,
                    help="减面质量阈值 0~1，1 表示尽量保形（慢一点）。")
    args = ap.parse_args()

    src = Path(args.input)
    dst = Path(args.output)
    if not src.is_file():
        print(f"输入文件不存在: {src}")
        return 1

    try:
        import pymeshlab
    except ImportError:
        print("缺少依赖，请先执行:  pip install pymeshlab")
        return 1

    dst.parent.mkdir(parents=True, exist_ok=True)

    ms = pymeshlab.MeshSet()
    ms.load_new_mesh(str(src))
    before = ms.current_mesh()
    faces_before = before.face_number()
    verts_before = before.vertex_number()
    print(f"输入 : {src.name}")
    print(f"       面 {faces_before:,}   顶点 {verts_before:,}   体积 {human(src.stat().st_size)}")

    if faces_before <= args.faces:
        print(f"       面数已低于目标 {args.faces:,}，跳过减面")
    else:
        # preservetexture: 保留 UV 接缝，否则贴图会被拉花
        # preservenormal:  保留硬边，避免模型变成一坨软泥
        # autoclean:       减面后清理退化面片
        ms.apply_filter(
            "meshing_decimation_quadric_edge_collapse",
            targetfacenum=args.faces,
            qualitythr=args.quality,
            preservetexture=True,
            preservenormal=True,
            optimalplacement=True,
            autoclean=True,
        )

    after = ms.current_mesh()
    print(f"       减面完成: 面 {after.face_number():,}")

    # 导出 OBJ。save_textures 会把贴图一并写到同目录
    ms.save_current_mesh(
        str(dst),
        save_wedge_texcoord=True,
        save_wedge_normal=True,
        save_vertex_color=False,
    )

    exported = sorted(p for p in dst.parent.iterdir() if p.stem == dst.stem or p.suffix.lower() in (".png", ".jpg"))
    print(f"输出 : {dst}")
    print(f"       面 {after.face_number():,}   体积 {human(dst.stat().st_size)}")
    print(f"       削减 {100 - after.face_number() / faces_before * 100:.1f}% 面数")
    for p in exported:
        if p != dst:
            print(f"       附带: {p.name}  ({human(p.stat().st_size)})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
