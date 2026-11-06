# QuietEQ

一个面向 Equalizer APO 的 Windows 桌面前端。设备、EQ 效果和配置明确分开；多个输出设备的 EQ 同时安装，由 Windows 实际输出设备决定使用哪一份，前端不需要常驻。

## 启动

构建后的应用位于 `artifacts/app/QuietEQ.exe`。依赖 Windows 10/11 和 .NET 9 Desktop Runtime（当前开发机器已具备）。Equalizer APO 仍负责系统音频处理，本项目不安装或替代 APO 驱动。

1. 在 **AutoEQ** 输入品牌或型号，选择测量来源，下载 Parametric EQ 或 Graphic EQ；也可以在 **EQ 效果库** 导入 `.txt` 或新建效果。
2. 在 **设备** 选择对应 Windows 输出设备，点击 **绑定 EQ**。多个设备分别设置。
3. 在 **配置** 检查启用的绑定，点击 **预览并应用**。应用会备份并替换 APO 的 `config.txt`。
4. 在 Windows 控制面板切换默认输出。每台设备使用自己的 EQ；关闭 QuietEQ 不影响已写入的规则。

同一设备可保存多份配置，但只启用一份；启用另一份时会停用旧配置。未绑定设备不会添加 EQ。不同设备名称相同也可通过端点 GUID 区分。若两副耳机共用同一 DAC/插孔，Windows 只看见一个端点，需要手动切换该端点的配置。

## 编辑与导入

- 支持前级增益、PK/PEQ、LSC、HSC（带 Q）以及 GraphicEQ。支持频段启用/停用、增加/删除、编辑频率/增益/Q、复制、导出。
- 图示 EQ 频率必须递增且不重复；保存时按频率排序。参数与图示 EQ 不混合导入。其他 APO 指令会明确拒绝，不会静默删掉。
- 参考响应曲线按 48 kHz 绘制，包含前级增益；不等于设备实测频响，也不代表实际音频链路已经验证。
- 保存效果/配置只更新本地资料。只有 **预览并应用** 才更新音频规则。
- AutoEQ 来源为上游公开 `results/INDEX.md` 和对应的预计算文本文件；搜索目录可离线缓存，新的效果下载需要网络。保留测量来源和下载 URL。

## 文件和恢复

- 数据库：`%LOCALAPPDATA%/QuietEQ/library.json`。
- 可读效果文件：`%LOCALAPPDATA%/QuietEQ/effects/<id>.txt`。
- APO 路径从注册表读取，也可通过窗口底部 **配置目录** 指定。
- 应用输出：`<APO config>/QuietEQ/<generation>/<effect id>.txt`。所有效果文件落盘后才原子替换根配置，旧代际保留，避免中途出现缺失引用。
- 备份：`<APO config>/QuietEQ/backups/`。**恢复上次应用前的配置** 可撤回最近一次应用。若外部工具已改动根配置，会拒绝自动覆盖。
- Peace 应退出并停止其自动启动，否则它可能重新接管 `config.txt`。QuietEQ 检测到 Peace 运行时会阻止应用，不会擅自结束 Peace 或修改它的自启动设置。
- 每台输出设备仍需在 APO Configurator 中安装 APO。界面仅报告是否存在 APO 注册记录，不声称实际音频处理已验证。音频增强被关闭、独占输出、驱动绕过系统效果等仍可能导致 EQ 不生效。
- 写入受保护的配置目录时，需要以有权限的用户运行；应用默认不请求管理员权限。

## 构建和验证

```powershell
dotnet build src/QuietEQ.App/QuietEQ.App.csproj -c Release
dotnet run --project tests/QuietEQ.Tests/QuietEQ.Tests.csproj -c Release -- --network --report artifacts/test-results.json
dotnet publish src/QuietEQ.App/QuietEQ.App.csproj -c Release --no-self-contained -o artifacts/app
```

应用支持隔离演示和窗口截图检查：

```powershell
artifacts/app/QuietEQ.exe --demo
artifacts/app/QuietEQ.exe --smoke-test --data-dir artifacts/ui-smoke
```

隔离模式使用独立数据和沙箱 APO 配置目录，不写真实 APO 配置。演示耳机名称和曲线是测试数据。`--smoke-test` 渲染四个页面、编辑器、绑定窗口、应用预览，并输出截图和结果。

只读系统设备探测（供诊断/验收）：

```powershell
artifacts/app/QuietEQ.exe --probe artifacts/device-probe.json
```

## 架构

- `QuietEQ.Core`：数据模型、严格 EQ 解析、设备配置编译、原子安装/备份恢复、AutoEQ 客户端、参考曲线。
- `QuietEQ.App`：WPF 桌面界面、Windows Core Audio 端点枚举、配置流程。
- `QuietEQ.Tests`：可直接运行的回归测试项目，覆盖设备隔离、持久化、错误处理、响应曲线及可选真实网络测试，无第三方测试框架依赖。

参考上游：Equalizer APO Configuration reference 和 Jaakko Pasanen 的 AutoEq。AutoEq MIT 许可见 `THIRD_PARTY_NOTICES.md`。QuietEQ 为独立前端，与上游项目无隶属关系。

## 许可证

本项目采用 [GNU General Public License v3.0](LICENSE) 许可证。第三方许可和署名见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
