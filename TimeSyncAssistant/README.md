# 时间同步助手

![时间同步助手图标](App/Assets/TimeSync.png)

[下载 Windows 发布包](https://github.com/ZhuYongZheng0603/MyGit/releases/latest)

适用于 Windows 11 的中文桌面校时工具，默认将 Windows 时区设为北京时间（China Standard Time，UTC+08:00），并使用 NTP 校准电脑系统时间。支持 Windows 安装的全部时区，自动遵循所选地区的夏令时规则。

## 运行

1. 下载对应架构的 ZIP 并解压到本地文件夹。常见 Intel / AMD 电脑选 `win-x64`，ARM 设备选 `win-arm64`。
2. 双击 `TimeSyncAssistant.exe`，允许 Windows 管理员权限提示。程序需要修改系统时区和时间；取消权限提示则不会启动。
3. 默认目标为北京时间。可先点击「检测网络时间」，查看网络时间、时差和延迟，此操作不会修改系统。
4. 点击「立即同步」，获取网络时间后修改系统时区和时间。启动程序、选择时区不会自动修改系统。

发布包自带 .NET 10 运行时，无需安装 Python、.NET SDK 或其他开发工具，也无需安装本应用。首次启动需要解压内置运行时，可能稍慢。本程序未做商业代码签名，Windows 可能显示未知发布者；请在确认文件来源后运行。

## 功能

- 时区默认北京时间，支持切换到东京、伦敦、纽约等 Windows 时区。
- 默认 `ntp.aliyun.com`，失败后依次尝试 `ntp.tencent.com`、`time.windows.com`、`time.cloudflare.com`；支持填写自定义域名或 IP，不要包含协议或端口。
- 使用 NTP 四时间戳算法估计偏差和网络往返延迟；检查数据包长度、版本、服务器同步状态、层级、请求匹配和时间戳。
- 同步前先获取有效网络时间，网络不可用时不修改时区；时区变更后校时失败，会尝试恢复原时区，恢复失败会在日志提示。
- 校时偏差超过 5 分钟时，手动操作需要确认；自动同步会跳过并提示。
- 可勾选每 60 分钟自动同步。**仅在本程序运行时生效**；关闭后停止，重新启动默认关闭，不添加开机启动或计划任务。
- 时区和服务器选择保存在 `%LOCALAPPDATA%\TimeSyncAssistant\settings.json`，日志只在当前窗口保留。

## 时间与时区的关系

系统内部保存 UTC 时间，任务栏显示时间由系统时区决定。因此应用通过「校准 UTC + 设置目标时区」同步到北京时间，不会把北京时间当作 UTC 写入系统。预览卡片使用当前本机时钟换算，并不表示已通过网络校准；网络检测结果在下方显示。

## 网络和系统条件

- 需能解析时间服务器域名并访问 UDP 123；普通网页可访问，不代表 NTP 端口可用。公司或校园网络可填写网络管理员提供的 NTP 地址。
- 本工具直接执行一次性系统校时，不重配置 Windows Time 服务。若系统开启「自动设置时区」，或受域策略、其他校时软件管理，之后可能被覆盖；需要固定时区时请在 Windows 的「时间和语言 → 日期和时间」中关闭「自动设置时区」。
- 普通 NTP 不提供加密身份认证；适合日常桌面校时，不作为金融交易、取证或其他高精度可信时间服务。系统年份若相差超过约 68 年，请先在 Windows 设置中修正大致年份。
- 单位策略禁止改时区/系统时间时，管理员运行仍可能失败，请查看日志。同步会影响时间显示和依赖系统时钟的软件。

## 从源码构建

安装 .NET 10 SDK，在项目目录运行 PowerShell：

```powershell
.\build.ps1                     # Intel / AMD 64 位
.\build.ps1 -Runtime win-arm64   # ARM64
```

输出位于 `dist`。也可在 Linux 上使用 .NET 10 SDK 交叉发布：

```sh
dotnet run --project Tests/TimeSync.Tests.csproj -c Release
dotnet publish App/TimeSyncAssistant.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o dist/win-x64
```

`Core` 为独立校时逻辑，`App` 为 Windows Forms 界面与系统接口，`Tests` 为不修改系统时钟的协议验证及本地 UDP 集成检查。

## 验证范围

已在 Linux 构建环境编译 Windows 发布包，验证 NTP 偏差/延迟算法、无效响应拒绝、2036 年时间戳回绕、北京时间和夏令时换算、本地 UDP 通信和取消超时。构建环境不能实际运行 Windows 图形界面或调用 Windows 设置系统时间的接口；在 Windows 11 上首次使用时建议先检测，再执行同步。
