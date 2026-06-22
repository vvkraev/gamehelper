using System.Windows;
using GameHelper.Services;
using WpfButton    = System.Windows.Controls.Button;
using WpfCheckBox  = System.Windows.Controls.CheckBox;
using WpfSeparator = System.Windows.Controls.Separator;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox   = System.Windows.Controls.TextBox;

namespace GameHelper;

/// <summary>
/// Диалог настроек одной вкладки торговца: количество повторений, интервал и шаги переоценки.
/// </summary>
public sealed class RepricingTabSettingsWindow : Window
{
    private readonly WpfTextBox _countBox;
    private readonly WpfTextBox _intervalBox;
    private readonly List<WpfCheckBox> _stepCheckBoxes = new();

    public RepricingTabSettingsWindow(RepricingTabConfig tab, int globalRepeatCount, int globalIntervalMinutes)
    {
        Title = $"Настройки вкладки: {tab.Name}";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new WpfStackPanel { Margin = new Thickness(16) };

        // ── Количество повторений ────────────────────────────────────────────
        root.Children.Add(new WpfTextBlock
        {
            Text = $"Количество повторений (пусто = по умолчанию: {globalRepeatCount})",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _countBox = new WpfTextBox
        {
            Text = tab.RepeatCount.HasValue ? tab.RepeatCount.Value.ToString() : "",
            Margin = new Thickness(0, 0, 0, 12)
        };
        root.Children.Add(_countBox);

        // ── Интервал ─────────────────────────────────────────────────────────
        root.Children.Add(new WpfTextBlock
        {
            Text = $"Интервал между повторениями, мин (пусто = по умолчанию: {globalIntervalMinutes})",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        _intervalBox = new WpfTextBox
        {
            Text = tab.RepeatIntervalMinutes.HasValue ? tab.RepeatIntervalMinutes.Value.ToString() : "",
            Margin = new Thickness(0, 0, 0, 16)
        };
        root.Children.Add(_intervalBox);

        root.Children.Add(new WpfSeparator { Margin = new Thickness(0, 0, 0, 12) });

        // ── Шаги переоценки ──────────────────────────────────────────────────
        root.Children.Add(new WpfTextBlock
        {
            Text = "Шаги снижения цены:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        root.Children.Add(new WpfTextBlock
        {
            Text = "Если шаг отключён — при достижении этого диапазона переоценка предмета прекращается.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var effectiveSteps = tab.PriceSteps is { Count: > 0 }
            ? (IReadOnlyList<RepricingPriceStep>)tab.PriceSteps
            : RepricingService.DefaultPriceSteps;

        for (var i = 0; i < RepricingService.DefaultPriceSteps.Length; i++)
        {
            var def = RepricingService.DefaultPriceSteps[i];
            var enabled = i < effectiveSteps.Count ? effectiveSteps[i].Enabled : def.Enabled;
            var sign = def.StrictlyGreater ? ">" : "≥";
            var label = $"{sign} {def.FromPrice}   шаг −{def.Step}";

            var cb = new WpfCheckBox
            {
                Content = label,
                IsChecked = enabled,
                Margin = new Thickness(0, 3, 0, 3)
            };
            _stepCheckBoxes.Add(cb);
            root.Children.Add(cb);
        }

        root.Children.Add(new WpfSeparator { Margin = new Thickness(0, 12, 0, 12) });

        // ── Кнопки ───────────────────────────────────────────────────────────
        var btnPanel = new WpfStackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        var okBtn = new WpfButton
        {
            Content = "OK",
            IsDefault = true,
            Padding = new Thickness(20, 6, 20, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        okBtn.Click += (_, _) => { DialogResult = true; };

        var cancelBtn = new WpfButton
        {
            Content = "Отмена",
            IsCancel = true,
            Padding = new Thickness(12, 6, 12, 6)
        };

        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        root.Children.Add(btnPanel);

        Content = root;
    }

    /// <summary>Применить введённые значения к конфигу вкладки.</summary>
    public void ApplyTo(RepricingTabConfig tab)
    {
        tab.RepeatCount = int.TryParse(_countBox.Text.Trim(), out var cnt) && cnt > 0 ? cnt : null;
        tab.RepeatIntervalMinutes = int.TryParse(_intervalBox.Text.Trim(), out var itv) && itv > 0 ? itv : null;

        var steps = new List<RepricingPriceStep>();
        for (var i = 0; i < RepricingService.DefaultPriceSteps.Length; i++)
        {
            var def = RepricingService.DefaultPriceSteps[i];
            steps.Add(new RepricingPriceStep
            {
                FromPrice = def.FromPrice,
                Step = def.Step,
                StrictlyGreater = def.StrictlyGreater,
                Enabled = _stepCheckBoxes[i].IsChecked == true
            });
        }

        // Если все шаги включены (= дефолт) — не сохраняем кастомный список
        tab.PriceSteps = steps.All(s => s.Enabled) ? null : steps;
    }
}
