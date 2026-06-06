using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameHelper.Services;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfGroupBox = System.Windows.Controls.GroupBox;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfSlider = System.Windows.Controls.Slider;
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

    /// <summary>Выбранный подтип планшета; null = без фильтра.</summary>
    private string? _tabletSubClass;

    /// <summary>
    /// Записи для каскадных дропдаунов: если выбран подтип планшета — фильтруются по подклассу
    /// (специфичные + универсальные); иначе — все записи.
    /// </summary>
    private IReadOnlyList<AffixLibraryEntry> EffectiveEntries =>
        CraftAffixCascadeHelper.FilterBySubClass(_entries, _tabletSubClass);

    public CraftConditionWindow(CraftConditionPlan plan, List<AffixLibraryEntry> entries)
    {
        InitializeComponent();
        WindowGeometryStore.Attach(this, "CraftConditionWindow");
        _plan = plan;
        _entries = entries;
        CraftConditionPlanNormalizer.NormalizeInPlace(_plan, _entries);
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

        _tabletSubClass = null;
        RefreshSubClassRow();
        RefreshOrAlternativesUi();
    }

    private void RefreshSubClassRow()
    {
        var ic = SelectedItemClass;
        var subClasses = ic is not null
            ? CraftAffixCascadeHelper.GetSubClassesForItemClass(ic, _entries)
            : new List<string>();

        if (subClasses.Count == 0)
        {
            SubClassRow.Visibility = System.Windows.Visibility.Collapsed;
            return;
        }

        SubClassRow.Visibility = System.Windows.Visibility.Visible;
        var items = new List<string?> { null };
        items.AddRange(subClasses);
        TabletSubClassCombo.ItemsSource = items.Select(s => s ?? "Все типы").ToList();
        TabletSubClassCombo.SelectedIndex = 0;
    }

    private void TabletSubClassCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var idx = TabletSubClassCombo.SelectedIndex;
        if (idx < 0)
        {
            _tabletSubClass = null;
            return;
        }

        var subClasses = SelectedItemClass is not null
            ? CraftAffixCascadeHelper.GetSubClassesForItemClass(SelectedItemClass, _entries)
            : new List<string>();

        // idx 0 = "Все типы" (null), idx 1+ = subClasses[idx-1]
        _tabletSubClass = idx == 0 ? null : (idx - 1 < subClasses.Count ? subClasses[idx - 1] : null);
        RefreshOrAlternativesUi();
    }

    private void AddOrAlternative_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedItemClass))
        {
            MessageBox.Show(this,"Сначала выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var first = FirstPickForClass(SelectedItemClass);
        var clause = new CraftClause { Kind = CraftClauseKind.Single, Single = first };
        _plan.OrAlternatives.Add(new CraftAndGroup { Clauses = new List<CraftClause> { clause } });
        RefreshOrAlternativesUi();
    }

    private CraftSingleAffixData FirstPickForClass(string itemClass)
    {
        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(itemClass, EffectiveEntries);
        if (types.Count == 0)
            return new CraftSingleAffixData { MinRoll = 0, AffixTier = 1 };
        var t = types[0];
        var stats = CraftAffixCascadeHelper.GetStatTemplatesForClassAndType(itemClass, t, EffectiveEntries);
        var st = stats.Count > 0 ? stats[0] : "";
        var tiers = CraftAffixCascadeHelper.GetDistinctTiersForClassTypeStat(itemClass, t, st, EffectiveEntries);
        var tier = tiers.Count > 0 ? tiers[0] : 1;
        var names = CraftAffixCascadeHelper.GetAffixNamesForClassTypeStatTier(itemClass, t, st, tier, EffectiveEntries);
        var fp = new CraftSingleAffixData
        {
            AffixType = t,
            AffixName = "",
            StatTemplate = st,
            AffixTier = tier,
            MinRoll = 0,
        };
        if (names.Count > 0)
            fp.SelectedAffixNames.Add(names[0]);
        var slots = CraftAffixCascadeHelper.GetRollSlotCountForStat(itemClass, t, st, EffectiveEntries);
        fp.EnsureMinRollsSize(slots);
        var (lo, hi) = CraftAffixCascadeHelper.GetUnionRollBoundsForSingleStat(
            itemClass, t, st, fp.SelectedAffixNames, fp.AffixTier, EffectiveEntries);
        fp.MinRoll = ResolveSliderThreshold(lo, hi, fp.MinRoll);
        for (var i = 0; i < fp.MinRolls.Count; i++)
            fp.MinRolls[i] = fp.MinRoll;
        return fp;
    }

    private static void SyncWholeModifierFromLibraryEntry(CraftWholeModifierAffixData data, AffixLibraryEntry e)
    {
        data.AffixType = e.AffixType;
        data.AffixName = e.AffixName;
        data.AffixTier = e.AffixTier;
        data.SelectedAffixNames.Clear();
        data.SelectedAffixNames.Add(e.AffixName);
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
        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(itemClass, EffectiveEntries);
        foreach (var t in types)
        {
            var list = CraftAffixCascadeHelper.GetMultiStatEntriesForClassAndType(itemClass, t, EffectiveEntries);
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
                MessageBox.Show(this,"Выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(this,"Выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(this,"Выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show(this,"Выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cl = TryCreateFirstWholeModifierClause(SelectedItemClass!);
            if (cl is null)
            {
                MessageBox.Show(this,
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
            var header = new WpfTextBlock
            {
                Text = "Одиночный аффикс: тир, имя(имена) из библиотеки, минимальный перекат (ползунок).",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap,
            };
            panel.Children.Add(header);
            panel.Children.Add(BuildTypeStatRowForSingle(s));
            panel.Children.Add(BuildTierAffixNamesSliderForSingle(
                s,
                () =>
                {
                    group.Clauses.Remove(clause);
                    RefreshOrAlternativesUi();
                },
                "Удалить строку"));
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
                inner.Children.Add(BuildTierAffixNamesSliderForSingle(mem, () =>
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
            Text =
                "Целый модификатор: шаблон из библиотеки, затем один или несколько имён (ИЛИ) с тем же набором строк стата; по каждой строке — ползунок минимума.",
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
            var list = CraftAffixCascadeHelper.GetMultiStatEntriesForClassAndType(ic, affixType, EffectiveEntries);
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

        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(ic, EffectiveEntries)
            .Where(t => CraftAffixCascadeHelper.GetMultiStatEntriesForClassAndType(ic, t, EffectiveEntries).Count > 0)
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

        var gateW = new[] { false };
        root.Children.Add(new WpfTextBlock
        {
            Text = "Имена (ИЛИ), тот же набор строк стата и тир:",
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = System.Windows.Media.Brushes.DimGray,
        });
        var lbWhole = new WpfListBox
        {
            SelectionMode = System.Windows.Controls.SelectionMode.Extended,
            MaxHeight = 120,
            Margin = new Thickness(0, 0, 0, 8),
        };

        void RefillWholeNameList()
        {
            var refE = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
                EffectiveEntries,
                ic,
                data.AffixType,
                data.AffixName,
                data.AffixTier);
            if (refE is null)
            {
                lbWhole.ItemsSource = null;
                return;
            }

            var compat = CraftAffixCascadeHelper.GetMultiStatEntriesForClassAndType(ic, data.AffixType, EffectiveEntries)
                .Where(e => e.AffixTier == data.AffixTier && CraftAffixCascadeHelper.EntriesShareSameAffixStats(refE, e))
                .ToList();
            lbWhole.ItemsSource = compat;
            lbWhole.DisplayMemberPath = nameof(AffixLibraryEntry.AffixName);
            gateW[0] = true;
            lbWhole.SelectedItems.Clear();
            foreach (var nm in data.SelectedAffixNames)
            {
                var m = compat.FirstOrDefault(x => string.Equals(x.AffixName, nm, StringComparison.Ordinal));
                if (m != null)
                    lbWhole.SelectedItems.Add(m);
            }

            if (lbWhole.SelectedItems.Count == 0 && compat.Count > 0)
            {
                lbWhole.SelectedItems.Add(compat[0]);
                data.SelectedAffixNames.Clear();
                data.SelectedAffixNames.Add(compat[0].AffixName);
                data.AffixName = compat[0].AffixName;
            }

            gateW[0] = false;
        }

        lbWhole.SelectionChanged += (_, _) =>
        {
            if (gateW[0])
                return;
            data.SelectedAffixNames.Clear();
            foreach (var o in lbWhole.SelectedItems)
            {
                if (o is AffixLibraryEntry we)
                    data.SelectedAffixNames.Add(we.AffixName);
            }

            data.AffixName = data.SelectedAffixNames.Count > 0 ? data.SelectedAffixNames[0] : "";
            RefreshOrAlternativesUi();
        };

        root.Children.Add(lbWhole);
        RefillWholeNameList();

        var entry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
            _entries,
            ic,
            data.AffixType,
            data.AffixName,
            data.AffixTier);
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
                if (si < 0)
                    si = CraftAffixCascadeHelper.FindStatIndexInEntry(entry, line.StatTemplate);
                if (si >= 0)
                    wrap.Children.Add(BuildWholeLineSliderRow(data, line, entry, si));
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

    private UIElement BuildWholeLineSliderRow(
        CraftWholeModifierAffixData whole,
        CraftWholeModifierLine line,
        AffixLibraryEntry refEntry,
        int statIndex)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(refEntry, statIndex);
        line.EnsureMinRollsSize(slots);
        var names = whole.SelectedAffixNames.Count > 0
            ? whole.SelectedAffixNames
            : whole.EffectiveWholeAffixNames().ToList();
        var (lo, hi) = CraftAffixCascadeHelper.GetUnionRollBoundsForWholeLine(
            ic,
            whole.AffixType,
            refEntry,
            statIndex,
            names,
            whole.AffixTier,
            _entries);
        var fixedRoll = hi <= lo;

        var col = new WpfStackPanel { Margin = new Thickness(0, 2, 0, 0) };
        var lblBounds = new WpfTextBlock
        {
            Text = fixedRoll
                ? $"Перекат по библиотеке (фикс.): {FormatNum(lo)}"
                : $"Диапазон по выбранным именам: [{FormatNum(lo)} … {FormatNum(hi)}]",
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var slider = new WpfSlider { MinWidth = 280, Margin = new Thickness(0, 0, 0, 4) };
        var lblVal = new WpfTextBlock();
        var span = hi - lo;
        var allInt = lo == Math.Truncate(lo) && hi == Math.Truncate(hi);
        slider.Minimum = lo;
        slider.Maximum = hi;
        slider.TickFrequency = allInt && span <= 500 ? 1 : Math.Max(span / 20.0, 0.01);
        slider.IsSnapToTickEnabled = allInt && span <= 500;
        slider.IsEnabled = !fixedRoll;
        var current = line.MinRolls.Count > 0 ? line.MinRolls[0] : line.MinRoll;
        var v = ResolveSliderThreshold(lo, hi, current);
        slider.Value = v;
        for (var i = 0; i < line.MinRolls.Count; i++)
            line.MinRolls[i] = v;
        line.MinRoll = v;
        lblVal.Text = fixedRoll
            ? $"Порог остановки: {FormatNum(v)} (совпадает с перекатом в библиотеке)"
            : $"Минимум переката: {FormatNum(v)}";

        slider.ValueChanged += (_, _) =>
        {
            if (fixedRoll)
                return;
            var nv = slider.Value;
            line.EnsureMinRollsSize(slots);
            for (var i = 0; i < line.MinRolls.Count; i++)
                line.MinRolls[i] = nv;
            line.MinRoll = nv;
            lblVal.Text = $"Минимум переката: {FormatNum(nv)}";
        };

        col.Children.Add(lblBounds);
        col.Children.Add(slider);
        col.Children.Add(lblVal);
        return col;
    }

    private UIElement BuildTypeStatRowForSingle(CraftSingleAffixData data)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        var row = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var cbType = new WpfComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 8, 0), IsEditable = false };
        var cbStat = new WpfComboBox { MinWidth = 360, IsEditable = false };

        void RefillStatForType(string affixType)
        {
            var stats = CraftAffixCascadeHelper.GetStatTemplatesForClassAndType(ic, affixType, EffectiveEntries);
            cbStat.ItemsSource = stats;
            if (stats.Count == 0)
                return;
            if (!string.IsNullOrEmpty(data.StatTemplate) &&
                stats.Any(s => string.Equals(s, data.StatTemplate, StringComparison.Ordinal)))
                cbStat.SelectedItem = data.StatTemplate;
            else
                cbStat.SelectedIndex = 0;
        }

        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(ic, EffectiveEntries);
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
            data.SelectedAffixNames.Clear();
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
            data.SelectedAffixNames.Clear();
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
                data.SelectedAffixNames.Clear();
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

    private UIElement BuildTierAffixNamesSliderForSingle(CraftSingleAffixData data, Action? removeAction, string removeButtonLabel)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        var col = new WpfStackPanel { Margin = new Thickness(0, 6, 0, 0) };

        var tierRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        tierRow.Children.Add(new WpfTextBlock { Text = "Тир:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        var cbTier = new WpfComboBox { MinWidth = 72, VerticalAlignment = VerticalAlignment.Center };
        var gate = new[] { false };

        void RefillTiers()
        {
            gate[0] = true;
            var tiers = CraftAffixCascadeHelper.GetDistinctTiersForClassTypeStat(ic, data.AffixType, data.StatTemplate, EffectiveEntries);
            cbTier.ItemsSource = tiers;
            if (tiers.Count == 0)
            {
                data.AffixTier = Math.Max(1, data.AffixTier);
                cbTier.SelectedItem = null;
            }
            else if (tiers.Contains(data.AffixTier))
                cbTier.SelectedItem = data.AffixTier;
            else
            {
                data.AffixTier = tiers[0];
                cbTier.SelectedItem = tiers[0];
            }

            gate[0] = false;
        }

        RefillTiers();

        tierRow.Children.Add(cbTier);
        col.Children.Add(tierRow);

        col.Children.Add(new WpfTextBlock
        {
            Text = "Имя аффикса (можно несколько, Ctrl/Shift):",
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = System.Windows.Media.Brushes.DimGray,
        });
        var lb = new WpfListBox
        {
            MinHeight = 72,
            MaxHeight = 160,
            SelectionMode = System.Windows.Controls.SelectionMode.Extended,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var sliderRow = new WpfStackPanel { Orientation = WpfOrientation.Vertical, Margin = new Thickness(0, 0, 0, 4) };
        var lblBounds = new WpfTextBlock { Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) };
        var slider = new WpfSlider { MinWidth = 280, Margin = new Thickness(0, 0, 0, 4) };
        var lblVal = new WpfTextBlock { Margin = new Thickness(0, 0, 0, 0) };
        sliderRow.Children.Add(lblBounds);
        sliderRow.Children.Add(slider);
        sliderRow.Children.Add(lblVal);

        void ApplySliderValueToData(double v)
        {
            var slots = CraftAffixCascadeHelper.GetRollSlotCountForStat(ic, data.AffixType, data.StatTemplate, EffectiveEntries);
            data.EnsureMinRollsSize(slots);
            data.MinRoll = v;
            for (var i = 0; i < data.MinRolls.Count; i++)
                data.MinRolls[i] = v;
        }

        void RefillNamesAndSlider()
        {
            gate[0] = true;
            var names = CraftAffixCascadeHelper.GetAffixNamesForClassTypeStatTier(
                ic, data.AffixType, data.StatTemplate, data.AffixTier, EffectiveEntries);
            lb.ItemsSource = names;
            lb.SelectedItems.Clear();
            foreach (var n in data.SelectedAffixNames)
            {
                if (names.Contains(n, StringComparer.Ordinal))
                    lb.SelectedItems.Add(n);
            }

            if (lb.SelectedItems.Count == 0 && names.Count > 0)
            {
                lb.SelectedItems.Add(names[0]);
                data.SelectedAffixNames.Clear();
                data.SelectedAffixNames.Add(names[0]);
                data.AffixName = names[0];
            }

            var (lo, hi) = CraftAffixCascadeHelper.GetUnionRollBoundsForSingleStat(
                ic, data.AffixType, data.StatTemplate, data.SelectedAffixNames, data.AffixTier, EffectiveEntries);
            var fixedRoll = hi <= lo;
            slider.Minimum = lo;
            slider.Maximum = hi;
            var span = hi - lo;
            var allInt = lo == Math.Truncate(lo) && hi == Math.Truncate(hi);
            slider.TickFrequency = allInt && span <= 500 ? 1 : Math.Max(span / 20.0, 0.01);
            slider.IsSnapToTickEnabled = allInt && span <= 500;
            slider.IsEnabled = !fixedRoll;
            var v = ResolveSliderThreshold(lo, hi, data.MinRoll);
            slider.Value = v;
            ApplySliderValueToData(v);
            lblBounds.Text = fixedRoll
                ? $"Перекат по библиотеке (фикс.): {FormatNum(lo)}"
                : $"Диапазон порога по библиотеке: [{FormatNum(lo)} … {FormatNum(hi)}]";
            lblVal.Text = fixedRoll
                ? $"Порог остановки: {FormatNum(v)} (совпадает с перекатом в библиотеке)"
                : $"Минимум переката: {FormatNum(v)}";
            gate[0] = false;
        }

        lb.SelectionChanged += (_, _) =>
        {
            if (gate[0])
                return;
            data.SelectedAffixNames.Clear();
            foreach (var o in lb.SelectedItems)
            {
                if (o is string sn && !string.IsNullOrWhiteSpace(sn))
                    data.SelectedAffixNames.Add(sn);
            }

            data.AffixName = data.SelectedAffixNames.Count > 0 ? data.SelectedAffixNames[0] : "";
            RefillNamesAndSlider();
        };

        slider.ValueChanged += (_, _) =>
        {
            if (gate[0] || !slider.IsEnabled)
                return;
            var v = slider.Value;
            ApplySliderValueToData(v);
            lblVal.Text = $"Минимум переката: {FormatNum(v)}";
        };

        cbTier.SelectionChanged += (_, _) =>
        {
            if (gate[0] || cbTier.SelectedItem is not int ti)
                return;
            data.AffixTier = ti;
            data.SelectedAffixNames.Clear();
            data.AffixName = "";
            RefillNamesAndSlider();
        };

        col.Children.Add(lb);
        col.Children.Add(sliderRow);

        RefillNamesAndSlider();

        if (removeAction is not null)
        {
            var remove = new WpfButton
            {
                Content = removeButtonLabel,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(8, 2, 8, 2),
            };
            remove.Click += (_, _) => removeAction();
            col.Children.Add(remove);
        }

        return col;
    }

    private UIElement BuildTypeStatRowForRef(CraftAffixRef part)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        var row = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
        var cbType = new WpfComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 8, 0), IsEditable = false };
        var cbStat = new WpfComboBox { MinWidth = 360, IsEditable = false };

        void RefillStatForType(string affixType)
        {
            var stats = CraftAffixCascadeHelper.GetStatTemplatesForClassAndType(ic, affixType, EffectiveEntries);
            cbStat.ItemsSource = stats;
            if (stats.Count == 0)
                return;
            if (!string.IsNullOrEmpty(part.StatTemplate) &&
                stats.Any(s => string.Equals(s, part.StatTemplate, StringComparison.Ordinal)))
                cbStat.SelectedItem = part.StatTemplate;
            else
                cbStat.SelectedIndex = 0;
        }

        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(ic, EffectiveEntries);
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

    /// <summary>
    /// Фикс. перекат (lo==hi) → lo. Диапазон → lo по умолчанию; сохранённый порог, если он в [lo..hi].
    /// </summary>
    private static double ResolveSliderThreshold(double lo, double hi, double current)
    {
        if (hi <= lo)
            return lo;
        return current >= lo && current <= hi ? current : lo;
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
            MessageBox.Show(this,
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
            MessageBox.Show(this,"Выберите класс предмета.", "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!CraftConditionEvaluator.TryValidate(_plan, out var err))
        {
            MessageBox.Show(this,err, "Условие", MessageBoxButton.OK, MessageBoxImage.Warning);
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
