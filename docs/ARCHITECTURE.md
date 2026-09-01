# 架构决策记录

本文记录 Fit 项目每一项技术选型的**来源与理由**。
选型依据是商业项目 **How to Fish**（Unity 6000.4.4f1）的构建产物静态分析结果 ——
一个已经上线、跑通了完整联机链路的参考实现。

标记约定：
- **[沿用]** —— 直接采用被分析项目的方案
- **[改进]** —— 采用思路，但修改了实现方式
- **[否决]** —— 分析后明确不采用

---

## 1. 引擎与运行时

### Unity 6000.4.4f1 [沿用]

被分析项目使用 Unity 6.4，并启用了几个 Unity 6 专属能力：

| 特性 | 证据 | 是否沿用 |
|---|---|---|
| APV 自适应探针体积 | `StreamingAssets/APVStreamingAssets/` | ✅ 替代手工布置 Light Probe Group |
| GPU Driven 渲染 | `Unity.RenderPipelines.GPUDriven.Runtime` | ✅ |
| 统一光追抽象 | `Unity.UnifiedRayTracing.Runtime` | ⚠️ 暂不启用，但保留程序集 |
| Unity 6 层级系统 | `UnityEngine.HierarchyModule` | ✅ |
| Graphics Jobs 多线程渲染 | `boot.config: gfx-enable-gfx-jobs=1` | ✅ |

### Mono 脚本后端 [沿用，但需注意]

被分析项目用 Mono 而非 IL2CPP（证据：`MonoBleedingEdge/EmbedRuntime/`、
`mono-2.0-bdwgc.dll`、`Assembly-CSharp.dll` 仅 822KB 的 IL 字节码）。

**选它的理由**：迭代快，构建时间短，反射与动态代码不受限。

**代价（必须知道）**：
- 代码可被 dnSpy 完整反编译，作弊门槛极低；
- 被分析项目同样**没有集成任何反作弊**（无 EAC / BattlEye 程序集）；
- 叠加"房主即服务器"拓扑，房主拥有完整权威。

**结论**：开发期用 Mono 无妨，**公开发版前必须切 IL2CPP 并评估反作弊方案**。
这一点写在 README 的已知限制里，不要忘记。

### Burst + Jobs [沿用]

被分析项目带 1.2MB 的 `lib_burst_generated.dll`，说明预编译了 Burst 代码。
本项目只把 Burst 用在**确实计算密集**的地方 —— 水面高度场
（`World/Water/GerstnerWaves.cs`），而不是全量上 DOTS。

理由：整体仍是 GameObject 架构，为 DOTS 全量重构的收益远小于代价。
这是被分析项目验证过的务实混合方案。

---

## 2. 渲染

### URP [沿用]

确认依据：`Unity.RenderPipelines.Universal.Runtime.dll`、
`Universal Render Pipeline/Lit` 着色器；`High Definition` 关键词命中 0 次。

### Shader Graph + VFX Graph [沿用]

被分析项目有 28 个自定义 Shader Graph、10+ 个 VFX Graph。
从命名能反推出完整的设计意图，本项目沿用其分类方式：

```
角色/载具  CharacterShader、BoatShader5（5 个变体 → 船体外观程序化定制）
场景      LevelShader、Grass、WindyDefault、CloudSphereShader、SkyboxShader
水下      UnderwaterShader、Water2、WaterBaked、WaterParticle
武器/UI   LaserSight5、SniperAim、RedDot5、UI5、UIBlur5、VignetteShader5
```

### 三档画质 [沿用]

被分析项目的 `globalgamemanagers` 里有 `Performant / Balanced / High Fidelity`。
本项目已在 `ProjectSettings/QualitySettings.asset` 中配好同名三档，
只需挂上对应 URP 资产（见 README）。

---

## 3. 网络

### FishNet 4 [沿用]

被分析项目用 FishNet（而非 Mirror / Netcode for GameObjects），
并启用了源码生成器（`GeneratedWriters___Internal`、`GeneratedComparers___Internal`），
编译期生成序列化器避免反射开销。

### Multipass 双传输 [沿用]

这是被分析项目里最有价值的设计之一：同时挂两条链路，运行时可切。

```
FishySteamworks      → Steam Networking Sockets（P2P + Steam 中继）
FishyUnityTransport  → UTP / UDP 直连
```

