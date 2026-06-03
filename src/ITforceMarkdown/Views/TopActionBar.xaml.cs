using System.Windows.Controls;
using Microsoft.Win32;
using ITforceMarkdown.Stores;

namespace ITforceMarkdown.Views;

public partial class TopActionBar : UserControl
{
    private WorkspaceStore Store => App.Store;

    public TopActionBar()
    {
        InitializeComponent();
    }

    private void OpenFolder_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Choose a workspace folder",
        };
        if (dlg.ShowDialog() == true)
            Store.AddWorkspace(dlg.FolderName);
    }

    private void OpenFile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open Markdown file",
            Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() == true)
            Store.OpenExternalFile(dlg.FileName);
    }

    private void NewFile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Store.CreateDocument();
    }
}
