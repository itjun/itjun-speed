# 内网测速（LanSpeed）

Windows 局域网测速工具。每台电脑运行同一个程序即可互相测速：内置 iperf3，
节点对等、无中心服务器、不需要 SSH 与账号。**仅支持 Windows 10 1809+**（跨平台请用 1Panel）。

> 设计文档：[docs/DESIGN.md](docs/DESIGN.md)（架构、接口、移植规格与里程碑）。

## 当前状态

| 能力 | 状态 |
|---|---|
| 引擎与命令行 | ✅ iperf3 解析 / Kestrel 控制接口 / Job Object |
| 发现与扫描 | ✅ UDP 39300 + ping/ARP/端口探测 |
| 软配对 | ✅ SQLite 记住主机，重启后按节点 ID 刷新 |
| 两机 / 分组测速 | ✅ TCP/UDP × 正向/反向/双向；星形 / 矩阵 |
| 链路健康 | ✅ 低于千兆醒目标记为排查重点（**不拦截开测**） |
| 历史 | ✅ SQLite，上限 100，CSV 导出 |
| WinUI 3 | ✅ NavigationView、Win11 Mica、LiveCharts2、矩阵热力 |
| MSIX / 发布 | ✅ 自签名测试包；`packaging/publish.ps1` 用 GitHub CLI 发布 amd64。低于最低版本强制更新，否则可选。正式代码签名待更换 |

## 构建与测试

需要 .NET 10 SDK 与 Windows（WinUI 界面建议 Visual Studio 工作负载「WinUI 应用程序开发」）。

```
dotnet build
dotnet test
```

## 发布（仅 amd64）

需要已登录的 GitHub CLI（`gh auth login`）。最低版本在 `packaging/min-version.txt`：低于它的已安装版本必须更新，否则是可选更新。脚本不上传 ARM 包。

```
powershell -NoProfile -ExecutionPolicy Bypass -File packaging/publish.ps1 -CheckOnly
powershell -NoProfile -ExecutionPolicy Bypass -File packaging/publish.ps1 -Notes "更新说明"
```

## 使用

**图形界面**：

```
dotnet run --project src/LanSpeed.App
```

界面六页：主机（扫描 / 软配对 / 低于千兆筛选）· 两机 · 分组 · 结果（矩阵热力）· 历史 · 设置。
关闭窗口隐藏到托盘。诊断日志：`%LOCALAPPDATA%\LanSpeed\ui-debug.log`。
本地库：`%LOCALAPPDATA%\LanSpeed\lanspeed.db`。

**命令行（Windows 调试）**：

```
dotnet run --project src/LanSpeed.Cli -- serve
dotnet run --project src/LanSpeed.Cli -- scan
dotnet run --project src/LanSpeed.Cli -- test <IP> -t 10 -P 4
dotnet run --project src/LanSpeed.Cli -- group star --center <IP1> <IP1> <IP2>
dotnet run --project src/LanSpeed.Cli -- history [--export out.csv]
```

## 仓库结构

```
LanSpeed/
├── LanSpeed.slnx
├── assets/iperf3/win64/
├── docs/DESIGN.md
├── src/
│   ├── LanSpeed.Core/     # Windows：引擎、发现、SQLite、评价
│   ├── LanSpeed.App/      # WinUI 3
│   └── LanSpeed.Cli/      # Windows 调试 CLI
└── tests/LanSpeed.Core.Tests/
```
