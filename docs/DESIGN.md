# 工业监控 — 设计手册

> 版本 **1.1.10** · 修订 **2** · 平台：Windows 10/11 工控机、Android 11 工业平板  
> 技术栈：.NET 10 MAUI · Modbus TCP（信捷 XD5E-60T10）· 西门子 S7 · MQTT  
> PDF 版：[DESIGN.pdf](DESIGN.pdf)（运行 `python ../scripts/export-design-pdf.py` 重新生成）  
> **代码架构与运行逻辑**：[ARCHITECTURE.md](ARCHITECTURE.md)

---

## 1. 产品概述

**工业监控** 是一套面向复合产线的现场采集终端。支持两种运行方式：

- **采集模式**：按 **PLC 采集周期** 从 PLC 读取点位，界面实时刷新；按 **发布周期**、温度阈值与报文模板组装 JSON，向一个或多个 MQTT 目标上报。
- **订阅模式**：连接 MQTT Broker，订阅一个或多个主题，在局域网内查看其他采集终端的遥测数据（支持 `properties` 嵌套与平台字段映射）。

| 项目 | 说明 |
|------|------|
| 应用名称 | 工业监控 |
| 版本号 | 1.1.10（`ApplicationDisplayVersion`，诊断页可见） |
| 修订号 | 2（`ApplicationVersion`，Android versionCode） |
| 包名 / ID | `com.industrial.monitor` |
| 产线（内置模板） | 先河 / 华迪 / 撒粉 / 平板 / C型火焰 热熔胶或复合机（见 §6） |
| PLC | **Modbus TCP**（信捷等，默认端口 502）或 **西门子 S7**（默认端口 102，机架/槽位/CPU 类型可配） |

**Windows 推荐部署**：安装包可选 **后台采集服务**（`HuaGuang.Monitor.Service`）与 **守护服务**（`HuaGuang.Monitor.Watchdog.Service`）。守护服务约每 20 秒巡检：采集 Windows 服务是否在跑、是否应自动恢复采集/订阅、界面是否异常退出并重启。UI 通过 IPC 启停与查看状态；三者共用 ProgramData 下 `lines` 与日志。Android 为单进程本地采集。

---

## 2. 系统架构

![系统架构](images/04-architecture.png)

```
┌─────────────┐   Modbus / S7    ┌────────────────────────────┐   MQTT (N 目标)   ┌─────────────┐
│    PLC      │ ───────────────► │  UI 和/或 Windows 后台服务   │ ───────────────► │ MQTT Broker │
│  :502/102   │    周期轮询       │  (Win / Android)            │   异步队列发布    │  / 云端     │
└─────────────┘                   └────────────────────────────┘                 └─────────────┘
                                         │
                    ┌────────────────────┼────────────────────┐
                    ▼                    ▼                    ▼
              DashboardPage          TagsPage           HistoryPage
              实时监控               点位管理              历史数据
                    │                    │                    │
                    └────────────────────┼────────────────────┘
                                         ▼
                              SettingsPage / DiagnosticsPage
                              参数配置          日志与自检
```

实现分层与 IPC 详见 [ARCHITECTURE.md](ARCHITECTURE.md)。

### 2.1 核心模块

| 模块 | 职责 |
|------|------|
| `AcquisitionService` | 采集：专用线程轮询 PLC、模拟数据、发布判定、MQTT 入队 |
| `MqttOutboundService` | 采集模式 MQTT：**后台队列**，每启用目标独立连接与发布 |
| `SubscriptionService` | 订阅模式：单连接、多主题、解析遥测 JSON |
| `PlcClientRouter` | 按协议路由 `ModbusTcpPlcClient` / `S7PlcClient` |
| `ModbusTcpPlcClient` / `S7PlcClient` | 批量读点；读失败时断开 PLC 并退避重连 |
| `MqttPublisher` / `MqttConnectionFactory` | 单目标 MQTT 连接与发布 |
| `MqttEndpointCatalog` | 多 MQTT 目标规范化、凭证校验、与 `settings.Mqtt` 同步 |
| `SettingsStore` | 运行时 `AppSettings` 内存态与修订号 |
| `LineExcelConfigService` | 产线 Excel 读/写（**权威持久化**） |
| `HistoryStore` / `HistoryRecorder` | SQLite 历史、异步写入、保留期清理 |
| `LineCatalog` | **新建**产线 Excel 时的默认种子（已存在 Excel 以文件为准） |
| `MonitorIpcServer` / `RemoteMonitorAcquisition` | Windows UI ↔ 后台服务 |
| `MonitorCoreTests` | 核心逻辑单元测试（诊断页可触发） |

