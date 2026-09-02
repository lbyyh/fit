# Legacy —— 已停用，但保留

## 为什么放在这里

这一批代码是项目早期按「海洋开放世界垂钓」搭的，游戏本体改为第一人称地牢弹幕射击后
不再适用。2026-09-02 决策 Q5 =「保留暂时不用」，所以没有删除。

| 文件 | 原用途 | 不适用原因 |
|---|---|---|
| `World/IslandStreamer.cs` | 岛屿按距离流式装卸 | 已被 `Assets/Scripts/World/RoomStreamer.cs` 取代（调度逻辑沿用，粒度从「岛」换成「房间」，距离判定由欧氏距离改为曼哈顿跳数） |
| `World/Water/GerstnerWaves.cs` | Gerstner 波水面（Burst + Jobs） | 地牢没有开放水域 |
| `World/UnderwaterController.cs` | 水下状态切换 | 同上 |
| `Networking/NetworkRigidbodySync.cs` | 刚体插值同步 | 地牢的同步主体是角色与投射物，不是刚体物理 |

## 它是怎么被停用的

靠 `Fit.Legacy.asmdef` 里的这一行：

```json
"includePlatforms": []
```

空的 `includePlatforms` 表示「不包含任何平台」，Unity 因此**完全不编译**这个程序集。
文件还在、还在版本控制里、Unity 也能看到它们，但不参与编译，不产生任何报错，
也不会拖慢主工程的编译速度。

这比删掉好：将来做别的题材（比如真的回去做海洋）能原样捡回来。

## 想恢复怎么办

编辑 `Fit.Legacy.asmdef`，把空的 `includePlatforms` 改成需要参与的平台：

```json
"includePlatforms": ["Editor", "WindowsStandalone64"]
```

恢复后要注意：这些文件依赖 FishNet、Unity.Burst、Unity.Jobs、Addressables，
包没装好会在这里报错。
