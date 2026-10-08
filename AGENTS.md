# AGENTS.md

- 默认用简体中文沟通，代码注释用简体中文。
- 设计以 `docs/DESIGN.md` 为准；实现与文档有冲突时，先改文档再改代码。
- 每次改动后运行 `dotnet build` 与 `dotnet test`，保持零警告、全绿（`TreatWarningsAsErrors` 已开启）。
- 控制接口不要用 `HttpListener`（非本机监听需要管理员 urlacl），用 Kestrel。
- 不要给控制接口加执行任意命令或读写文件的能力；只接受私有地址段的请求。
- 用户可见名称一律「内网测速」；代码标识（命名空间、程序集、MSIX 包标识）用 ASCII 的 `LanSpeed.*`。
- **仅 Windows 10 1809+**：不做 Linux / macOS 节点；跨平台测速继续用 1Panel。Core / CLI / App 均面向 Windows。
- iperf3 资源只在 `assets/iperf3/win64/` 更新，并同步更新 `SHA256SUMS`；运行时按校验落盘
  `%LOCALAPPDATA%\LanSpeed\iperf3\<版本>\`，`iperf3.exe` 与 `cygwin1.dll` 必须同目录；产品路径固定 `--json-stream`。
- 解析、方向映射、汇总语义的权威定义在 `docs/DESIGN.md` 附录 A；改动前先读对应小节，
  附录 B 的测试用例是行为基线，不允许为了通过实现而修改测试语义。
- 本地持久化用 SQLite（`%LOCALAPPDATA%\LanSpeed\lanspeed.db`）：软配对主机 + 历史（上限 100）。
- 低于千兆（协商 &lt; 1000 Mbps）只做醒目标记与排查提示，**不拦截开测**。
- 服务端 maxSeconds 到期杀进程后必须清空槽位状态，否则节点永久 busy（历史踩坑，勿回退）。
- `src/LanSpeed.App` 是 WinUI 3 工程（Windows App SDK + H.NotifyIcon 托盘 + CommunityToolkit.Mvvm）：
  它参与构建但不受 `TreatWarningsAsErrors` 约束（XAML 生成代码告警不受控）；
  界面逻辑一律复用 `LanSpeed.Core`（Scanner/GroupRunner/HistoryStore/PairedHostStore），不要在页面里复制业务逻辑；
  Win11 启用 Mica，Win10 回退默认背景。
- MSIX 打包在 Release 配置（`dotnet build src/LanSpeed.App -c Release`），签名证书用
  `packaging/make-cert.ps1` 生成自签名测试证书并把指纹更新进 csproj；正式发布必须换正式代码签名证书。