### 2.2 配置与持久化

- **权威配置**：每条产线一个 xlsx，路径 `{用户数据目录}\lines\{产线名}.xlsx`（Windows 多为 `C:\ProgramData\com.industrial.monitor\Data\lines`；安装目录 `lines\` 为只读模板副本）。
- **保存设置**：合并界面 → `SettingsStore` → **Export 整本 Excel**（含「MQTT目标」多行表）。
- 仓库内 `config/lines/` 为随安装包分发的模板；**不以**独立的 `settings.json` 覆盖 Excel 点表。

Excel 工作表：`配置`、`MQTT目标`、`MQTT报文`、`字段映射`、`点表`、`点表·显示分组`（说明）。

### 2.3 数据流（采集模式）

1. 按 **PLC 采集周期**（Excel「扫描周期毫秒」，常见 2000–60000 ms）读取所有启用点位  
2. **界面每轮刷新**，与是否发布 MQTT 无关  
3. **MQTT 发布**（与采集周期独立）：  
   - 满足 **发布周期毫秒**（默认 60000），或  
   - **温度发布阈值** > 0 且任一温度变化 ≥ 阈值时提前发布，或  
   - 用户触发「立即发布」  
4. 启用多个 MQTT 目标时，**相同 payload** 分别入队发往各 Broker  
5. 监控页 MQTT 状态：**全部已启用目标在线** 才显示「已连接」；诊断页可查看各目标明细  

### 2.4 数据流（订阅模式）

1. 使用 **第一个已启用的 MQTT 目标** 的连接参数连接 Broker  
2. **同时订阅** 设置中的全部主题（支持 `+` / `#`）  
3. 收到 JSON 后按 `主题::deviceId` 区分设备；支持 `properties` 内平台字段（如 `rrjwd`）映射为点表中文名  
4. 首页 **主题筛选**；运行中可在首页 **添加主题** 并自动重订  

---

## 3. 视觉设计规范（Windows）

### 3.1 设计语言

深色工业风，高对比度数值、低干扰背景，适配工控机长时间运行。

### 3.2 色彩

| 用途 | 色值 | 说明 |
|------|------|------|
| 页面背景 | `#0B1522` | 主背景 |
| 卡片背景 | `#152536` | 点位卡片、状态块 |
| 卡片边框 | `#1F3A52` | 1 px 描边 |
| Tab 栏背景 | `#101C28` | 底部导航 |
| 主强调色 | `#2EC4B6` | 数值、选中 Tab、按钮 |
| 主文字 | `#FFFFFF` | 标题 |
| 次级文字 | `#8AA0B5` | 标签、说明 |
| 数值辅助 | `#C9D6E2` | 报文、点名 |
| 成功 / 已连接 | `#3DDC97` | 状态点 |
| 警告 | `#FFB347` | 未发布提示 |
| 错误 | `#FF6B6B` | 异常信息 |
| 删除按钮 | `#5A2430` | 危险操作 |

### 3.3 字体与圆角

| 元素 | 规格 |
|------|------|
| 页面标题 | 20 pt，Bold，White |
| 区块标题 | 18 pt，Bold |
| 实时数值 | 22 pt，Bold，`#2EC4B6` |
| 卡片圆角 | 8 px |
| 状态点 | 7–8 px 圆形 |

### 3.4 导航结构

底部 **TabBar** 五页，**无顶部 Shell 标题栏**（`Shell.NavBarIsVisible=False`），内容区自带标题。

