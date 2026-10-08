# 内网测速（LanSpeed）

局域网测速工具。每台电脑运行同一个程序即可互相测速：内置 / 使用 iperf3，
节点对等、无中心服务器、不需要 SSH 与账号。**Windows 与 Linux 节点可互通互测。**

> 设计文档：[docs/DESIGN.md](docs/DESIGN.md)（架构、接口、移植规格与里程碑）。

## 当前状态（2026-10-08）

| 里程碑 | 状态 |
|---|---|
| M1 引擎与命令行 | ✅ 解析器 + 67 项单元测试、Kestrel 控制接口、iperf3 子进程管理（Job Object / 端口自动选择 / maxSeconds 硬上限） |
| M2 发现与扫描 | ✅ UDP 39300 广播发现、ping/ARP/端口探测扫描、网卡过滤、CLI `scan`（实测列出全网 79 台主机并正确标记状态） |
| M3 分组测速 + 界面 | ✅ 星形 / 矩阵分组、结论规则（很快/正常/偏慢/很差 + 建议）、历史存储与 CSV 导出；**WinUI 3 五页（主机/分组/结果/历史/设置）+ 托盘** |
| 跨平台 | ✅ Linux 节点（Ubuntu 24.04 实测）：使用系统 iperf3，自动检测 `--json-stream` 能力并回退 `-J` 经典输出 |
| M4 交付 | ✅ MSIX 打包签名（自签名测试证书，清单含 StartupTask + 防火墙规则）、防火墙自检与一键放行（UAC）、开机自启、被测通知与一键中止、检查更新；正式代码签名证书与自动更新服务待发布时接入 |

已实测（Windows 10 ↔ Ubuntu 24.04，千兆有线）：
TCP 正向 921.9 Mbps（92% 线速）、反向 867.8 Mbps、双向同跑 830/773 Mbps、
UDP 200M 打满并正确提示封顶；两个方向发起均通过；星形 / 矩阵分组通过。

## 构建与测试

需要 .NET 10 SDK（Windows 与 Linux 均可构建；WinUI 界面到 M3 才需要 Visual Studio 2026）。

```
dotnet build
dotnet test
```

发布 Linux 版（自包含，目标机器无需安装 dotnet）：

```
dotnet publish src/LanSpeed.Cli -c Release -r linux-x64 --self-contained -o publish/linux-x64
```

## 使用

**图形界面（Windows）**：

```
dotnet run --project src/LanSpeed.App
```

或安装 MSIX 包（见下节）。界面六页：
- **主机**：打开自动扫描（可关），主机表带状态与快速测速；
- **两机测试**：A、B 从扫描结果下拉选择或手动输入任意 IP（跨网段可用）；A 选「本机」即常规单测，A、B 都选远程主机时本机仅作发起机（打流只在两机之间）。结果为 **1Panel 风格报表**：结论徽章横幅、蓝色 A→B / 绿色 B→A 大数字卡（无流量方向自动置灰）、峰值/重传/RTT/抖动/链路上限指标卡、**LiveCharts2 逐秒速率曲线**（底部图例 + 坐标轴）；
- **分组**（星形 / 矩阵）、**结果**、**历史**、**设置**（允许被测 / 端口 / 防火墙自检与一键放行 / 开机自启 / 检查更新）。
关闭窗口隐藏到托盘，退出走托盘菜单。诊断日志：`%LOCALAPPDATA%\LanSpeed\ui-debug.log`。

**应用图标**（`src/LanSpeed.App/Assets/`，`scripts/make-icons.py` 可重新生成）：品牌蓝渐变圆角底 + 白色速度表盘；
`app.ico`（16–256 多尺寸）嵌入 exe（任务栏/窗口/文件管理器）并用于 MSIX 徽标；托盘常态用 `app.ico`，
测速中自动切换 `app-busy.ico`（绿色徽章 + 双向箭头）。

**MSIX 安装包**：`dotnet build src/LanSpeed.App -c Release` 产出
`packaging/AppPackages/LanSpeed.App_0.1.0.0_x64_Test/LanSpeed.App_0.1.0.0_x64.msix`（已用自签名测试证书签名）。
首次安装需信任证书（管理员 PowerShell）：

```
Import-Certificate -FilePath packaging\LanSpeed.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage packaging\AppPackages\LanSpeed.App_0.1.0.0_x64_Test\LanSpeed.App_0.1.0.0_x64.msix
```

证书重新生成用 `packaging/make-cert.ps1`（并把新指纹更新到 LanSpeed.App.csproj）。

**命令行**（被测端 Linux/Windows 皆可；发起端同）：

```
dotnet run --project src/LanSpeed.Cli -- serve                # 常驻节点
dotnet run --project src/LanSpeed.Cli -- scan                 # 扫描
dotnet run --project src/LanSpeed.Cli -- test <IP> -t 10 -P 4 # 单对测速（-R/--bidir/-u -b）
dotnet run --project src/LanSpeed.Cli -- group star --center <IP1> <IP1> <IP2>
dotnet run --project src/LanSpeed.Cli -- group mesh <IP1> <IP2>
dotnet run --project src/LanSpeed.Cli -- history [--export out.csv]
```

Linux 发布（自包含，目标机器无需 dotnet）：

```
dotnet publish src/LanSpeed.Cli -c Release -r linux-x64 --self-contained -o publish/linux-x64
```

## 已知问题（引擎层，非本工具缺陷）

- Windows（cygwin 版 iperf3）：127.0.0.1 上跑高带宽 UDP 报 `Resource temporarily unavailable`，
  低速率（如 `-u -b 10`）正常，跨机器不受影响。
- 旧版 iperf3（如 Ubuntu 24.04 的 3.16）：UDP 多流 + JSON 输出会段错误（退出码 139），
  单流 `-P 1` 正常；工具会给出明确的错误提示。
- Windows 首次运行 `serve` 时若 Windows 防火墙弹窗，请允许（或手动放行 TCP 39301、TCP/UDP 5201–5210、UDP 39300）。

## 仓库结构

```
LanSpeed/
├── LanSpeed.slnx
├── Directory.Build.props
├── assets/iperf3/win64/           # iperf3.exe、cygwin1.dll、SHA256SUMS（Windows 专用）
├── docs/DESIGN.md                 # 设计文档（含与 1Panel 源码的核对记录）
├── src/
│   ├── LanSpeed.Core/             # 全部核心逻辑（跨平台；Windows API 以运行时守卫包含在内）
│   ├── LanSpeed.App/              # WinUI 3 界面（M3 起实现，现为占位）
│   └── LanSpeed.Cli/              # 跨平台命令行：serve / scan / test / group / history
└── tests/LanSpeed.Core.Tests/     # xUnit（67 项，含真实 iperf3 输出样本驱动）
```
