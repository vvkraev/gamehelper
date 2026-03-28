using System.Windows;

namespace GameHelper;

public partial class StepConfirmDialog : Window
{
    public StepConfirmDialog(string message)
    {
        InitializeComponent();
        MessageBlock.Text = message;
        Loaded += (_, _) => ContinueBtn.Focus();
    }

    private void Continue_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