| Tab | 路由 | 功能 |
|-----|------|------|
| 监控 | `dashboard` | 实时数据、启停采集/订阅、PLC/MQTT 状态、订阅主题筛选、扫码输入 |
| 点位 | `tags` | 按显示分组浏览点位，增删改、手动值、显示精度 |
| 历史 | `history` | SQLite 历史表格、动态列、删除、分页/自定义时间 |
| 设置 | `settings` | 运行模式、产线 Excel、PLC 协议、多 MQTT 目标、采集/发布周期 |
| 诊断 | `diagnostics` | 后台服务状态、运行日志、路径、软件自检 |

---

## 4. 界面设计（Windows）

### 4.1 监控页

![监控页](images/01-dashboard.png)

**布局（上 → 下）**

| 区域 | 内容 |
|------|------|
| 顶栏 | 应用名、设备 ID、运行模式；PLC / MQTT 状态卡片；**启动/停止** 按钮。**竖屏**为三行，**横屏**单行三列 |
| 订阅区 | （订阅模式）主题筛选、「全部」+ 各主题；添加主题 |
| 设备选择 | （订阅模式）远程设备 Picker |
| 信息条 | 发布/订阅摘要；采集/发布周期提示；橙色未发布原因；红色错误 |
| 主区域 | 按 **显示分组** 网格卡片：实时值、单位、地址/来源、质量、更新时间 |
| 底栏 | 最近 MQTT 报文摘要 |

**状态指示**

- PLC：绿点「已连接」/ 灰点「未连接」；模拟模式不连 PLC  
- MQTT（采集）：**全部启用目标** 在线为绿；否则灰（详情见诊断页运行摘要）  
- MQTT（订阅）：单连接是否在线  

**交互**

- 「启动采集 / 停止采集」「启动订阅 / 停止订阅」  
- 支持 USB 扫码枪（键盘模式）的手动文本点位：点击后输入或扫码（如产品货号）  

---

### 4.2 点位页

![点位页](images/02-tags.png)

**布局**

| 区域 | 内容 |
|------|------|
| 顶栏 | 「采集点位」、产线摘要、**新增** |
| 说明 | 分组与 Excel「点表·显示分组」一致；MQTT 字段见「字段映射」 |
| 主区域 | 虚拟化分组列表；卡片含地址、MQTT 字段、启用状态 |
| 操作 | **编辑** / **删除** |

**点位编辑页**

- **Modbus（信捷）**：`D100`、`M0`、`X20`（八进制）、`Y0`；浮点占两个 D，默认字节序 **CDAB**  
- **西门子 S7**：`DB1.DBD0`、`IW64`、`%IW128`、`ID100` 等（支持 IW/ID、德文 EW 别名）；机架/槽位/CPU 在设置页  
- Scale / Offset、**显示精度**（0–4，留空用全局「精度」）  
- 手动文本点位可勾选 **USB 扫码枪输入**  

> 产线切换、Excel 导入/导出在 **设置** 页；点表以运行时 `lines\{产线名}.xlsx` 为准。

---

### 4.3 历史页

| 区域 | 内容 |
|------|------|
| 顶栏 | 「历史数据」、条数摘要 |
| 筛选 | 时间范围（24h / 7d / 30d / **自定义起止**）、设备 Picker |
| 操作 | **刷新**、**删除筛选**、**清空全部** |
| 表头 | 动态合并点位列（最多 64 列）；列宽可拖（Windows） |
| 主区域 | 虚拟化列表，单行等宽文本 + 横向滚动 |

- **不自动加载**：须手动「刷新」  
- **分页**：每页 40 条，「上一页 / 下一页 / 跳转页码」  
- 订阅模式：MQTT 平台键映射为中文点名入库与展示  
- 须在设置中开启 **历史记录**  

---

### 4.4 设置页

![设置页](images/03-settings.png)

#### 采集

| 字段 | 说明 |
|------|------|
| 运行模式 | **采集** / **订阅** |
| 订阅主题 | 多个，支持通配符；至少保留一个（订阅模式） |
| 产线 | Picker 五产线；显示 **运行时 Excel 路径** |
| Excel 操作 | 从 Excel 载入、保存到 Excel、从其他文件导入 |
| 设备编号 | MQTT `{deviceId}` 等替换 |
| **PLC 采集周期** | 两次读 PLC 间隔；仅影响采集与界面刷新 |
| **MQTT 发布周期** | 两次上报最短间隔；可与采集周期不同（如 5 s 采、60 s 发） |
| 温度发布阈值 | `0` = 仅按发布周期；`>0` = 温度达阈值可提前发 |
| **精度** | 0–4 位小数，全局默认（原「温度精度」） |
| 使用模拟数据 | 不连 PLC，验证 MQTT |
| 开机自动启动 / 启动后自动运行 | Windows；与安装向导选项一致 |