| 场景 | 走哪条 |
|---|---|
| Steam 版、Steam 在线 | FishySteamworks（免端口转发，NAT 穿透） |
| Steam 不可用 / 其他商店 / 开发期多开 | FishyUnityTransport |

**为什么值得做**：不把联机能力绑死在单一平台上。
被分析项目同时打进了 `Unity.Networking.Transport.dll` 和 Steamworks，
并且代码里有 `OnRelayStatus`、`SteamRelayNetworkStatus_t` 的中继状态监听 ——
说明它是真的在运行时切换，而不是构建期二选一。

本项目实现在 `Networking/TransportSwitcher.cs`，
`Auto` 模式会探测 `SteamAPI.IsSteamRunning()` 自动选择。

### Listen Server（房主即服务器）[沿用]

被分析项目同时有 `Server` 和 `Client` 两个 NetworkBehaviour，
外加 `ServerSettings`、`CreateNewServer`、`DeleteServer`、`CurServerSave`。

**优点**：零服务器运维成本。
**代价**：房主掉线房间结束；房主可作弊。

因此**重连逻辑必须做扎实** —— 本项目在 `FitNetworkManager` 里实现了
指数退避重连（2s → 3s → 4.5s → 6.75s → 10s，共 5 次）。

### Steam 大厅 [沿用]

`SteamMatchmaking` + `LobbyCreated_t` / `LobbyEnter_t` / `LobbyChatUpdate_t`，
代码里有 `CreateLobby` / `CopyLobbyIDButton` / `CurrentLobbyID`。

**不用** UGS Relay / Lobby / Matchmaker（被分析项目也没用，
依赖里只有 `core` + `authentication` 两个包）。

### 细粒度同步 [沿用 + 改进]

被分析项目有 **55 个 NetworkBehaviour**，粒度细到令人意外：
`PlayerMovement` / `PlayerVitals` / `PlayerEating` / `PlayerDying` /
`PlayerHolding` / `PlayerPunching` / `CrabArms`（蟹钳）各自独立同步。

**优点**：带宽可控、职责清晰、可组合复用。
**代价**：对象数量上升，需要小心处理部件间的初始化顺序。

本项目在 `NetworkEntity` 基类里统一解决了"兄弟组件引用"问题（带缓存的 `Sibling<T>()`），
这是对原方案的一个改进 —— 避免每个部件各自 `GetComponent`。

---

## 4. 语音

### 自研 MetaVoiceChat 风格方案 [沿用]

被分析项目没有用 Vivox / Photon Voice / Dissonance，而是自研了一整套：

```
MetaVoiceChat
├── Input   : Mic（采集）
├── Opus    : IOpusEncoder / IOpusDecoder / OpusCodecFactory
├── Rnnoise : GRU 神经网络降噪
├── NetProviders.FishNet : FishNetNetProvider / RelayFrame
└── Output  : Multicast / AudioSource
```

三件套：
- **Concentus** —— 纯托管 Opus，无原生依赖，跨平台省心
- **RNNoise** —— 开源 AI 降噪，专治键盘声与风扇噪声
- **FishNet RPC 中继** —— `ServerRelayFrame`

**为什么自研**：Vivox 按分钟计费，对长时长的 co-op 游戏成本可观。
Opus + RNNoise 是开源里的最优组合，质量不输商业方案。

### 星型中继拓扑 [沿用，但明确瓶颈]

```
本地采集 → RNNoise → Opus → ServerRpc → 房主 → ObserversRpc 广播
```

带宽估算：8 人 × 16kbps ≈ 房主需要 ~900kbps 上行。

**这是人数上限的主要约束**。超过 8 人必须改成 P2P 网状或引入语音服务器。

### 抖动缓冲 [改进]

语音质量的关键不在编解码，而在缓冲策略。本项目在 `VoicePlayback` 里实现了：

1. 预热 2 帧再开始播放（用延迟换流畅）
2. 播放速率按缓冲水位微调（0.98x ~ 1.02x，超出这个范围人耳能听出音调变化）
3. 缓冲溢出丢最旧帧（宁可断一下，也不要越播越延迟）

没做好的话就会出现"越聊延迟越大"的经典问题。

---

## 5. 音频

### Unity 原生，不用中间件 [沿用]

被分析项目的 `Managed/` 与 `Plugins/x86_64/` 中**完全没有** FMOD / Wwise / CRIWARE,
只有 `UnityEngine.AudioModule.dll` + `DSPGraphModule.dll`。

