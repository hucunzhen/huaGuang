# Windows 安装包

## 现场安装（工控机）

1. 复制 **`IndustrialMonitor-{版本}-r{修订}-Setup.exe`**（如 `IndustrialMonitor-1.1.3-r3-Setup.exe`）到工控机（U 盘、网络共享均可）
2. **双击**安装包，按向导「下一步」完成
3. 建议勾选：
   - 创建桌面快捷方式
   - 开机自动启动
4. 安装完成后打开「工业监控」，在「设置」里配置 PLC / MQTT

无需命令行，无需预装 .NET。

卸载：Windows「设置 → 应用 → 已安装的应用」中找到「工业监控」卸载。

卸载时会删除程序目录。卸载开始时会询问是否同时删除用户数据（`settings.json`、历史数据库、AppData 中的产线 Excel 等）；默认选「否」，保留用户数据。

## 开发机生成安装包

1. 安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)
2. Visual Studio：配置 **Release**，目标框架 **net10.0-windows10.0.19041.0**，执行 **发布**
3. 输出 **`installer/output/IndustrialMonitor-{版本}-r{修订}-Setup.exe`**

版本号 / 修订号来自 `src/HuaGuang.Monitor/HuaGuang.Monitor.csproj` 中的 `ApplicationDisplayVersion` / `ApplicationVersion`。

未安装 Inno Setup 时发布会成功，但会跳过安装包并提示警告；也可双击 **`build-installer.bat`** 手动打包。
