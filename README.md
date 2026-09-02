# Fit

Unity 6 多人合作游戏项目的工程骨架。

技术栈选型来自对已完成商业项目 **How to Fish**（Dazed Games，Unity 6000.4.4f1）的
逆向分析，挑出其中被验证过、且适合小团队无服务器架构的部分，重新实现为干净的代码骨架。

> 本项目只包含架构与实现骨架，**不含任何被分析项目的美术、音频、配置或代码资产**。

---

## 三份文档，各管一件事

| 文档 | 管什么 | 谁写 |
|---|---|---|
| **[`docs/GAME_DESIGN.md`](docs/GAME_DESIGN.md)** | **游戏本身是什么**：构思、可行性评估、玩法设计、里程碑。**新想法往这里写** | 构思区由你写，评估区由小w写 |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | 技术选型依据：每项决策沿用/改进/否决了什么，理由是什么 | 小w |
| `README.md` | 怎么把工程跑起来 | 小w |

**如果你只想打开一个文件，开 `docs/GAME_DESIGN.md`。**

---

## 环境要求

| 项目 | 版本 |
|---|---|
| Unity Editor | **6000.4.4f1** |
| 目标平台 | Windows x64（Standalone） |
| 后端 | Mono |
| IDE | Rider / Visual Studio 2022 |

## 首次打开工程后必须做的 4 件事

工程骨架不包含 URP 资产与原生插件（这些是二进制文件，不适合放进模板仓库）。
打开工程后按顺序完成：

### 1. 创建 URP 管线资产（3 档）

`Assets > Create > Rendering > URP Asset (with Universal Renderer)`，
在 `Assets/Settings/` 下创建三个，然后把它们拖到
`Project Settings > Graphics > Scriptable Render Pipeline Settings`，
并在 `Project Settings > Quality` 里分别指派给三档：

| 档位 | 对应 Quality Level | 建议设置 |
|---|---|---|
| `URP-Performant` | Performant | 阴影关闭、抗锯齿关闭、软阴影关闭、Lod Bias 0.4 |
| `URP-Balanced` | Balanced | 2 级阴影级联、60m 阴影距离、各向异性 1x |
| `URP-HighFidelity` | High Fidelity | 4 级阴影级联、150m 阴影距离、抗锯齿 SMAA、软粒子 |

> `ProjectSettings/QualitySettings.asset` 里已经配好了同名的三档画质参数，
> 只需把对应 URP 资产挂上去。

### 2. 放入第三方音频库

| 库 | 用途 | 获取方式 | 放置位置 |
|---|---|---|---|
| Concentus | Opus 编解码（纯托管，无原生依赖） | NuGet `Concentus` | `Assets/Plugins/Concentus.dll` |
| RNNoise4Unity | GRU 神经网络降噪 | GitHub `adrenak/RNNoise4Unity` | `Assets/Plugins/`（含 `rnnoise.dll`） |

放好后，把 `Assets/Scripts/Voice/OpusCodec.cs` 里标注了「真实实现」的注释
替换成实际 API 调用即可 —— 调用点已全部收敛在这一个文件里。

### 3. 配置 Addressables

`Window > Asset Management > Addressables > Groups`，
为每个岛屿场景设置 Addressable Key（与 `IslandStreamer.Islands` 里的
`AddressableKey` 字段保持一致）。

### 4. 配置本地化

`Window > Asset Management > Localization Tables`。
建议起步语言集（对齐被分析项目的 12 语言配置）：

```
zh-cn（简体中文） · zh-tw（繁体中文） · en（英语） · ja（日语） · ko（韩语）
fr（法语） · de（德语） · it（意大利语） · ru（俄语）
pt-br（巴西葡萄牙语） · es-mx（墨西哥西班牙语） · pl（波兰语）
```

---

## 目录结构

```
Assets/Scripts/
├── Core/           GameBootstrap（显式初始化编排）、ServiceLocator
├── Networking/
│   ├── FitNetworkManager        会话生命周期 + 指数退避重连
│   ├── TransportSwitcher        Multipass 双传输（Steam P2P / UTP 直连）
│   ├── NetworkEntity            所有联网实体的基类
│   ├── Session/                 SteamLobbyService、ServerListStore
│   ├── Sync/                    NetworkRigidbodySync（插值 + 瞬移阈值）
│   └── Player/                  NetworkPlayer、PlayerVitals、PlayerInventory
├── Voice/          VoiceSettings、MicrophoneInput、OpusCodec、
│                   FishNetVoiceProvider、VoicePlayback（抖动缓冲）
├── World/          IslandStreamer、UnderwaterController、Water/GerstnerWaves
├── Save/           SaveData（原子写 + 备份）、AutoSaver（节流 + 强制落盘）
├── Gameplay/       Creatures、Economy、Achievements
└── UI/             LobbyMenu、ServerListEntryView
```

## 依赖说明

`Packages/manifest.json` 中三个包通过 Git URL 引入，首次打开会较慢：

| 包 | 来源 |
|---|---|
| Steamworks.NET | `github.com/rlabrecque/Steamworks.NET` |
| FishNet | `github.com/FirstGearGames/FishNet` |
| FishyUnityTransport | `github.com/FirstGearGames/FishyUnityTransport` |

> **注意**：FishNet 主仓库的部分功能需要授权。请确认你的使用方式符合其许可协议。
> 若不使用 Steam，在 `Project Settings > Player > Scripting Define Symbols` 中
> 添加 `DISABLESTEAMWORKS` 即可编译出不含 Steam 的版本，传输层会自动降级到 UTP。

---

## 已知限制

- 脚本后端为 **Mono**，代码可被完整反编译，且**未集成任何反作弊方案**。
  若计划公开联机，需要在发版前另行评估。
- 语音采用**房主中继**拓扑，房主上行带宽随人数呈 N² 增长。
  超过 8 人需要改为 P2P 网状或引入语音服务。
- C# 代码为架构参考实现，**尚未在 Unity 中编译验证**，
  部分 FishNet / Steamworks 的 API 签名需按实际版本微调。

## 参考

架构决策的完整依据见 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)。
