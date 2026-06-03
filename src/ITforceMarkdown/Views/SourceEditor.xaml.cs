using System.ComponentModel;
using System.Windows.Controls;
using ITforceMarkdown.Stores;

namespace ITforceMarkdown.Views;

public partial class SourceEditor : UserControl
{
    private WorkspaceStore Store => App.Store;
    private bool _suppressEvent;

    public SourceEditor()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Sync();
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

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvent) return;
        Store.SourceDraft = Editor.Text;
    }
}
