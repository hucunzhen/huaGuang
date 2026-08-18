# 华光工业监控（.NET MAUI）

Windows / Android 共用一套代码：按设定周期从 **信捷 XD5E-60T10** 读点位，再以 JSON 通过 MQTT 发布。

## 支持的系统

| 平台 | 要求 |
|------|------|
| Windows | **Windows 10 1809 及以上 64 位**（含 21H2 / 22H2），也可用 Windows 11 |
| Android | **Android 11（API 30）工业平板可装可跑**。安装下限 API 24；APK 含 `arm` / `arm64` |

现场 Win10 工控机不必预装 .NET：Release 发布为自包含，并带上 Windows App SDK。

```powershell
dotnet publish src/HuaGuang.Monitor/HuaGuang.Monitor.csproj -f net10.0-windows10.0.19041.0 -c Release -r win-x64 --self-contained
dotnet publish src/HuaGuang.Monitor/HuaGuang.Monitor.csproj -f net10.0-android -c Release
```

调试：Visual Studio 打开 `HuaGuang.Monitor.slnx`，工具栏框架选 **net10.0-windows10.0.19041.0**，启动目标选 **Windows Machine**。若出现“未引发 CoreCLR 启动事件”，先 `dotnet clean`，再生成一次后 F5。

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

默认载入 **先河热熔胶复合机**（`192.168.6.10`）点表。设置里可切换 **华迪**（`192.168.6.20`）。胶辊/胶水型号、门幅、厚度为手动填写，不采集。
