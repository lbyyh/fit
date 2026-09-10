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


def extract_texture(src: Path, dst: Path) -> None:
    """
    从 glb 里把内嵌贴图抠出来，并修正 mtl 的 map_Kd 指向。

    【为什么需要手动抠】
    glb 的贴图是内嵌在二进制 chunk 里的（image 只有 bufferView、没有 URI）。
    pymeshlab 读进来后给它取名 texture_0，**不带扩展名**，保存时就懵了:
        PyMeshLabException: Image .../texture_0 cannot be saved.
        Your MeshLab version has not plugin to save file format.
    但此时 OBJ 本体其实已经写出来了，只是贴图没落地。
    所以这里直接从源 glb 的 BIN chunk 里把原始字节抠出来存成文件 ——
    顺带还是无损的（绕过了 MeshLab 的一次编解码）。
    """
    import json
    import re
    import struct

    try:
        with open(src, "rb") as f:
            f.read(12)                                    # glb 头
            clen, _ = struct.unpack("<II", f.read(8))
            js = json.loads(f.read(clen).decode("utf-8"))
            blen, _ = struct.unpack("<II", f.read(8))
            bin_data = f.read(blen)
    except Exception as e:
        print(f"       贴图提取失败（不影响 OBJ 本身）: {e}")
        return

    images = js.get("images") or []
    if not images:
        return

    try:
        img = images[0]
        bv = js["bufferViews"][img["bufferView"]]
        data = bin_data[bv["byteOffset"]: bv["byteOffset"] + bv["byteLength"]]
    except (KeyError, IndexError) as e:
        print(f"       贴图提取失败（bufferView 解析异常）: {e}")
        return

    mime = img.get("mimeType", "image/png")
    ext = "png" if "png" in mime else ("jpg" if "jpeg" in mime else "png")
    tex_name = f"{dst.stem}_texture.{ext}"
    (dst.parent / tex_name).write_bytes(data)
    print(f"       贴图  : {tex_name}  ({human(len(data))})")

    # OBJ 的材质文件由 MeshLab 写成 <模型名>.obj.mtl，引用的是无扩展名的 texture_0
    mtl_path = dst.parent / (dst.name + ".mtl")
    if not mtl_path.is_file():
        return

    mtl = mtl_path.read_text(encoding="utf-8", errors="ignore")
    fixed = re.sub(r"(?im)^(\s*map_Kd\s+).*$", rf"\g<1>{tex_name}", mtl)
    if fixed == mtl:                       # 原来没有 map_Kd 就补一行
        fixed = mtl.rstrip() + f"\nmap_Kd {tex_name}\n"
    mtl_path.write_text(fixed, encoding="utf-8")


def main() -> int:
    ap = argparse.ArgumentParser(description="AI 高模 -> 游戏可用轻量资产")
    ap.add_argument("input", help="输入模型（.glb / .gltf）")
    ap.add_argument("output", help="输出模型（.obj）")
    ap.add_argument("--faces", type=int, default=80_000,
                    help="目标面数，默认 80000。低多边形风格 3~8 万足够。")
    ap.add_argument("--quality", type=float, default=0.5,
                    help="质量阈值 0~1。越大越激进（减得更多但形状损失大），"
                         "默认 0.5。想尽量保形就调小到 0.3。")
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
        # 【为什么必须用 _with_texture 版本】
        # pymeshlab 有两个二次误差减面滤镜:
        #   meshing_decimation_quadric_edge_collapse            不带纹理保护
        #   meshing_decimation_quadric_edge_collapse_with_texture  带 extratcoordw
        # 带贴图的角色模型必须用后者。普通版本会把 UV 接缝一起塌缩掉，
        # 结果就是贴图被拉花（脸上糊着背部的图案）。
        #
        # extratcoordw: 纹理坐标权重。1.0 表示把 UV 当作第三个维度参与误差计算，
        #               塌缩时优先保证不破坏 UV 布局。
        # preservenormal: 保留硬边（帽檐、衣领这类折角），否则模型会变成一坨软泥。
        ms.apply_filter(
            "meshing_decimation_quadric_edge_collapse_with_texture",
            targetfacenum=args.faces,
            qualitythr=args.quality,
            extratcoordw=1.0,
            preservenormal=True,
            optimalplacement=True,
        )

    after = ms.current_mesh()
    print(f"       减面完成: 面 {after.face_number():,}")

    # 导出 OBJ。
    # 注意：glb 的内嵌贴图在 pymeshlab 里叫 texture_0 且不带扩展名，
    # 保存时会抛异常，但**OBJ 本体其实已经写出来了**。所以这里吞掉异常，
    # 贴图交给 extract_texture() 从源 glb 直接抠（顺带还是无损的）。
    try:
        ms.save_current_mesh(
            str(dst),
            save_wedge_texcoord=True,
            save_wedge_normal=True,
            save_vertex_color=False,
        )
    except Exception as e:
        if not dst.is_file():
            print(f"导出失败: {e}")
            return 1
        print(f"       （贴图需单独处理，交给 extract_texture）")

    extract_texture(src, dst)

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
