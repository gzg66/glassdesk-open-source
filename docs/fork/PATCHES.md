# patch 台账（自动生成，勿手改）

生成时间: 2026-10-20T11:40:30Z ｜ 上游: `upstream/main` @ c825c9d ｜ 集成分支: `main`

每行的明细在 `docs/fork/patches/`。改 patch 就改那里的文件，再重建本表。

| # | 提交 | 改动文件 | 龄期(天) | 明细文档 |
|---|---|---|---|---|
| 000 | patch(000): chore(fork): 初始化二开薄补丁脚手架 | 19 | 0 | (脚手架) |
| 001 | patch(001): chore(fork): 二开工作流文档与发布/同步脚本 | 11 | 0 | 001-fork-workflow-scaffold.md |
| 002 | patch(002): feat(app): 启动台滚轮翻页 | 5 | 0 | 002-launcher-wheel-navigation.md |
| 003 | patch(003): feat(app): 启动台「从桌面批量添加…」+ 本地化显示名 | 8 | 0 | 003-launcher-desktop-add.md |
| 004 | patch(004): fix(app): 修「显示方式」保存时被 Normalize 改回 | 9 | 0 | 004-dock-display-mode.md |
| 005 | patch(005): feat(app): Win 键手势状态机 + 低级键盘钩子 | 4 | 0 | 005-winkey-gesture.md |
| 006 | patch(006): feat(app): 原生任务栏接管 + 启动台托盘入口 | 9 | 0 | 006-native-taskbar-tray.md |
| 007 | patch(007): fix(app): 全屏误判 / 启动台焦点与搜索 / 发布保留数据 | 11 | 0 | 007-fullscreen-focus-publish.md |
| 008 | patch(008): docs(fork): 同步模型改为 rebase 重放，legacy PowerShell 脚本降级 | 8 | 0 | 008-sync-model-rebase.md |
| 009 | patch(009): chore(fork): 更新 .fork/bin 工具并装 systemd 每日巡检 | 4 | 0 | 009-fork-tooling-update.md |
| 010 | patch(010): fix(app): Win 组合键补发错键，Win+D 等全部失效 | 9 | 0 | 010-winkey-chord-replay-fix.md |

合计: 11 个 patch（预算 40）
