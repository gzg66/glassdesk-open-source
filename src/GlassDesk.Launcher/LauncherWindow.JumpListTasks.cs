using System.Windows.Controls;
using GlassDesk.AppServices;

namespace GlassDesk.Launcher;

/// <summary>
/// 启动台右键菜单里的「任务」分组：程序自己注册的动作（Edge 的「新建窗口」这类）。
/// </summary>
/// <remarks>
/// 插在打开菜单时才做，不在建菜单时做：<c>BuildTileMenu</c> 是创建磁贴时调用的，
/// 磁贴和它的 <see cref="ContextMenu"/> 会被长期复用，建菜单时还拿不到（也可能早已过期）。
/// 打开时插入、关闭时移除，保证用户看到的是当下的任务。
///
/// <para>
/// 任务项一律插在最前面并紧跟一条分隔线，菜单的其它条目顺序不动。
/// </para>
/// </remarks>
public partial class LauncherWindow
{
    /// <summary>
    /// 给一个右键菜单挂上任务分组。没有任务时什么也不加，也不留空分隔线。
    /// </summary>
    private void AttachJumpListTasks(ContextMenu menu, string entryId)
    {
        var inserted = new List<object>();

        menu.Opened += (_, _) =>
        {
            if (inserted.Count > 0) return;

            var entry = _services?.Library.Find(entryId);
            if (entry is null) return;

            var tasks = JumpListTaskCatalog.TasksFor(entry);
            if (tasks.Count == 0) return;

            for (var index = 0; index < tasks.Count; index++)
            {
                var task = tasks[index];
                var item = new MenuItem { Header = task.Title };

                // 执行语义是「总是新开」，不是「已运行就提到前面」——后者是应用图标本身的语义。
                item.Click += (_, _) =>
                {
                    var result = JumpListTaskCatalog.Launch(task);

                    if (!result.Started)
                    {
                        ShowNotice(result.Message ?? "任务未能启动。");
                        return;
                    }

                    CloseWithTransition();
                };

                menu.Items.Insert(index, item);
                inserted.Add(item);
            }

            var separator = new Separator();
            menu.Items.Insert(tasks.Count, separator);
            inserted.Add(separator);
        };

        menu.Closed += (_, _) =>
        {
            foreach (var element in inserted) menu.Items.Remove(element);
            inserted.Clear();
        };
    }
}
