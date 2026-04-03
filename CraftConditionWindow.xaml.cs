using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameHelper.Services;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGroupBox = System.Windows.Controls.GroupBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using MessageBox = System.Windows.MessageBox;
using Clipboard = System.Windows.Clipboard;

namespace GameHelper;

public partial class CraftConditionWindow : Window
{
    private readonly CraftConditionPlan _plan;
    private readonly List<AffixLibraryEntry> _entries;

    public CraftConditionWindow(CraftConditionPlan plan, List<AffixLibraryEntry> entries)
    {
        InitializeComponent();
        _plan = plan;
        _entries = entries;
        LoadItemClasses();
        if (!string.IsNullOrEmpty(_plan.ExpectedItemClass) &&
            ItemClassCombo.Items.Cast<string>().Contains(_plan.ExpectedItemClass, StringComparer.Ordinal))
            ItemClassCombo.SelectedItem = _plan.ExpectedItemClass;
        else if (ItemClassCombo.Items.Count > 0)
            ItemClassCombo.SelectedIndex = 0;
        RefreshOrAlternativesUi();
    }

    private void LoadItemClasses()
    {
        var classes = _entries
            .SelectMany(e => e.ItemClasses)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x)
            .ToList();
        ItemClassCombo.ItemsSource = classes;
    }

    private string? SelectedItemClass => ItemClassCombo.SelectedItem as string;

    private void ItemClassCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedItemClass is { } ic)
            _plan.ExpectedItemClass = ic;
        RefreshOrAlternativesUi();
    }

    private void AddOrAlternative_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedItemClass))
        {
            MessageBox.Show("Сначала выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var first = FirstPickForClass(SelectedItemClass);
        var clause = new CraftClause { Kind = CraftClauseKind.Single, Single = first };
        _plan.OrAlternatives.Add(new CraftAndGroup { Clauses = new List<CraftClause> { clause } });
        RefreshOrAlternativesUi();
    }

    private CraftSingleAffixData FirstPickForClass(string itemClass)
    {
        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(itemClass, _entries);
        if (types.Count == 0)
            return new CraftSingleAffixData { MinRoll = 0 };
        var t = types[0];
        var stats = CraftAffixCascadeHelper.GetStatTemplatesForClassAndType(itemClass, t, _entries);
        var st = stats.Count > 0 ? stats[0] : "";
        var fp = new CraftSingleAffixData
        {
            AffixType = t,
            AffixName = "",
            AffixTier = 0,
            StatTemplate = st,
            MinRoll = 0,
        };
        fp.EnsureMinRollsSize(CraftAffixCascadeHelper.GetRollSlotCountForStat(itemClass, t, st, _entries));
        return fp;
    }

    private static void SyncWholeModifierFromLibraryEntry(CraftWholeModifierAffixData data, AffixLibraryEntry e)
    {
        data.AffixType = e.AffixType;
        data.AffixName = e.AffixName;
        data.AffixTier = e.AffixTier;
        data.Lines.Clear();
        for (var i = 0; i < e.AffixStats.Count; i++)
        {
            var st = e.AffixStats[i];
            var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(e, i);
            var line = new CraftWholeModifierLine { StatTemplate = st };
            line.EnsureMinRollsSize(slots);
            data.Lines.Add(line);
        }
    }

    private CraftClause? TryCreateFirstWholeModifierClause(string itemClass)
    {
        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(itemClass, _entries);
        foreach (var t in types)
        {
            var list = CraftAffixCascadeHelper.GetMultiStatEntriesForClassAndType(itemClass, t, _entries);
            if (list.Count == 0)
                continue;
            var whole = new CraftWholeModifierAffixData();
            SyncWholeModifierFromLibraryEntry(whole, list[0]);
            return new CraftClause { Kind = CraftClauseKind.WholeModifier, Whole = whole };
        }

        return null;
    }

    private void RefreshOrAlternativesUi()
    {
        OrAlternativesHost.Children.Clear();
        for (var i = 0; i < _plan.OrAlternatives.Count; i++)
            OrAlternativesHost.Children.Add(BuildOrGroupUi(_plan.OrAlternatives[i], i));
    }

    private UIElement BuildOrGroupUi(CraftAndGroup group, int orIndex)
    {
        var gb = new WpfGroupBox
        {
            Header = $"Вариант {orIndex + 1} (внутри — И)",
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(8),
        };
        var sp = new WpfStackPanel();
        for (var j = 0; j < group.Clauses.Count; j++)
            sp.Children.Add(BuildClauseUi(group, group.Clauses[j], orIndex, j));

        var btns = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var addSingle = new WpfButton { Content = "Одиночный аффикс", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
        addSingle.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(SelectedItemClass))
            {
                MessageBox.Show("Выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            group.Clauses.Add(new CraftClause
            {
                Kind = CraftClauseKind.Single,
                Single = FirstPickForClass(SelectedItemClass!),
            });
            RefreshOrAlternativesUi();
        };
        var addSum = new WpfButton { Content = "Сумма значений", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
        addSum.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(SelectedItemClass))
            {
                MessageBox.Show("Выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fp = FirstPickForClass(SelectedItemClass!);
            group.Clauses.Add(new CraftClause
            {
                Kind = CraftClauseKind.Sum,
                Sum = new CraftSumAffixData
                {
                    MinSum = 0,
                    Parts = new List<CraftAffixRef>
                    {
                        new CraftAffixRef
                        {
                            AffixType = fp.AffixType,
                            AffixName = fp.AffixName,
                            AffixTier = fp.AffixTier,
                            StatTemplate = fp.StatTemplate,
                        },
                    },
                },
            });
            RefreshOrAlternativesUi();
        };
        var addCount = new WpfButton { Content = "Набор (COUNT)", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
        addCount.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(SelectedItemClass))
            {
                MessageBox.Show("Выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var fp = FirstPickForClass(SelectedItemClass!);
            group.Clauses.Add(new CraftClause
            {
                Kind = CraftClauseKind.Count,
                Count = new CraftCountAffixData
                {
                    MinMatchCount = 1,
                    Members = new List<CraftSingleAffixData> { fp },
                },
            });
            RefreshOrAlternativesUi();
        };
        var addWhole = new WpfButton
        {
            Content = "Целый модификатор",
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(8, 4, 8, 4),
        };
        addWhole.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(SelectedItemClass))
            {
                MessageBox.Show("Выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cl = TryCreateFirstWholeModifierClause(SelectedItemClass!);
            if (cl is null)
            {
                MessageBox.Show(
                    "Для этого класса в библиотеке нет модификаторов с двумя и более строками стата (см. affix_library.json).",
                    "Условие",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            group.Clauses.Add(cl);
            RefreshOrAlternativesUi();
        };
        var removeOr = new WpfButton { Content = "Удалить вариант", Padding = new Thickness(8, 4, 8, 4) };
        removeOr.Click += (_, _) =>
        {
            _plan.OrAlternatives.RemoveAt(orIndex);
            RefreshOrAlternativesUi();
        };
        btns.Children.Add(addSingle);
        btns.Children.Add(addSum);
        btns.Children.Add(addCount);
        btns.Children.Add(addWhole);
        btns.Children.Add(removeOr);
        sp.Children.Add(btns);
        gb.Content = sp;
        return gb;
    }

    private UIElement BuildClauseUi(CraftAndGroup group, CraftClause clause, int orIndex, int clauseIndex)
    {
        var panel = new WpfStackPanel { Margin = new Thickness(0, 0, 0, 8) };

        if (clause.Kind == CraftClauseKind.Single && clause.Single is { } s)
        {
            var header = new WpfTextBlock { Text = "Одиночный аффикс (≥ порог)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
            panel.Children.Add(header);
            panel.Children.Add(BuildTypeStatRowForSingle(s));
            panel.Children.Add(BuildMinRollRowForSingle(s, clause, group));
        }
        else if (clause.Kind == CraftClauseKind.WholeModifier && clause.Whole is { } whole)
        {
            panel.Children.Add(BuildWholeModifierClauseUi(whole, clause, group));
        }
        else if (clause.Kind == CraftClauseKind.Sum && clause.Sum is { } sum)
        {
            panel.Children.Add(new WpfTextBlock { Text = "Сумма значений аффиксов", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            foreach (var part in sum.Parts)
            {
                var line = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                line.Children.Add(BuildTypeStatRowForRef(part));
                var rem = new WpfButton { Content = "×", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2), Tag = part };
                rem.Click += (_, _) =>
                {
                    sum.Parts.Remove(part);
                    if (sum.Parts.Count == 0)
                        group.Clauses.Remove(clause);
                    RefreshOrAlternativesUi();
                };
                line.Children.Add(rem);
                panel.Children.Add(line);
            }

            var addPart = new WpfButton { Content = "Добавить аффикс в сумму", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
            addPart.Click += (_, _) =>
            {
                var fp = FirstPickForClass(SelectedItemClass ?? _plan.ExpectedItemClass);
                sum.Parts.Add(new CraftAffixRef
                {
                    AffixType = fp.AffixType,
                    AffixName = fp.AffixName,
                    AffixTier = fp.AffixTier,
                    StatTemplate = fp.StatTemplate,
                });
                RefreshOrAlternativesUi();
            };
            panel.Children.Add(addPart);

            var sumRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
            sumRow.Children.Add(new WpfTextBlock { Text = "Мин. сумма:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            var tbSum = new WpfTextBox { Width = 120, Text = FormatNum(sum.MinSum) };
            tbSum.LostFocus += (_, _) =>
            {
                if (double.TryParse(tbSum.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    sum.MinSum = v;
            };
            sumRow.Children.Add(tbSum);
            panel.Children.Add(sumRow);

            var removeClause = new WpfButton { Content = "Удалить условие «Сумма»", Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2) };
            removeClause.Click += (_, _) =>
            {
                group.Clauses.Remove(clause);
                RefreshOrAlternativesUi();
            };
            panel.Children.Add(removeClause);
        }
        else if (clause.Kind == CraftClauseKind.Count && clause.Count is { } cnt)
        {
            panel.Children.Add(new WpfTextBlock
            {
                Text = "Набор аффиксов (COUNT)",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
            });
            var countRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            countRow.Children.Add(new WpfTextBlock
            {
                Text = "Мин. выполненных строк из набора:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            var tbCount = new WpfTextBox { Width = 56, Text = cnt.MinMatchCount.ToString(CultureInfo.InvariantCulture) };
            tbCount.LostFocus += (_, _) =>
            {
                if (int.TryParse(tbCount.Text.Trim(), out var v))
                {
                    var n = Math.Max(1, cnt.Members.Count);
                    cnt.MinMatchCount = Math.Clamp(v, 1, n);
                    tbCount.Text = cnt.MinMatchCount.ToString(CultureInfo.InvariantCulture);
                }
            };
            countRow.Children.Add(tbCount);
            countRow.Children.Add(new WpfTextBlock
            {
                Text = $"из {cnt.Members.Count}",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(8, 0, 0, 0),
            });
            panel.Children.Add(countRow);

            foreach (var mem in cnt.Members.ToList())
            {
                var inner = new WpfStackPanel();
                inner.Children.Add(BuildTypeStatRowForSingle(mem));
                inner.Children.Add(BuildMinRollRowCore(mem, () =>
                {
                    cnt.Members.Remove(mem);
                    if (cnt.Members.Count == 0)
                        group.Clauses.Remove(clause);
                    else if (cnt.MinMatchCount > cnt.Members.Count)
                        cnt.MinMatchCount = cnt.Members.Count;
                    RefreshOrAlternativesUi();
                }, "Убрать из набора"));

                var memPanel = new WpfStackPanel { Margin = new Thickness(0, 0, 0, 8) };
                memPanel.Children.Add(new WpfBorder
                {
                    BorderBrush = System.Windows.Media.Brushes.Gainsboro,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    Child = inner,
                });
                panel.Children.Add(memPanel);
            }

            var addMem = new WpfButton
            {
                Content = "Добавить строку в набор",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6),
            };
            addMem.Click += (_, _) =>
            {
                cnt.Members.Add(FirstPickForClass(SelectedItemClass ?? _plan.ExpectedItemClass));
                RefreshOrAlternativesUi();
            };
            panel.Children.Add(addMem);

            var removeCountClause = new WpfButton
            {
                Content = "Удалить условие «Набор (COUNT)»",
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Padding = new Thickness(8, 2, 8, 2),
            };
            removeCountClause.Click += (_, _) =>
            {
                group.Clauses.Remove(clause);
                RefreshOrAlternativesUi();
            };
            panel.Children.Add(removeCountClause);
        }

        return new WpfBorder { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1), Padding = new Thickness(8), Child = panel };
    }

    private sealed record AffixComboItem(string Label, AffixLibraryEntry Entry);

    private UIElement BuildWholeModifierClauseUi(
        CraftWholeModifierAffixData data,
        CraftClause clause,
        CraftAndGroup group)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        var root = new WpfStackPanel();

        root.Children.Add(new WpfTextBlock
        {
            Text = "Целый модификатор: один аффикс с выбранным именем и тиром; все строки стата должны удовлетворять порогам (И).",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        });

        var row1 = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var cbType = new WpfComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 8, 0), IsEditable = false };
        var cbEntry = new WpfComboBox { MinWidth = 420, IsEditable = false };

        var gate = new[] { false };

        bool SameModifier(AffixLibraryEntry e) =>
            string.Equals(e.AffixName, data.AffixName, StringComparison.Ordinal) &&
            e.AffixTier == data.AffixTier &&
            string.Equals(e.AffixType, data.AffixType, StringComparison.Ordinal);

        void RefillEntries(string affixType)
        {
            var list = CraftAffixCascadeHelper.GetMultiStatEntriesForClassAndType(ic, affixType, _entries);
            var items = list
                .Select(e => new AffixComboItem(
                    $"{e.AffixName} (T{e.AffixTier}, {e.AffixStats.Count} стр.)",
                    e))
                .ToList();
            cbEntry.ItemsSource = items;
            if (items.Count == 0)
                return;

            var pick = items.FirstOrDefault(x => SameModifier(x.Entry));
            gate[0] = true;
            cbEntry.SelectedItem = pick ?? items[0];
            gate[0] = false;
        }

        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(ic, _entries)
            .Where(t => CraftAffixCascadeHelper.GetMultiStatEntriesForClassAndType(ic, t, _entries).Count > 0)
            .ToList();
        if (!string.IsNullOrEmpty(data.AffixType) && !types.Contains(data.AffixType))
            types.Insert(0, data.AffixType);

        cbType.ItemsSource = types;
        if (types.Count > 0)
        {
            if (!string.IsNullOrEmpty(data.AffixType) && types.Contains(data.AffixType))
                cbType.SelectedItem = data.AffixType;
            else
                cbType.SelectedIndex = 0;
        }

        gate[0] = true;
        if (cbType.SelectedItem is string initT)
            RefillEntries(initT);
        gate[0] = false;

        cbType.SelectionChanged += (_, _) =>
        {
            if (gate[0] || cbType.SelectedItem is not string nt)
                return;
            RefillEntries(nt);
        };

        cbEntry.SelectionChanged += (_, _) =>
        {
            if (gate[0] || cbEntry.SelectedItem is not AffixComboItem aci)
                return;
            if (SameModifier(aci.Entry))
                return;
            SyncWholeModifierFromLibraryEntry(data, aci.Entry);
            RefreshOrAlternativesUi();
        };

        cbEntry.DisplayMemberPath = nameof(AffixComboItem.Label);

        row1.Children.Add(new WpfTextBlock { Text = "Тип:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row1.Children.Add(cbType);
        row1.Children.Add(new WpfTextBlock { Text = "Модификатор:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        row1.Children.Add(cbEntry);
        root.Children.Add(row1);

        var entry = AffixCraftPatternBuilder.FindEntryByNameAndTier(_entries, ic, data.AffixType, data.AffixName, data.AffixTier);
        foreach (var line in data.Lines)
        {
            var wrap = new WpfStackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var stDisp = line.StatTemplate.Length > 120 ? line.StatTemplate[..117] + "…" : line.StatTemplate;
            wrap.Children.Add(new WpfTextBlock
            {
                Text = stDisp,
                TextWrapping = TextWrapping.Wrap,
                Foreground = System.Windows.Media.Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 4),
            });

            if (entry is not null)
            {
                var si = AffixCraftPatternBuilder.GetStatIndex(entry, line.StatTemplate);
                if (si >= 0)
                    wrap.Children.Add(BuildMinRollRowForWholeLine(line, entry, si));
                else
                {
                    wrap.Children.Add(new WpfTextBlock
                    {
                        Text = "Строка не совпадает с записью библиотеки.",
                        Foreground = System.Windows.Media.Brushes.OrangeRed,
                    });
                }
            }
            else
            {
                wrap.Children.Add(new WpfTextBlock
                {
                    Text = "Запись библиотеки не найдена для этого имени и тира.",
                    Foreground = System.Windows.Media.Brushes.OrangeRed,
                });
            }

            root.Children.Add(wrap);
        }

        var removeClause = new WpfButton
        {
            Content = "Удалить условие «Целый модификатор»",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Padding = new Thickness(8, 2, 8, 2),
        };
        removeClause.Click += (_, _) =>
        {
            group.Clauses.Remove(clause);
            RefreshOrAlternativesUi();
        };
        root.Children.Add(removeClause);

        return root;
    }

    private UIElement BuildMinRollRowForWholeLine(CraftWholeModifierLine line, AffixLibraryEntry entry, int statIndex)
    {
        var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(entry, statIndex);
        line.EnsureMinRollsSize(slots);
        var rangeStr = statIndex >= 0 && statIndex < entry.AffixRanges.Count ? entry.AffixRanges[statIndex] : null;
        var labels = CraftAffixCascadeHelper.GetRollSlotLabels(rangeStr, slots).ToList();

        var row = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        for (var i = 0; i < slots; i++)
        {
            var labelText = slots > 1 && i < labels.Count && labels[i].Length > 0
                ? $"Мин. {labels[i]}:"
                : slots > 1
                    ? $"Мин. {i + 1}:"
                    : "Мин. перекат:";
            row.Children.Add(new WpfTextBlock
            {
                Text = labelText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(i == 0 ? 0 : 12, 0, 6, 0),
            });
            var idx = i;
            var tb = new WpfTextBox { Width = 72, Text = FormatNum(line.MinRolls[idx]) };
            tb.LostFocus += (_, _) =>
            {
                if (!double.TryParse(tb.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    return;
                while (line.MinRolls.Count <= idx)
                    line.MinRolls.Add(0);
                line.MinRolls[idx] = v;
                if (line.MinRolls.Count == 1)
                    line.MinRoll = v;
            };
            row.Children.Add(tb);
        }

        return row;
    }

    private UIElement BuildTypeStatRowForSingle(CraftSingleAffixData data)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        var row = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var cbType = new WpfComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 8, 0), IsEditable = false };
        var cbStat = new WpfComboBox { MinWidth = 360, IsEditable = false };

        void RefillStatForType(string affixType)
        {
            var stats = CraftAffixCascadeHelper.GetStatTemplatesForClassAndType(ic, affixType, _entries);
            cbStat.ItemsSource = stats;
            if (stats.Count == 0)
                return;
            if (!string.IsNullOrEmpty(data.StatTemplate) &&
                stats.Any(s => string.Equals(s, data.StatTemplate, StringComparison.Ordinal)))
                cbStat.SelectedItem = data.StatTemplate;
            else
                cbStat.SelectedIndex = 0;
        }

        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(ic, _entries);
        cbType.ItemsSource = types;
        if (types.Count > 0)
        {
            if (!string.IsNullOrEmpty(data.AffixType) && types.Contains(data.AffixType))
                cbType.SelectedItem = data.AffixType;
            else
                cbType.SelectedIndex = 0;
        }

        var effType = cbType.SelectedItem as string ?? data.AffixType ?? "";
        RefillStatForType(effType);
        if (cbType.SelectedItem is string tSync)
        {
            data.AffixType = tSync;
            data.AffixName = "";
            data.AffixTier = 0;
        }

        if (cbStat.SelectedItem is string sSync)
            data.StatTemplate = sSync;

        cbType.SelectionChanged += (_, _) =>
        {
            if (cbType.SelectedItem is not string nt)
                return;
            data.AffixType = nt;
            data.AffixName = "";
            data.AffixTier = 0;
            RefillStatForType(nt);
            if (cbStat.SelectedItem is string st)
                data.StatTemplate = st;
            RefreshOrAlternativesUi();
        };
        cbStat.SelectionChanged += (_, _) =>
        {
            if (cbStat.SelectedItem is string st)
            {
                data.StatTemplate = st;
                data.AffixName = "";
                data.AffixTier = 0;
            }

            RefreshOrAlternativesUi();
        };

        row.Children.Add(new WpfTextBlock { Text = "Тип:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(cbType);
        row.Children.Add(new WpfTextBlock { Text = "Стата:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        row.Children.Add(cbStat);
        return row;
    }

    private UIElement BuildMinRollRowForSingle(CraftSingleAffixData data, CraftClause clause, CraftAndGroup group) =>
        BuildMinRollRowCore(
            data,
            () =>
            {
                group.Clauses.Remove(clause);
                RefreshOrAlternativesUi();
            },
            "Удалить строку");

    /// <summary>Поля минимального переката; <paramref name="removeAction"/> — кнопка справа (если null — без кнопки).</summary>
    private UIElement BuildMinRollRowCore(CraftSingleAffixData data, Action? removeAction, string removeButtonLabel)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        var slots = CraftAffixCascadeHelper.GetRollSlotCountForStat(ic, data.AffixType, data.StatTemplate, _entries);
        data.EnsureMinRollsSize(slots);
        var rangeStr = CraftAffixCascadeHelper.GetTierRangeStringForStat(ic, data.AffixType, data.StatTemplate, _entries);
        var labels = CraftAffixCascadeHelper.GetRollSlotLabels(rangeStr, slots).ToList();

        var row = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < slots; i++)
        {
            var labelText = slots > 1 && i < labels.Count && labels[i].Length > 0
                ? $"Мин. {labels[i]}:"
                : slots > 1
                    ? $"Мин. {i + 1}:"
                    : "Мин. перекат:";
            row.Children.Add(new WpfTextBlock
            {
                Text = labelText,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(i == 0 ? 0 : 12, 0, 6, 0),
            });
            var idx = i;
            var tb = new WpfTextBox { Width = 72, Text = FormatNum(data.MinRolls[idx]) };
            tb.LostFocus += (_, _) =>
            {
                if (!double.TryParse(tb.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    return;
                while (data.MinRolls.Count <= idx)
                    data.MinRolls.Add(0);
                data.MinRolls[idx] = v;
                if (data.MinRolls.Count == 1)
                    data.MinRoll = v;
            };
            row.Children.Add(tb);
        }

        if (removeAction is not null)
        {
            var remove = new WpfButton { Content = removeButtonLabel, Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            remove.Click += (_, _) => removeAction();
            row.Children.Add(remove);
        }

        return row;
    }

    private UIElement BuildTypeStatRowForRef(CraftAffixRef part)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        var row = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
        var cbType = new WpfComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 8, 0), IsEditable = false };
        var cbStat = new WpfComboBox { MinWidth = 360, IsEditable = false };

        void RefillStatForType(string affixType)
        {
            var stats = CraftAffixCascadeHelper.GetStatTemplatesForClassAndType(ic, affixType, _entries);
            cbStat.ItemsSource = stats;
            if (stats.Count == 0)
                return;
            if (!string.IsNullOrEmpty(part.StatTemplate) &&
                stats.Any(s => string.Equals(s, part.StatTemplate, StringComparison.Ordinal)))
                cbStat.SelectedItem = part.StatTemplate;
            else
                cbStat.SelectedIndex = 0;
        }

        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(ic, _entries);
        cbType.ItemsSource = types;
        if (types.Count > 0)
        {
            if (!string.IsNullOrEmpty(part.AffixType) && types.Contains(part.AffixType))
                cbType.SelectedItem = part.AffixType;
            else
                cbType.SelectedIndex = 0;
        }

        var effType = cbType.SelectedItem as string ?? part.AffixType ?? "";
        RefillStatForType(effType);
        if (cbType.SelectedItem is string tSync)
        {
            part.AffixType = tSync;
            part.AffixName = "";
            part.AffixTier = 0;
        }

        if (cbStat.SelectedItem is string sSync)
            part.StatTemplate = sSync;

        cbType.SelectionChanged += (_, _) =>
        {
            if (cbType.SelectedItem is not string nt)
                return;
            part.AffixType = nt;
            part.AffixName = "";
            part.AffixTier = 0;
            RefillStatForType(nt);
            if (cbStat.SelectedItem is string st)
                part.StatTemplate = st;
            RefreshOrAlternativesUi();
        };
        cbStat.SelectionChanged += (_, _) =>
        {
            if (cbStat.SelectedItem is string st)
            {
                part.StatTemplate = st;
                part.AffixName = "";
                part.AffixTier = 0;
            }

            RefreshOrAlternativesUi();
        };

        row.Children.Add(new WpfTextBlock { Text = "Тип:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(cbType);
        row.Children.Add(new WpfTextBlock { Text = "Стата:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        row.Children.Add(cbStat);
        return row;
    }

    private static string FormatNum(double v) =>
        v == Math.Truncate(v) ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);

    private void CopyForReport_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(SettingsStore.FormatCraftConditionForClipboard(_plan));
            SessionLogger.Info("Условие остановки крафта скопировано в буфер обмена (кратко + JSON).");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Не удалось скопировать в буфер обмена: " + ex.Message,
                "Копирование",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_plan.ExpectedItemClass))
        {
            MessageBox.Show("Выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!CraftConditionEvaluator.TryValidate(_plan, out var err))
        {
            MessageBox.Show(err, "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
