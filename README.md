# GDA-ZZZ

重新编写的 Windows PC 端，只做绝区零。兼容现有 GDA Android App，不修改手机端。

**当前版本尚未完成真实游戏终验，不能宣称已经实测完成游戏日常。**
本机已有的所谓游戏文件实际上是重命名的 Windows Notepad，详见 `docs/ACCEPTANCE.md`。

## 使用

运行 `GDA-ZZZ-Setup.exe`，从桌面打开 GDA-ZZZ。点击“安装绝区零 Provider”，
程序下载 OneDragon 官方 v2.6.0 完整环境包（约 715 MB），安装独立 Python 和离线依赖。
安装时不需要已有 Python、Git、旧版 GDA 或手工运行 pip。

自动查找常见目录中的游戏；也可以直接选择 `ZenlessZoneZero.exe`。
点击“启动绝区零”或勾选日常后点击“开始任务”。运行日志和错误直接显示。
窗口关闭后保留托盘，可继续从手机操作；通过“退出 GDA”彻底退出。

在原有 Android App 的连接电脑页面扫描 PC 显示的二维码。
手机和桌面提交的任务使用同一个队列和 OneDragon 执行入口。
手机沿用 App 已有的状态轮询，不另建事件服务。

第一版支持影像店、刮刮乐、咖啡、日常奖励，以及 OneDragon 提供的丽都周纪、丽都城募、每日签到。
本版未实现清体力、周常战斗、养成规划、AI 对话、其他游戏、自更新或云托管。
不支持的任务会明确返回原因，不会把未执行的任务写成成功。

运行需要完整游戏客户端、已登录的游戏账号和可交互的 Windows 桌面。
游戏与 GDA 应具有相同的 Windows 权限，才能输入键鼠。

## 数据与实现

程序安装在当前用户的 `LocalAppData/Programs/GDA-ZZZ`；
配置、日志、配对信息和 OneDragon 位于 `LocalAppData/GDA-ZZZ`。
可运行目录也可以直接解压使用。删除程序不删除游戏或旧 GDA。

PC 是一个 WPF 进程，内含仅监听回环地址的移动业务 API；
一个小型 Rust Iroh 进程负责现有手机协议；每次任务启动一个 OneDragon Python 进程。
不需要旧 GDA 的 Brain、Trigger 服务、签名目录、描述符、安装收据或版本激活。
请求重传按 requestId 返回原任务；新的请求可以重复执行已完成任务。

## 开发

需要 Windows x64、.NET 10 SDK、Rust 及 MSVC 构建工具。

```powershell
./tools/Build.ps1
```

生成独立运行包及自包含安装程序，不需要目标机器预装 .NET。
回归检查使用隔离子进程验证排队、取消、失败、持久化及 Android 契约；不代表真实游戏验收。

旧项目和 Android 工作区均只读参考。此仓库与旧 PC 实现没有运行依赖。
