using System.Windows;
using GameHelper.Services;
using MessageBox = System.Windows.MessageBox;

namespace GameHelper;

public partial class RecipeNameDialog : Window
{
    public string RecipeName { get; private set; } = "";

    public RecipeNameDialog(string? initialName = null)
    {
        InitializeComponent();
        NameBox.Text = initialName?.Trim() ?? "";
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var raw = NameBox.Text ?? "";
        var safe = RecipeStore.SanitizeRecipeName(raw);
        if (safe.Length == 0)
        {
            MessageBox.Show("Введите имя рецепта.", "Рецепт", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RecipeName = raw.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