#### 历史数据

| 字段 | 说明 |
|------|------|
| 记录开关 | 采集/订阅快照写 SQLite |
| 保留天数 | 1–365，默认 14 |

#### PLC

| 字段 | 说明 |
|------|------|
| 协议 | **Modbus TCP** / **西门子 S7** |
| 型号 | 备注（如 XD5E-60T10、S7-1200） |
| IP / 端口 | Modbus 默认 502；S7 默认 102 |
| 站号 | Modbus |
| 机架 / 槽位 / CPU 类型 | S7（S71200、S71500 等） |
| 超时 | 建议 2000–5000 ms |

默认 IP 以各产线 Excel「配置」为准（模板种子见 §6，现场常改为 `172.14.1.20x`）。

#### MQTT（多目标）

| 字段 | 说明 |
|------|------|
| 目标列表 | 名称、启用、Broker、端口、ClientId、用户名、密码、主题、QoS、TLS |
| 行为 | 采集：**所有启用目标** 各发一份；订阅：用 **第一个启用目标** 连接 |
| Excel | 「MQTT目标」工作表与界面同步；保存设置时写回 |

> 修改 PLC/MQTT 或导入 Excel 前建议 **停止采集/订阅**（首页运行中添加订阅主题除外）。保存设置会 Export 整表。

---

### 4.5 诊断页

| 区域 | 内容 |
|------|------|
| 版本 | 版本号与修订号 |
| **后台服务状态** | 服务是否安装/运行、IPC 是否可用 |
| **运行日志** | UI/服务日志 tail；刷新、清空、**打开日志目录** |
| **日志与数据路径** | ProgramData / LocalAppData 回退；产线 Excel 运行时路径 |
| 运行摘要 | PLC/MQTT、各 MQTT 目标连接与凭证摘要、采集/发布周期、周期计数 |
| **软件自检** | **核心测试** / **全部测试** / **压力测试**，PASS/FAIL 列表 |

日志文件示例：`runtime-ui-yyyyMMdd.log`（UI）、`runtime-yyyyMMdd.log`（服务）、`crash-*` / `exit-*`。

---

## 5. MQTT 报文格式

默认产线使用 **properties** 嵌套 + 平台字段 id（见 Excel「MQTT报文」「字段映射」）。示例：

```json
{
  "deviceId": "先河热熔胶复合机",
  "timestamp": "2026-08-19T05:30:00Z",
  "simulator": false,
  "plcHost": "172.14.1.200",
  "quality": "Good",
  "properties": {
    "run_status": 1,
    "rrjwd": 180.5,
    "jgwd": 175.2,
    "speed": 45.2,
    "cphh": "SKU-001"
  }
}
```

| 字段 | 说明 |
|------|------|
| `TagsPath` | Excel 可配为 `properties` / `tags` / 平铺根级 |
| `properties` 内键 | 来自「字段映射」id 列（如 `rrjwd` = 当前工作胶盘温度） |
| `quality` | `Good` / `Uncertain`（部分点位失败） |

---

## 6. 产线与默认模板

内置产线名（`LineCatalog`）：

| 产线 | 模板默认 IP（种子） | 说明 |
|------|---------------------|------|
| 先河热熔胶复合机 | 172.14.1.200 | 含上展开转速率等完整热熔胶点表 |
| 华迪热熔胶复合机 | 172.14.1.201 | 车速 D18；上下展开转速率；无上下卷出转速率 |
| 撒粉复合机 | 172.14.1.202 | 点表以 Excel 为准 |
| 平板复合机 | 172.14.1.203 | 点表以 Excel 为准 |
| C型火焰复合机 | 172.14.1.204 | 点表以 Excel 为准；现场多为 S7 + IW 地址 |

**热熔胶类默认 REAL 点位（节选，以 Excel 点表为准）**

