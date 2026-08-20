# 工业监控（.NET MAUI）

Windows / Android 共用一套代码：**采集模式**下按设定周期从 **信捷 XD5E-60T10** 读点位并以 JSON 发布 MQTT；**订阅模式**下连接同一 Broker，查看局域网内其他设备的遥测。

**当前版本：v1.1.0**（采集 + 订阅、多主题切换、Windows 安装包、开机自启、点位精度与手动标签）

## 支持的系统

| 平台 | 要求 |
|------|------|
| Windows | **Windows 10 1809 及以上 64 位**（含 21H2 / 22H2），也可用 Windows 11 |
| Android | **Android 11（API 30）工业平板可装可跑**。安装下限 API 24；APK 含 `arm` / `arm64` |

现场 Win10 工控机不必预装 .NET：Release 发布为自包含，并带上 Windows App SDK。

### 首次环境（本机）

需要 **.NET SDK 10.0.302+** 与 MAUI 工作负载（`maui-windows`、`maui-android`）：

```powershell
cd d:\work\huaGuang
.\scripts\setup-dev.ps1              # 还原工作负载并验证 Windows 编译
.\scripts\setup-dev.ps1 -InstallMqtt # 可选：winget 安装 Mosquitto 测试 Broker
.\scripts\install-android-sdk.ps1    # Android SDK 安装到 D:\Android\Sdk
```

Android SDK 默认安装路径 **`D:\Android\Sdk`**，并写入用户环境变量 `ANDROID_HOME` / `ANDROID_SDK_ROOT`。首次安装会顺带安装 **Microsoft OpenJDK 17**（Android 编译必需）。**重启终端或 Visual Studio** 后环境变量生效。

Visual Studio 中也可手动指定：`工具 → 选项 → Xamarin → Android Settings → Android SDK Location` → `D:\Android\Sdk`。

### 编译与发布

```powershell
.\scripts\build.ps1                          # Windows Debug
.\scripts\build.ps1 -Configuration Release     # Windows Release
.\scripts\publish-windows.ps1                  # Win10 x64 自包含（RID 见 csproj）
.\scripts\publish-android.ps1                  # Android APK（需 Android SDK）
```

或双击 `build.bat` / `publish-windows.bat` / **`publish-android.bat`**。

**Android 平板安装**

1. 运行 `publish-android.bat`（或上面的 `publish-android.ps1`）
2. 把 **`dist\IndustrialMonitor-1.1-android.apk`** 拷到平板（U 盘 / 数据线；**不要用微信传 APK**，容易损坏）
3. 平板开启「允许安装未知来源应用」，用文件管理器打开上述 APK 安装

> 务必安装 **已签名** 的安装包。`bin\...\publish\` 里带 **`-Signed`** 后缀的才是可安装文件；不带 `-Signed` 的 `.apk` 会提示「安装包似乎无效」。

**生成 Windows 安装包**

- **Visual Studio**：配置选 **Release**，框架选 **net10.0-windows10.0.19041.0**，点 **发布** → 完成后自动生成 `installer\output\IndustrialMonitor-Setup.exe`（需本机安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)）
- **或**双击 **`build-installer.bat`** / 运行 `.\scripts\build-installer.ps1`

关闭自动打安装包：发布时加 MSBuild 属性 `-p:BuildInstallerOnPublish=false`

### 现场安装（Windows 工控机）

**双击 `IndustrialMonitor-Setup.exe`**，按向导下一步即可，无需命令行。

- 安装到 `C:\Program Files\IndustrialMonitor`
- 可选：桌面快捷方式、**开机自动启动**（默认勾选）
- 应用内「设置」可开关「开机自动启动」「启动后自动采集」（均默认开启）
- 支持「程序和功能」中卸载

> 开发/运维备用：`install-windows.ps1`（命令行拷贝安装，一般不必使用）

Release 自包含参数已在 `HuaGuang.Monitor.csproj` 中配置，**不要**在命令行再加 `-r win-x64 --self-contained`（会与 Android 运行时包冲突导致 NU1102）。

调试：Visual Studio 打开 `HuaGuang.Monitor.slnx`，工具栏框架选 **net10.0-windows10.0.19041.0**，启动目标选 **Windows Machine**。若出现“未引发 CoreCLR 启动事件”，先 `dotnet clean`，再生成一次后 F5。

## 本地测试环境（无 PLC）

1. 启动 MQTT Broker：`.\scripts\start-test-mqtt.ps1`（默认 `127.0.0.1:1883`）
2. 另开终端订阅：`.\scripts\subscribe-telemetry.ps1`
3. 运行应用，在「设置」保持 **使用模拟数据** 勾选，MQTT 填 `127.0.0.1:1883`
4. 点「启动采集」，订阅终端应收到 JSON 遥测

**订阅模式联调（第二台或同机第二个实例）**

1. 实例 A：采集模式，发布到 `monitor/{deviceId}/telemetry`
2. 实例 B：设置 → 运行模式选 **订阅模式**，添加主题如 `monitor/+/telemetry`，保存后点 **启动订阅**
3. 首页 **主题筛选** 可选「全部」或单个主题；运行中也可 **添加主题** 而无需停止订阅

参考配置见 `test/settings.sample.json`；`test/mosquitto.conf` 为本地匿名 Broker（仅开发机）。

## 连接 XD5E-60T10

1. PLC 与电脑/平板同一网段，记下 PLC 的 IP。
2. 在 **XDPpro** 中打开以太网设置，启用 **Modbus TCP Server**，端口 **502**，站号默认 **1**。
3. 本软件「设置」里关掉模拟模式，填入该 IP、端口 502、站号。
4. 「点位」直接填信捷元件，不要填 40001 这种 Modbus 编号：
   - `D0`、`D100`、`HD0`：数据寄存器
   - `M0`、`M10`：辅助继电器
   - `X0`、`X20`：输入（**八进制**，X7 下一个是 X10）
   - `Y0`、`Y10`：输出（同样是八进制）
5. 浮点占用两个 D，默认字节序 **CDAB**。例如温度在 D0/D1，下一个浮点用 D2。
6. 点「启动采集」。

XD5E-60T10 本体大约 **36 入 / 24 晶体管出**，输入约 `X0–X43`，输出约 `Y0–Y27`。

默认载入 **先河热熔胶复合机**（`192.168.6.10`）点表。设置里可切换 **华迪**（`192.168.6.20`）。默认含胶辊/胶水型号、门幅、厚度（手动输入，不读 PLC）。

各点位可在「点位 → 编辑」单独设置 **显示精度**（0–4 位小数）；未设置时使用设置页的全局温度精度。

## 运行模式

| 模式 | 说明 |
|------|------|
| **采集模式** | 读 PLC（或模拟）→ 发布 MQTT |
| **订阅模式** | 订阅一个或多个 MQTT 主题，展示远程设备遥测；首页可随时切换主题筛选 |

订阅主题支持 MQTT 通配符 `+`、`#`。多个主题在 **设置** 中维护，首页可快速添加。

## 设计手册

界面说明、架构与 MQTT 报文格式见 **[docs/DESIGN.md](docs/DESIGN.md)**（含 Windows 界面截图）。

导出 PDF：

```powershell
python scripts/export-design-pdf.py
# 或双击 scripts/export-design-pdf.bat
```

输出：`docs/DESIGN.pdf`
