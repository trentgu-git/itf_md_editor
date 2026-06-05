using System.ComponentModel;
using System.Windows.Controls;
using ITforceMarkdown.Stores;

namespace ITforceMarkdown.Views;

public partial class SourceEditor : UserControl
{
    private WorkspaceStore Store => App.Store;
    private bool _suppressEvent;

    /// <summary>基准字号 — DocumentZoomLevel 应用倍数到这个值上.</summary>
    private const double BaseFontSize = 12.5;

    public SourceEditor()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Sync();
            ApplyZoom();
            Store.PropertyChanged += OnStoreChanged;
        };
        Unloaded += (_, _) =>
        {
            Store.PropertyChanged -= OnStoreChanged;
        };
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkspaceStore.SelectedFile) ||
            e.PropertyName == nameof(WorkspaceStore.SourceDraft))
        {
            Dispatcher.BeginInvoke(new System.Action(Sync));
        }
        else if (e.PropertyName == nameof(WorkspaceStore.DocumentZoomLevel))
        {
            Dispatcher.BeginInvoke(new System.Action(ApplyZoom));
        }
    }

    private void Sync()
    {
        if (Editor.Text != Store.SourceDraft)
        {
            _suppressEvent = true;
            try { Editor.Text = Store.SourceDraft; }
            finally { _suppressEvent = false; }
        }
    }

    private void ApplyZoom()
    {
        Editor.FontSize = BaseFontSize * Store.DocumentZoomLevel;
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvent) return;
        Store.SourceDraft = Editor.Text;
    }
}
