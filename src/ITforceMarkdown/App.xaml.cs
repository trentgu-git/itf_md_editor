using System.Windows;
using ITforceMarkdown.Services;
using ITforceMarkdown.Stores;

namespace ITforceMarkdown;

/// <summary>
/// Application 入口。挂全局 WorkspaceStore 单例 + ThemeManager 初始化。
/// </summary>
public partial class App : Application
{
    /// <summary>全局 store, 类似 Mac 版 @StateObject + EnvironmentObject。</summary>
    public static WorkspaceStore Store { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.Initialize(Store.Appearance);

        // store.Appearance 变化时 → 立即换主题字典
        Store.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceStore.Appearance))
                ThemeManager.Apply(Store.Appearance);
        };

        base.OnStartup(e);
    }
}
