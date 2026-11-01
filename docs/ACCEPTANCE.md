# 0.1.0 实施与验证记录

日期：2026-11-01。

## 当前结果

PC 已在独立仓库从头实现，已生成并实际运行自包含安装程序。
OneDragon 官方完整环境包已下载、解压、离线安装 Python 依赖，并实际导入成功。
已有手机协议的配对、查询、提交、重复请求处理和失败回传已通过 Iroh 实测。
真实游戏闭环、实体 Android 手机闭环均为 **BLOCKED_ENV**；不能称为全部需求已验收完成。

## 范围

只支持绝区零的日常任务；手机和桌面使用同一个队列、同一个 Python runner。
没有旧 PC 的 Brain、Trigger、Provider Catalog、签名、描述符、收据、版本激活或日常完成禁提规则。
仅保留配对设备访问、输入合法性和游戏操作串行执行。

旧 PC 工作树未修改。Android 源码未修改；检查前后源码 SHA-256 清单一致。
新仓库不包含旧应用运行时、旧发布配置、本机身份和测试日志。

## 总数

共 **23 case：PASS 21，BLOCKED_ENV 2，FAIL 0**。
代码调试过程中发现并修复了退出阻塞、子进程输出编码、启动即退出却显示成功的问题。
下表是修复后的结果。环境阻塞不计入 PASS；隔离子进程通过不代表游戏通过。

## 核心回归：13 PASS

通过 `tests/Checks` 执行，使用临时目录和独立子进程，不接触真实游戏：

1. 非绝区零任务不进入队列。
2. 同 requestId 重传返回原任务。
3. 两个任务按顺序执行，运行时间不重叠。
4. 中文子进程日志保持 UTF-8。
5. 已完成日常可以用新请求再次执行。
6. 子进程失败码和具体失败原因回传。
7. 排队任务可取消，取消后不执行。
8. 运行任务可以停止并返回终态。
9. 本地业务 API 拒绝没有控制凭证的访问。
10. profile 与原 Android App 的 API 3.3 / revision 10 一致。
11. Android 批量请求重传不重复添加任务。
12. 队列 JSON 符合 Android 的解析格式。
13. 结果及 requestId 查询在重启后保留。

命令：

```powershell
dotnet build tests/ProcessFixture/ProcessFixture.csproj -c Release
dotnet run --project tests/Checks/Checks.csproj -c Release -- tests/ProcessFixture/bin/Release/net10.0
```

## 集成与界面：8 PASS

| Case | 实际执行与证据 | 结果 |
| --- | --- | --- |
| I01 | `tools/Build.ps1` 构建 Rust、WPF、自包含运行包和单 EXE 安装程序，零编译错误 | PASS |
| I02 | 实际执行安装程序，安装文件与构建输出哈希一致，生成桌面快捷方式并从安装目录启动 | PASS |
| I03 | 实际下载官方 v2.6.0 完整环境包；安装独立 Python 3.11.12 和离线 wheels；实际导入 ZContext、OCR、Windows 控制依赖 | PASS |
| I04 | WPF 实际 SendInput 键盘操作安装按钮、启动按钮、开始任务及退出；启动失败可见，提交后应用存活、可继续操作 | PASS |
| I05 | 与手机相同版本 Iroh 1.0.3、相同帧格式完成配对；profile/catalog/providers/queue 均实际返回 200 | PASS |
| I06 | 经 Iroh 提交任务并重传，taskId 不变；按 requestId 查询返回真实启动失败，中文原因完整 | PASS |
| I07 | 旧 PC Git 工作树无本轮 diff；Android 源码哈希清单前后一致 | PASS |
| I08 | 实际退出后 WPF 和所属手机子进程消失；重新启动后 Provider 状态保留、手机连接码恢复 | PASS |

本地原始证据位于 `artifacts/evidence/`，包括 `installed-ui.png`、
`phone-routes.json`、`phone-submit.json` 和运行记录。
截图含本机连接码，证据目录不进入 Git。

## 真实业务：2 BLOCKED_ENV

| Case | 状态 | 已确认阻塞 |
| --- | --- | --- |
| E01 安装 Provider 后启动真实绝区零并完成日常 | BLOCKED_ENV | 常见游戏目录内的 `ZenlessZoneZero.exe` 实为重命名的 `NOTEPAD.EXE`，文件说明为 Notepad，约 200 KB；不存在可用的完整游戏客户端。启动后没有游戏进程，新 PC 正确返回失败。 |
| E02 原 Android App 操作真实绝区零任务 | BLOCKED_ENV | ADB 无已连接设备；已有 x86_64 AVD 因缺少虚拟化支持启动失败。未改手机、未构建 APK。Iroh 协议测试已完成，不能替代实体手机操作。 |

未执行游戏大文件下载、账号登录、消耗体力、真实战斗或真实奖励领取。
没有要求用户提供设计选择或代为验收；缺失环境无法用单元测试补成通过。

## 交付

- 源码：此仓库 main 分支；本地开发目录为 `C:/dev/GDA-ZZZ`。
- 安装程序：`artifacts/GDA-ZZZ-Setup.exe`。
- 可运行目录压缩包：`artifacts/GDA-ZZZ-win-x64.zip`。
- 已安装程序：当前用户 `LocalAppData/Programs/GDA-ZZZ`，桌面快捷方式 GDA-ZZZ。
- 数据及 OneDragon：当前用户 `LocalAppData/GDA-ZZZ`。
- 安装程序 SHA-256：`2A091806B25E650B4A326345867008E422DAE8366F4C7B9D8BDCE05CB910D43B`。
- 可运行包 SHA-256：`A81F72DD327A29F8C8F3F6455265C03B67D916AEB243E74A51FDEB8D08945EB8`。

尚未发布 GitHub Release，生成物不提交到源码仓库。
剩余关键风险是实际游戏版本、登录状态、游戏分辨率、Windows 输入权限和实体手机跨网络连接尚未终验。
