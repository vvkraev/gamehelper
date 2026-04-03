using System.Windows;

namespace GameHelper;

public partial class ItemGridDimensionsDialog : Window
{
    public int GridColumns { get; private set; } = 1;
    public int GridRows { get; private set; } = 1;

    public ItemGridDimensionsDialog()
    {
        InitializeComponent();
        for (var i = 1; i <= 12; i++)
            ComboX.Items.Add(i);
        for (var i = 1; i <= 12; i++)
            ComboY.Items.Add(i);
        ComboX.SelectedIndex = 0;
        ComboY.SelectedIndex = 0;
        SyncProps();
    }

    private void Combo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) =>
        SyncProps();

    private void SyncProps()
    {
        GridColumns = ComboX.SelectedItem is int xc ? xc : 1;
        GridRows = ComboY.SelectedItem is int yr ? yr : 1;
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        SyncProps();
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
