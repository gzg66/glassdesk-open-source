# 冲突处理决策记录

同一个文件每次 rebase 都冲突，说明**我们的改法和上游的结构预期在打架**。
别每次手工解——把决策记下来，要么一次性改掉结构，要么永久采用固定策略。

## 记录格式

| 文件/路径 | 冲突原因 | 决策 | 落笔日期 | 复查条件 |
|---|---|---|---|---|
| `config/upstream.yaml` | 我们加的自定义 key 与上游新增 key 同段 | 我们的 key 一律 `x_` 前缀，永远 ours | 2026-01-01 | 上游支持 extensions 段后删 patch |
| `web/router.js` | 我们插了一条路由 | 只在上游的 `// EXTENSION POINT` 之后插入，冲突时 ours+手工补 | 2026-01-02 | 上游提供 plugin 注册即撤 |

## 分级处理

1. **永远是我们对** → `git diff` 里该块能整块保留；如果每次都冲突，考虑把这块挪到我们自己的目录，上游文件只留一行调用
2. **永远是上游对** → 我们的 patch 该消失了（上游已实现/已改接口），删 patch 而不是解冲突
3. **两边都要** → 写清合并后的语义，并加一条测试固定它
4. **上游重构导致我们整体失效** → 这不是冲突，是「大版本迁移」，按 README 的迁移流程走

## rebase 中常用的几个动作

```bash
git status                     # 谁在冲突
git diff                       # 冲突块
git checkout --ours  <file>    # rebase 里 ours = 上游基线 + 已重放的提交
git checkout --theirs <file>   # rebase 里 theirs = 正在重放的那个 patch
git rebase --skip              # 这个 patch 上游已实现，直接丢掉（记得删台账）
git rebase --abort             # 全撤
git rerere status              # 已记住的冲突解（rerere 开着，重复冲突会自动填）
```