| 名称 | 典型地址 | 单位 / 说明 |
|------|----------|-------------|
| 运行状态 | D1000 | Int16：0 停止 / 1 运行 / 2 待机 |
| 当前注胶机编号 | D1130 | — |
| 当前工作胶盘/胶管/胶枪温度 | D6100 / D6120 / D6140 | ℃；MQTT `rrjwd` / `jgwd` / `jqwd` |
| 油温机温度 | D6200 | ℃ |
| 车速 | D1030（华迪可为 D18） | — |
| 卷曲张力 | D1090 | — |

**手动输入**（不读 PLC）：胶辊型号、胶水型号、产品货号（`cphh`）、门幅、厚度等；华迪可含注胶量。

字段映射参考：`config/热熔胶复合机字段映射.xlsx`。

---

## 7. 部署与运行（Windows）

| 场景 | 方式 |
|------|------|
| 开发调试 | Visual Studio F5，`net10.0-windows10.0.19041.0` |
| 安装包 | Release 发布或 `build-installer.bat` → `installer/output/IndustrialMonitor-{版本}-r{修订}-Setup.exe` |
| 现场工控机 | 安装包向导；可选 **安装并启动后台采集服务** |
| 无 PLC 联调 | 模拟数据 + 本地 Broker（如 Mosquitto） |

**安装包**

- Inno Setup 自包含 Release，无需预装 .NET  
- 可选桌面图标、开机启动、**Windows 服务**  
- 卸载可选删除用户数据  

**数据目录**

- 优先 `C:\ProgramData\com.industrial.monitor\Data`（lines、SQLite、日志）  
- 不可写时回退 LocalAppData / 临时目录（诊断页显示实际路径）  

**运行环境**

- Windows 10 1809+ x64  
- Debug 需 [Windows App Runtime 1.7](https://aka.ms/windowsappsdk/1.7/latest/windowsappruntimeinstall-x64.exe)  

**Android**

- APK：`IndustrialMonitor-{版本}-r{修订}-android.apk`  
- 产线 Excel 从内置资源复制到应用可写目录  

---

## 8. 文件与目录

```
huaGuang/
├── config/lines/                    ← 仓库内产线 Excel 模板（安装到 lines/）
├── config/热熔胶复合机字段映射.xlsx   ← 字段 id 参考
├── installer/
│   ├── IndustrialMonitor.iss
│   └── output/
├── docs/
│   ├── DESIGN.md                    ← 本设计手册
│   ├── ARCHITECTURE.md              ← 代码架构
│   ├── CHANGELOG.md
│   └── images/
├── src/
│   ├── HuaGuang.Monitor/            ← MAUI UI
│   ├── HuaGuang.Monitor.Core/       ← 模型、Excel、映射、历史
│   ├── HuaGuang.Monitor.Runtime/    ← 采集、MQTT、PLC、IPC
│   └── HuaGuang.Monitor.Service/    ← Windows 后台服务
├── test/HuaGuang.Monitor.Tests/
├── tools/
└── scripts/
```

---

## 9. 修订记录

| 版本 | 日期 | 说明 |
|------|------|------|
| 1.1.10 | 2026-09-11 | 设计手册对齐：五条产线、Excel 权威配置、采集/发布周期分离、多 MQTT、S7、服务与诊断 |
| 1.1.8 | 2026-09-08 | 崩溃/退出日志；订阅历史全量与字段映射展示 |
| 1.1.7 | 2026-09-04 | 撒粉/平板/C型火焰 Excel；当前工作温度点位；Windows 服务 IPC |
| 1.1.6 | 2026-09-02 | 后台采集服务；扫码点位；运行状态 0/1/2 |
| 1.1.5 | 2026-08-25 | MQTT 异步出站队列；Modbus 批量读 |
| 1.1.4 | 2026-08-24 | 产品货号扫码；历史自定义时间 |
| 1.1.3 | 2026-08-22 | 历史表格与删除；点位虚拟化；监控页布局 |
| 1.1.0 | 2026-08-20 | 订阅模式、Inno 安装包、开机自启 |
| 1.0 | 2026-08-19 | 初版 Modbus 采集与 MQTT |

更多条目见 [CHANGELOG.md](CHANGELOG.md)。