小团队省授权费与集成成本的合理选择。Unity 6 的 DSPGraph 已够用。

---

## 6. 内容管线

### Addressables [沿用]

`StreamingAssets/aa/` 下有 `catalog.bin`(73KB)、各语言 bundle、
`duplicateassetisolation` 分包。

### 岛屿 Additive 流式加载 [沿用 + 改进]

被分析项目的 `OnlineIslandManager` 有一整套生命周期：
`LoadIsland` / `UnloadAllIslandsRoutine` / `OnIslandChange` /
`IslandSpawner` / `IslandBuildOffset` / `TimeWhenSwappingIsland` / `MaxIsland`

场景结构（7 个，对应 level0~level6）：
```
Assets/Scenes/Game.unity              主场景
Assets/Scenes/Islands/Island1..5.unity 5 个岛屿（Additive 加载）
Assets/Scenes/Islands/DevIsland.unity  开发岛
```

**本项目在此基础上加了两个细节**（原版没有或做得不完整）：

1. **卸载迟滞（hysteresis）** —— 加载半径与卸载半径之间留 60m 缓冲，
   否则玩家在边界来回走会触发反复加载（thrashing）。
2. **延迟卸载 + 二次确认** —— 触发卸载后等 2 秒再检查一次位置，
   玩家可能已经走回来了。

另外原版的 `DevIsland` 被打包进了正式版，是个疏漏，本项目不保留。

### 12 语言本地化 [沿用]

被分析项目为每个语言单独打 bundle：
`zh-cn / zh-tw / en / fr / de / it / ja / ko / pl / pt-br / ru / es-mx`

这个投入**远超独立游戏平均水准**，说明发行方对多区域市场有预期。

---

## 7. 存档

### 玩家进度与房间世界状态分离 [沿用]

被分析项目有 `GetSavedPlayer` / `GetSavedCreature` / `CurServerSave` /
`ServerHasFinishedTutorial` 四套存档接口，说明它把两类数据分开了：

| 数据 | 归属 | 跟随 |
|---|---|---|
| 玩家进度（金币、皮肤、成就、图鉴） | 每个玩家 | 跨房间跟随 |
| 房间世界状态（天数、生物、经济） | 房主 | 跟房主走 |

### 原子写 + 备份 [改进]

存档最容易挨骂的 bug 是"写到一半断电，进度全丢"。
本项目 `SaveFile.WriteAtomic()` 的做法：

```
写 .tmp → Move 覆盖正式文件 → 保留 .bak
```

解析失败时静默重置并保留 `.bak`，而不是弹窗卡住启动流程。

### 自动存档 [沿用]

被分析项目有 `AutoSaver` + `AutoSave` 协程。联机 co-op 里这是必须的 ——
玩家不知道房主什么时候关房间，靠手动存档一定会丢进度。

本项目加了节流（最小 5s 间隔 + 脏标记合并写入），
避免每次金币变动都写盘卡主线程。

---

## 8. 明确不采用的部分

| 项 | 原因 |
|---|---|
| 房间世界数据与玩家数据混存 | 换房间会串档，联机游戏的大忌 |
| 逐帧 Transform 同步 | 带宽浪费 5~10 倍，改为状态 + 插值收敛 |
| DevIsland 进正式包 | 原版的疏漏 |
| 单文件巨型资源包 | 原版 `sharedassets0.resource` 177MB，无法按需分块，首屏加载压力大 |
| UGS Relay / Lobby | 已被 Steam 大厅覆盖，不重复建设 |
| 商用语音服务 | 成本考虑，自研方案已足够 |

---

## 附：分析方法

| 目标 | 手段 |
|---|---|
| Unity 版本 | 扫描 `globalgamemanagers` 中的版本串 |
| 场景列表 | 提取 `.unity` 路径字符串 |
| 类型/成员名 | 自研 PE + CLI 元数据解析器，dump `#Strings` 堆（8132 个标识符） |
| NetworkBehaviour 清单 | 匹配 `NetworkInitialize___Early<类名>Assembly-CSharp` 代码生成符号 |
| 渲染管线 | 扫描 `sharedassets0.assets` 中的着色器名 |
| 本地化语言 | `StreamingAssets/aa/StandaloneWindows64/` 下 bundle 命名 |
| Steam 配置 | `OnlineFix.ini`、`UnityServicesProjectConfiguration.json` |
