# patch 013: packaging-scripts

- 开单: 2026-10-21
- 状态: 待评审
- 能不能不改源码做到？结论: **能，就是不动源码**。这不是改代码，是把上游 v1.4.0 里终于公开的打包脚本原样取进来。
- 为什么上游不会接受 / 该不该推上游: 上游已经发布了这些文件（v1.4.0 快照），我们只是取用；没有需要推回的内容。
- 触及的上游文件: 无（全部是新增文件）
- 新增文件:
  - `build/package.ps1`（591 行，原样）
  - `build/verify-package.ps1`（260 行，原样）
  - `packaging/GlassDesk.iss`（60 行，原样）
  - `packaging/release.json`（19 行，原样）
  - `docs/第三方声明.md`、`packaging/RELEASE-NOTES.md`（package.ps1 要求随包携带的声明/说明，原样）
- 来源: 上游快照 `b1eb58c`「Publish GlassDesk 1.4.0 source snapshot」，逐文件 `git show b1eb58c:<path>`
- 回滚方式: 单独 revert 本 patch（纯新增文件）
- 验证:
  - [x] 单元/集成测试: `GlassDesk.AppServices.Tests` **414/414 全绿**（此前 378 中 46 红）
  - [x] 手工验证步骤: 不需要（脚本被测试间接驱动）
  - [ ] `bash .fork/bin/fork-sync.sh --apply` 后仍通过

## 目的（一句话，写给人看，不是写给编译器看）

我们这棵树的 `AppServices` 有 46 条测试一红到底，根因是公开快照缺打包脚本；上游 v1.4.0 补上了，直接取用即可让 46 条转绿，且不用自己维护一份私有脚本。

## 改动要点

零改动风险：四个文件是**逐字节复制**，没有为了适配我们的 fork 改任何一行。它们读取的 AppId（`GlassDesk.Independent`）、SDK（10.0.401）、运行时（10.0.12）、Inno（7.1.0）都是上游默认值——等做品牌隔离（AGENTS.md §4）时，这些常量要跟着改，届时改的是上游文件，冲突面会上升。

## 风险 / 以后要盯的地方

- 这两个脚本**只覆盖「上游签名打包」这一条路**，我们的便携版发布仍然走 `scripts/publish-portable.ps1`，两者互不影响。
- `packaging/release.json` 里 `appId` 是 `GlassDesk.Capture.e3f9f152`、`productVersion` 0.2.4，属上游「捕获验收载荷」的清单，不是主安装包版本；品牌隔离时一起核对。
- 上游同步到 1.4.0 时，这六个文件应当变成「上游已存在」→ 删掉本 patch 的台账即可（不要重复维护）。
