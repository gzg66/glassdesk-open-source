# 上游同步记录（append-only）

每次 `fork-sync.sh --apply` 会自动追加一条草稿，**人工补齐「确认/结论」再提交**。
格式固定，方便半年后 grep「上次同步是哪天、崩在哪」。

模板：

```
## YYYY-MM-DD 同步
- 上游: `旧sha .. 新sha`（N 提交）
- patch 重放: N 个（range-diff 已比对）
- 冲突面: K 个重叠文件 — 怎么解、为什么这么解
- 测试: PASS / FAIL(修了什么)
- 人工确认: 谁看的、结论、有没有要删的 patch

## YYYY-MM-DD 大版本迁移 v1.x → v3.x
- 旧分支归档: main-v1
- 保留 patch: 007 012 019
- 丢弃 patch: 003（上游已实现） 011（改成钩子，不再需要）
- 新生 patch: 020 021
- 验证: 全量测试 + 手工回归用例（见 docs/fork/CONFLICTS.md 附）
```

<!-- 以下由 fork-sync.sh 自动追加 -->
