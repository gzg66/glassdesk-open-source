# patch 010: winkey-chord-replay-fix（Win 组合键补发错键）

- 开单: 2026-10-20
- 状态: 已合入
- 能不能不改源码做到？ 否 —— 补发 Win 键是钩子内部的实现细节，没有配置点可用
- 为什么上游不会接受 / 该不该推上游: 上游没有「改 Win 键行为」的路线（同 patch(005)），属 fork 私有交互
- 触及的上游文件:
  - `src/GlassDesk.Platform/Input/WinKeyHook.cs`（改 3 处：记住 Win 键身份、补发时用它、补发时带上扫描码/扩展位）
  - `src/GlassDesk.Platform/GlassDesk.Platform.csproj`（加一条 `ProjectReference` → `GlassDesk.Core`）
- 新增文件:
  - `src/GlassDesk.Core/Input/WinKeyIdentity.cs`
  - `tests/GlassDesk.Core.Tests/WinKeyIdentityTests.cs`
- 附带文件（与本次修复无关，按仓库主人要求随本提交收录）:
  - `docs/third-party/wxextract-tool.zip`（微信导出工具包，12MB 第三方二进制：`wxkey.exe` + Python 运行时 + 脚本）
- 上游同类实现/已有 PR: 无
- 回滚方式: 可单独 revert（补丁只影响补发用的键，撤掉后回到 patch(005) 的原始行为）
- 验证:
  - [x] `WinKeyIdentityTests` 4 条（Core 全绿 185/185）
  - [x] `dotnet build src/GlassDesk.App -c Release` → 0 warning / 0 error
  - [x] 端到端（真实进程 + 下游钩子取证）：Win+D 显示桌面、Win+E 打开资源管理器、Win+R 打开「运行」、单击 Win 切换启动台
- 手验步骤（用户侧）:
  1. 设置中心开启「隐藏 Windows 原生任务栏」
  2. 单击 Win → 启动台打开/关闭
  3. Win+D → 显示桌面 / 再按恢复
  4. Win+E、Win+R、Win+Tab、Win+Shift+S、Win+A 等逐条试
  5. 退出 GlassDesk → Win 键恢复系统开始菜单

## 目的（一句话，写给人看，不是写给编译器看）

Win 组合键（Win+D 等）全部失效，只有「单击 Win 打开启动台」正常。

## 根因

`WinKeyHook.OnHookMessage` 里补发 Win 键时传的是**触发组合键的那个事件**的虚拟键码：

```csharp
if (decision.ReplayWinDown) SendWinKey(keyUp: false, (int)data.VirtualKey);
if (decision.ReplayWinUp) SendWinKey(keyUp: true, (int)data.VirtualKey);
```

`ReplayWinDown` 只在「Win 按住 + 按了别的键」时置位，此时 `data.VirtualKey` 是那个字母键
（Win+D 就是 `D`）。于是系统收到的是「单独按了一次 D」，Win 修饰键从头到尾没出现过：

- Win+D → 前台窗口收到一个 `d`，桌面不动
- Win+E → 收到一个 `e`，资源管理器不开
- 抬起 Win 时又补发一次 `Win up`（这次 `data.VirtualKey` 恰好是 Win 键），留下一次孤立的抬起事件

而「单击 Win」走的是另一条分支（吞掉按下/抬起 + 触发启动台），没用到补发，所以看起来
「Win 键接管是好的，只是组合键不灵」——最容易误判成时序问题的症状。

取证方式：在 GlassDesk 之后装一个只读低级键盘钩子，注入一次「物理味」的 Win+D，
观察 GlassDesk 补发的键（日志里 `from=glassdesk`）：

```
#008 vk=0x44 dir=down from=ours       ← 物理 D（GlassDesk 放行）
#009 vk=0x5B dir=down from=glassdesk  ← 修好后补发的是 Win
#012 vk=0x5B dir=up   from=glassdesk
```

修复前同一位置是 `vk=0x44 ... from=glassdesk`（补发了 D）。

## 改动要点

- `WinKeyIdentity`（Core，纯逻辑）：从键盘事件里提取「这次是不是左/右 Win」，不是 Win 键就
  沿用上一次记住的身份；带扫描码与扩展位。可脱离 Win32 单测。
- `WinKeyHook`：物理 Win 事件时更新 `_winIdentity`；补发一律用它与对应扫描码/扩展位，
  不再使用 `data.VirtualKey`。
- 补发时保留 `LLKHF_EXTENDED`（数值与 `KEYEVENTF_EXTENDEDKEY` 相同）→ 右 Win 也能正确补发。

## 风险 / 以后要盯的地方

- 组合键仍然依赖一次 `SendInput` 补发 Win 按下：前台窗口属于**更高完整性级别**（管理员窗口）时
  受 UIPI 限制，补发可能被拒，此时 Win+X 会失效；这是用户态钩子方案的固有边界。
- 补发事件带 `dwExtraInfo` 标记，钩子靠它放行自己的注入；若将来换成别的注入方式要同步改标记判断。
- 系统「粘滞键 / 筛选键」会改变按键序列，仍属手验项（同 patch(005)）。
- `GlassDesk.Platform` 新增对 `GlassDesk.Core` 的项目引用：分层方向是 Core（底座）→ Platform，
  不构成循环；同步上游时要留意上游是否给 Platform 加了同名引用。
