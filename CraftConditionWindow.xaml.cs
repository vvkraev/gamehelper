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
    private readonly Services.AffixStatsData? _stats;

    /// <summary>Выбранный подтип планшета; null = без фильтра.</summary>
    private string? _tabletSubClass;

    /// <summary>
    /// Записи для каскадных дропдаунов: если выбран подтип планшета — фильтруются по подклассу
    /// (специфичные + универсальные); иначе — все записи.
    /// </summary>
    private IReadOnlyList<AffixLibraryEntry> EffectiveEntries =>
        CraftAffixCascadeHelper.FilterBySubClass(_entries, _tabletSubClass);

    public CraftConditionWindow(CraftConditionPlan plan, List<AffixLibraryEntry> entries,
        Services.AffixStatsData? stats = null)
    {
        InitializeComponent();
        WindowGeometryStore.Attach(this, "CraftConditionWindow");
        _plan = plan;
        _entries = entries;
        _stats = stats;
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

    private static void SyncSingleFromEntry(CraftSingleAffixData data, AffixLibraryEntry e)
    {
        data.AffixType = e.AffixType;
        data.AffixName = e.AffixName;
        data.AffixTier = e.AffixTier;
        // Rebuild Lines from library entry stats, preserving existing thresholds where stat matches
        var oldLines = data.Lines.ToList();
        data.Lines.Clear();
        for (var i = 0; i < e.AffixStats.Count; i++)
        {
            var st = e.AffixStats[i];
            var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(e, i);
            var existing = oldLines.FirstOrDefault(l =>
                string.Equals(l.StatTemplate, st, StringComparison.Ordinal) ||
                CraftAffixCascadeHelper.StatMatchesNormalizedTemplate(st, l.StatTemplate));
            var line = new CraftWholeModifierLine
            {
                StatTemplate = st,
                MinRoll = existing?.MinRoll ?? 0,
                MinRolls = existing?.MinRolls.Count > 0 ? existing.MinRolls.ToList() : new List<double>(),
            };
            line.EnsureMinRollsSize(slots);
            data.Lines.Add(line);
        }
        // Sync legacy field
        data.StatTemplate = data.Lines.Count > 0 ? data.Lines[0].StatTemplate : "";
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
        UpdateCombinedChanceLabel();
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

            var fp = FirstWholePickForClass(SelectedItemClass!);
            group.Clauses.Add(new CraftClause
            {
                Kind = CraftClauseKind.Count,
                Count = new CraftCountAffixData
                {
                    MinMatchCount = 1,
                    Members = new List<CraftWholeModifierAffixData> { fp },
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
                Text = "Одиночный аффикс: семейство статов, тир и имена из библиотеки, минимальный перекат (ползунок).",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap,
            };
            panel.Children.Add(header);
            panel.Children.Add(BuildSingleAffixCoreUi(
                s,
                () => { group.Clauses.Remove(clause); RefreshOrAlternativesUi(); },
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
                var memPanel = new WpfStackPanel { Margin = new Thickness(0, 0, 0, 8) };
                memPanel.Children.Add(new WpfBorder
                {
                    BorderBrush = System.Windows.Media.Brushes.Gainsboro,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    Child = BuildCountMemberUi(mem, () =>
                    {
                        cnt.Members.Remove(mem);
                        if (cnt.Members.Count == 0)
                            group.Clauses.Remove(clause);
                        else if (cnt.MinMatchCount > cnt.Members.Count)
                            cnt.MinMatchCount = cnt.Members.Count;
                        RefreshOrAlternativesUi();
                    }),
                });
                panel.Children.Add(memPanel);
            }

            var addMem = new WpfButton
            {
                Content = "Добавить аффикс в набор",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 6),
            };
            addMem.Click += (_, _) =>
            {
                cnt.Members.Add(FirstWholePickForClass(SelectedItemClass ?? _plan.ExpectedItemClass));
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
    private sealed record TierComboItem(string Label, int Tier, AffixLibraryEntry RefEntry, List<string> AllNames);

    private static string NormalizeStatForDisplay(string libStat) =>
        System.Text.RegularExpressions.Regex.Replace(libStat.Trim(), @"\(\d[^)]*\)", "#");

    private static string FormatFamilyLabel(AffixLibraryEntry e) =>
        e.AffixStats.Count == 0 ? "(нет стат)" : string.Join(" / ", e.AffixStats.Select(NormalizeStatForDisplay));

    // Normalised family equality: strips numeric ranges before comparing so T1/T2/... of same family match.
    private static bool SameStatFamily(AffixLibraryEntry a, AffixLibraryEntry b)
    {
        if (a.AffixStats.Count != b.AffixStats.Count) return false;
        for (var i = 0; i < a.AffixStats.Count; i++)
        {
            if (!string.Equals(
                    NormalizeStatForDisplay(a.AffixStats[i]),
                    NormalizeStatForDisplay(b.AffixStats[i]),
                    StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private List<AffixComboItem> GetFamilyItems(string ic, string affixType, bool multiStatOnly)
    {
        var result = new List<AffixComboItem>();
        var seen = new List<AffixLibraryEntry>();
        var entries = EffectiveEntries
            .Where(e => e.ItemClasses.Any(c => string.Equals(c, ic, StringComparison.Ordinal)) &&
                        string.Equals(e.AffixType, affixType, StringComparison.Ordinal) &&
                        (!multiStatOnly || e.AffixStats.Count >= 2))
            .OrderBy(e => NormalizeStatForDisplay(e.AffixStats.FirstOrDefault() ?? ""))
            .ThenBy(e => e.AffixStats.Count)
            .ToList();
        foreach (var e in entries)
        {
            if (seen.Any(s => SameStatFamily(s, e)))
                continue;
            seen.Add(e);
            result.Add(new AffixComboItem(FormatFamilyLabel(e), e));
        }
        return result;
    }

    private List<TierComboItem> GetTierItemsForFamily(string ic, string affixType, AffixLibraryEntry familyRef)
    {
        return EffectiveEntries
            .Where(e => e.ItemClasses.Any(c => string.Equals(c, ic, StringComparison.Ordinal)) &&
                        string.Equals(e.AffixType, affixType, StringComparison.Ordinal) &&
                        SameStatFamily(familyRef, e))
            .GroupBy(e => e.AffixTier)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var names = g.Select(e => e.AffixName).OrderBy(n => n).ToList();
                return new TierComboItem($"T{g.Key} — {string.Join(", ", names)}", g.Key, g.First(), names);
            })
            .ToList();
    }

    // Shared core builder: Type → Family (stat-set) → Tier → Names (ИЛИ) → Sliders.
    private UIElement BuildWholeAffixCoreUi(CraftWholeModifierAffixData data, bool multiStatOnly)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        var root = new WpfStackPanel();
        var gate = new[] { false };
        var gateN = new[] { false };

        var cbType = new WpfComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 8, 0), IsEditable = false };
        var cbFamily = new WpfComboBox { MinWidth = 400, IsEditable = false };
        cbFamily.DisplayMemberPath = nameof(AffixComboItem.Label);
        var cbTier = new WpfComboBox { MinWidth = 300, IsEditable = false };
        cbTier.DisplayMemberPath = nameof(TierComboItem.Label);
        var lbNames = new WpfListBox
        {
            SelectionMode = System.Windows.Controls.SelectionMode.Extended,
            MaxHeight = 80,
            Margin = new Thickness(0, 0, 0, 6),
        };
        lbNames.DisplayMemberPath = nameof(AffixLibraryEntry.AffixName);
        var slidersPanel = new WpfStackPanel { Margin = new Thickness(0, 2, 0, 0) };

        void RebuildSliders()
        {
            slidersPanel.Children.Clear();
            var refEntry = (cbTier.SelectedItem as TierComboItem)?.RefEntry;
            if (refEntry is null) return;
            foreach (var line in data.Lines)
            {
                var si = CraftAffixCascadeHelper.FindStatIndexInEntry(refEntry, line.StatTemplate);
                if (si < 0) si = AffixCraftPatternBuilder.GetStatIndex(refEntry, line.StatTemplate);
                var wrap = new WpfStackPanel { Margin = new Thickness(0, 0, 0, 6) };
                wrap.Children.Add(new WpfTextBlock
                {
                    Text = NormalizeStatForDisplay(line.StatTemplate),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.DimGray,
                    Margin = new Thickness(0, 0, 0, 2),
                });
                wrap.Children.Add(si >= 0
                    ? BuildWholeLineSliderRow(data, line, refEntry, si)
                    : new WpfTextBlock { Text = "Строка не найдена в записи библиотеки.", Foreground = System.Windows.Media.Brushes.OrangeRed });
                slidersPanel.Children.Add(wrap);
            }
        }

        void RefillNames()
        {
            if (cbTier.SelectedItem is not TierComboItem ti) { lbNames.ItemsSource = null; return; }
            var compat = EffectiveEntries
                .Where(e => e.ItemClasses.Any(c => string.Equals(c, ic, StringComparison.Ordinal)) &&
                            string.Equals(e.AffixType, data.AffixType, StringComparison.Ordinal) &&
                            e.AffixTier == ti.Tier &&
                            SameStatFamily(ti.RefEntry, e))
                .OrderBy(e => e.AffixName)
                .ToList();
            gateN[0] = true;
            lbNames.ItemsSource = compat;
            lbNames.SelectedItems.Clear();
            foreach (var nm in data.SelectedAffixNames)
            {
                var m = compat.FirstOrDefault(x => string.Equals(x.AffixName, nm, StringComparison.Ordinal));
                if (m != null) lbNames.SelectedItems.Add(m);
            }
            if (lbNames.SelectedItems.Count == 0 && compat.Count > 0)
            {
                lbNames.SelectedItems.Add(compat[0]);
                data.SelectedAffixNames.Clear();
                data.SelectedAffixNames.Add(compat[0].AffixName);
                data.AffixName = compat[0].AffixName;
            }
            gateN[0] = false;
        }

        void RefillTiers(AffixLibraryEntry? familyRef)
        {
            if (familyRef is null) { cbTier.ItemsSource = null; return; }
            var tiers = GetTierItemsForFamily(ic, data.AffixType, familyRef);
            cbTier.ItemsSource = tiers;
            gate[0] = true;
            var pick = data.AffixTier > 0 ? tiers.FirstOrDefault(t => t.Tier == data.AffixTier) : null;
            cbTier.SelectedItem = pick ?? tiers.FirstOrDefault();
            gate[0] = false;
        }

        void RefillFamilies(string affixType)
        {
            var families = GetFamilyItems(ic, affixType, multiStatOnly);
            cbFamily.ItemsSource = families;
            if (families.Count == 0) { cbTier.ItemsSource = null; lbNames.ItemsSource = null; return; }

            AffixComboItem? pick = null;
            if (!string.IsNullOrEmpty(data.AffixName) && data.AffixTier > 0)
            {
                var cur = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
                    EffectiveEntries, ic, affixType, data.AffixName, data.AffixTier);
                if (cur != null)
                    pick = families.FirstOrDefault(f => SameStatFamily(f.Entry, cur));
            }
            gate[0] = true;
            cbFamily.SelectedItem = pick ?? families[0];
            gate[0] = false;

            // Sync Lines from best available entry at previously preferred tier for selected family
            var fi = (cbFamily.SelectedItem as AffixComboItem)!;
            var bestRef = data.AffixTier > 0
                ? EffectiveEntries.FirstOrDefault(e =>
                    e.ItemClasses.Any(c => string.Equals(c, ic, StringComparison.Ordinal)) &&
                    string.Equals(e.AffixType, affixType, StringComparison.Ordinal) &&
                    e.AffixTier == data.AffixTier &&
                    SameStatFamily(fi.Entry, e))
                : null;
            SyncWholeModifierFromLibraryEntry(data, bestRef ?? fi.Entry);

            RefillTiers(fi.Entry);
            RefillNames();
            RebuildSliders();
        }

        // ── Init ──────────────────────────────────────────────────────────────
        var allTypes = CraftAffixCascadeHelper.GetAffixTypesForItemClass(ic, EffectiveEntries);
        var types = multiStatOnly
            ? allTypes.Where(t => GetFamilyItems(ic, t, true).Count > 0).ToList()
            : allTypes;
        if (!string.IsNullOrEmpty(data.AffixType) && !types.Contains(data.AffixType))
            types = types.Prepend(data.AffixType).ToList();
        cbType.ItemsSource = types;
        if (!string.IsNullOrEmpty(data.AffixType) && types.Contains(data.AffixType))
            cbType.SelectedItem = data.AffixType;
        else if (types.Count > 0)
            cbType.SelectedIndex = 0;
        if (cbType.SelectedItem is string initType)
            data.AffixType = initType;
        RefillFamilies(data.AffixType);

        // ── Events ────────────────────────────────────────────────────────────
        cbType.SelectionChanged += (_, _) =>
        {
            if (gate[0] || cbType.SelectedItem is not string nt) return;
            data.AffixType = nt;
            data.AffixName = "";
            data.SelectedAffixNames.Clear();
            data.AffixTier = 0;
            data.Lines.Clear();
            RefillFamilies(nt);
            RefreshOrAlternativesUi();
        };

        cbFamily.SelectionChanged += (_, _) =>
        {
            if (gate[0] || cbFamily.SelectedItem is not AffixComboItem fi) return;
            var prevTier = data.AffixTier;
            var bestRef = prevTier > 0
                ? EffectiveEntries.FirstOrDefault(e =>
                    e.ItemClasses.Any(c => string.Equals(c, ic, StringComparison.Ordinal)) &&
                    string.Equals(e.AffixType, data.AffixType, StringComparison.Ordinal) &&
                    e.AffixTier == prevTier &&
                    SameStatFamily(fi.Entry, e))
                : null;
            SyncWholeModifierFromLibraryEntry(data, bestRef ?? fi.Entry);
            RefillTiers(fi.Entry);
            RefillNames();
            RebuildSliders();
            RefreshOrAlternativesUi();
        };

        cbTier.SelectionChanged += (_, _) =>
        {
            if (gate[0] || cbTier.SelectedItem is not TierComboItem ti) return;
            // Find entry at new tier for current family (stat set is same, ranges differ)
            var fi = (cbFamily.SelectedItem as AffixComboItem)?.Entry;
            var refE = fi is not null
                ? EffectiveEntries.FirstOrDefault(e =>
                    e.ItemClasses.Any(c => string.Equals(c, ic, StringComparison.Ordinal)) &&
                    string.Equals(e.AffixType, data.AffixType, StringComparison.Ordinal) &&
                    e.AffixTier == ti.Tier &&
                    SameStatFamily(fi, e))
                : null;
            refE ??= ti.RefEntry;
            // Preserve MinRolls — same stat family, only bounds change
            var savedRolls = data.Lines.Select(l => (l.MinRoll, Rolls: l.MinRolls.ToList())).ToList();
            SyncWholeModifierFromLibraryEntry(data, refE);
            for (var i = 0; i < Math.Min(savedRolls.Count, data.Lines.Count); i++)
            {
                data.Lines[i].MinRoll = savedRolls[i].MinRoll;
                data.Lines[i].MinRolls = savedRolls[i].Rolls;
            }
            // Update names to available ones at new tier
            var prevSelected = new HashSet<string>(data.SelectedAffixNames, StringComparer.Ordinal);
            var keep = ti.AllNames.Where(n => prevSelected.Contains(n)).ToList();
            data.SelectedAffixNames.Clear();
            data.SelectedAffixNames.AddRange(keep.Count > 0 ? keep : ti.AllNames.Take(1));
            data.AffixName = data.SelectedAffixNames.Count > 0 ? data.SelectedAffixNames[0] : "";
            data.AffixTier = ti.Tier;
            RefillNames();
            RebuildSliders();
            RefreshOrAlternativesUi();
        };

        lbNames.SelectionChanged += (_, _) =>
        {
            if (gateN[0]) return;
            data.SelectedAffixNames.Clear();
            foreach (var o in lbNames.SelectedItems)
                if (o is AffixLibraryEntry we)
                    data.SelectedAffixNames.Add(we.AffixName);
            data.AffixName = data.SelectedAffixNames.Count > 0 ? data.SelectedAffixNames[0] : "";
            RebuildSliders();
            RefreshOrAlternativesUi();
        };

        // ── Layout ────────────────────────────────────────────────────────────
        var row1 = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(new WpfTextBlock { Text = "Тип:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row1.Children.Add(cbType);
        row1.Children.Add(new WpfTextBlock { Text = "Семейство:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        row1.Children.Add(cbFamily);
        root.Children.Add(row1);

        var row2 = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row2.Children.Add(new WpfTextBlock { Text = "Тир:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row2.Children.Add(cbTier);
        root.Children.Add(row2);

        root.Children.Add(new WpfTextBlock
        {
            Text = "Имена (ИЛИ, Ctrl/Shift для нескольких):",
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = System.Windows.Media.Brushes.DimGray,
        });
        root.Children.Add(lbNames);
        root.Children.Add(slidersPanel);
        return root;
    }

    private UIElement BuildWholeModifierClauseUi(
        CraftWholeModifierAffixData data,
        CraftClause clause,
        CraftAndGroup group)
    {
        var root = new WpfStackPanel();
        root.Children.Add(new WpfTextBlock
        {
            Text = "Целый модификатор: семейство статов, затем тир и имена (ИЛИ); по каждой строке — ползунок минимума.",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(BuildWholeAffixCoreUi(data, multiStatOnly: true));
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

    private CraftWholeModifierAffixData FirstWholePickForClass(string itemClass)
    {
        var types = CraftAffixCascadeHelper.GetAffixTypesForItemClass(itemClass, EffectiveEntries);
        if (types.Count == 0)
            return new CraftWholeModifierAffixData { AffixTier = 1 };
        var t = types[0];
        var families = GetFamilyItems(itemClass, t, multiStatOnly: false);
        if (families.Count == 0)
            return new CraftWholeModifierAffixData { AffixType = t, AffixTier = 1 };
        var whole = new CraftWholeModifierAffixData();
        SyncWholeModifierFromLibraryEntry(whole, families[0].Entry);
        return whole;
    }

    private UIElement BuildCountMemberUi(CraftWholeModifierAffixData data, Action removeAction)
    {
        var root = new WpfStackPanel();
        root.Children.Add(BuildWholeAffixCoreUi(data, multiStatOnly: false));
        var removeBtn = new WpfButton
        {
            Content = "Убрать из набора",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0),
            Padding = new Thickness(8, 2, 8, 2),
        };
        removeBtn.Click += (_, _) => removeAction();
        root.Children.Add(removeBtn);
        return root;
    }

    private UIElement BuildSingleAffixCoreUi(CraftSingleAffixData data, Action? removeAction, string removeButtonLabel)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        data.EnsureLinesFromLegacy();

        var root  = new WpfStackPanel();
        var gate  = new[] { false };
        var gateN = new[] { false };

        var cbType   = new WpfComboBox { MinWidth = 160, Margin = new Thickness(0, 0, 8, 0), IsEditable = false };
        var cbFamily = new WpfComboBox { MinWidth = 380, IsEditable = false };
        cbFamily.DisplayMemberPath = nameof(AffixComboItem.Label);
        var cbTier = new WpfComboBox { MinWidth = 300, IsEditable = false };
        cbTier.DisplayMemberPath = nameof(TierComboItem.Label);
        var lb = new WpfListBox
        {
            MinHeight = 60,
            MaxHeight = 160,
            SelectionMode = System.Windows.Controls.SelectionMode.Extended,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var lblStats    = new WpfTextBlock { Margin = new Thickness(0, 0, 0, 6), FontSize = 11, Foreground = System.Windows.Media.Brushes.CornflowerBlue };
        var slidersPanel = new WpfStackPanel { Margin = new Thickness(0, 2, 0, 0) };

        void UpdateStatsLabel()
        {
            if (_stats == null || string.IsNullOrEmpty(ic) ||
                !_stats.PerClass.TryGetValue(ic, out var cs) || cs.TotalSnapshots == 0)
            {
                lblStats.Text = _stats == null ? "" : "Статистика: нет данных по этому классу";
                return;
            }
            var selected = data.SelectedAffixNames;
            if (selected.Count == 0) { lblStats.Text = ""; return; }
            var total  = cs.TotalSnapshots;
            var firstStat = data.Lines.Count > 0 ? data.Lines[0].StatTemplate : data.StatTemplate;
            var hasTpl = !string.IsNullOrEmpty(firstStat);
            if (selected.Count == 1)
            {
                var c = hasTpl ? cs.GetStatCount(selected[0], firstStat) : (cs.AffixCounts.TryGetValue(selected[0], out var raw) ? raw : 0);
                var pct = (double)c / total * 100;
                var avg = c > 0 ? $"~{total / c} орбов" : "∞";
                lblStats.Text = $"Статистика: {c} / {total} ({pct:F1}%) — {avg}";
            }
            else
            {
                var combined = hasTpl
                    ? selected.Sum(n => cs.GetStatCount(n, firstStat))
                    : selected.Sum(n => cs.AffixCounts.TryGetValue(n, out var c) ? c : 0);
                var pct = (double)combined / total * 100;
                var avg = combined > 0 ? $"~{total / combined} орбов" : "∞";
                lblStats.Text = $"Статистика: {combined} / {total} ({pct:F1}%) — {avg}, {selected.Count} вариантов";
            }
        }

        void RebuildSliders()
        {
            slidersPanel.Children.Clear();
            if (cbTier.SelectedItem is not TierComboItem ti) return;
            var refEntry = EffectiveEntries.FirstOrDefault(e =>
                e.ItemClasses.Any(c => string.Equals(c, ic, StringComparison.Ordinal)) &&
                string.Equals(e.AffixType, data.AffixType, StringComparison.Ordinal) &&
                e.AffixTier == ti.Tier &&
                SameStatFamily(ti.RefEntry, e)) ?? ti.RefEntry;
            foreach (var line in data.Lines)
            {
                var si = CraftAffixCascadeHelper.FindStatIndexInEntry(refEntry, line.StatTemplate);
                if (si < 0) si = AffixCraftPatternBuilder.GetStatIndex(refEntry, line.StatTemplate);
                var wrap = new WpfStackPanel { Margin = new Thickness(0, 0, 0, 6) };
                wrap.Children.Add(new WpfTextBlock
                {
                    Text = NormalizeStatForDisplay(line.StatTemplate),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.DimGray,
                    Margin = new Thickness(0, 0, 0, 2),
                });
                wrap.Children.Add(si >= 0
                    ? BuildSingleLineSliderRow(data, line, refEntry, si)
                    : new WpfTextBlock { Text = "Строка не найдена в записи библиотеки.", Foreground = System.Windows.Media.Brushes.OrangeRed });
                slidersPanel.Children.Add(wrap);
            }
            UpdateStatsLabel();
        }

        void RefillNames()
        {
            if (cbTier.SelectedItem is not TierComboItem ti) { lb.ItemsSource = null; return; }
            var refEntry = EffectiveEntries.FirstOrDefault(e =>
                e.ItemClasses.Any(c => string.Equals(c, ic, StringComparison.Ordinal)) &&
                string.Equals(e.AffixType, data.AffixType, StringComparison.Ordinal) &&
                e.AffixTier == ti.Tier &&
                SameStatFamily(ti.RefEntry, e)) ?? ti.RefEntry;
            SyncSingleFromEntry(data, refEntry);
            var names = EffectiveEntries
                .Where(e => e.ItemClasses.Any(c => string.Equals(c, ic, StringComparison.Ordinal)) &&
                            string.Equals(e.AffixType, data.AffixType, StringComparison.Ordinal) &&
                            e.AffixTier == ti.Tier &&
                            SameStatFamily(ti.RefEntry, e))
                .OrderBy(e => e.AffixName)
                .Select(e => e.AffixName)
                .ToList();
            gateN[0] = true;
            lb.ItemsSource = names;
            lb.SelectedItems.Clear();
            foreach (var n in data.SelectedAffixNames)
                if (names.Contains(n, StringComparer.Ordinal))
                    lb.SelectedItems.Add(n);
            if (lb.SelectedItems.Count == 0 && names.Count > 0)
            {
                lb.SelectedItems.Add(names[0]);
                data.SelectedAffixNames.Clear();
                data.SelectedAffixNames.Add(names[0]);
                data.AffixName = names[0];
            }
            gateN[0] = false;
            RebuildSliders();
        }

        void RefillTiers(AffixLibraryEntry? familyRef)
        {
            if (familyRef is null) { cbTier.ItemsSource = null; return; }
            var tiers = GetTierItemsForFamily(ic, data.AffixType, familyRef);
            cbTier.ItemsSource = tiers;
            gate[0] = true;
            var pick = data.AffixTier > 0 ? tiers.FirstOrDefault(t => t.Tier == data.AffixTier) : null;
            cbTier.SelectedItem = pick ?? tiers.FirstOrDefault();
            if (cbTier.SelectedItem is TierComboItem sel)
                data.AffixTier = sel.Tier;
            gate[0] = false;
        }

        void ApplyFamily(AffixComboItem fi)
        {
            RefillTiers(fi.Entry);
            RefillNames();
        }

        void RefillFamilies(string affixType)
        {
            var families = GetFamilyItems(ic, affixType, multiStatOnly: false);
            cbFamily.ItemsSource = families;
            if (families.Count == 0) { cbTier.ItemsSource = null; lb.ItemsSource = null; return; }

            AffixComboItem? pick = null;
            if (!string.IsNullOrEmpty(data.AffixName) && data.AffixTier > 0)
            {
                var cur = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
                    EffectiveEntries, ic, affixType, data.AffixName, data.AffixTier);
                if (cur != null)
                    pick = families.FirstOrDefault(f => SameStatFamily(f.Entry, cur));
            }
            if (pick is null && !string.IsNullOrEmpty(data.StatTemplate))
            {
                pick = families.FirstOrDefault(f =>
                    f.Entry.AffixStats.Any(s =>
                        string.Equals(s, data.StatTemplate, StringComparison.Ordinal) ||
                        CraftAffixCascadeHelper.StatMatchesNormalizedTemplate(s, data.StatTemplate)));
            }
            gate[0] = true;
            cbFamily.SelectedItem = pick ?? families[0];
            gate[0] = false;
            ApplyFamily((cbFamily.SelectedItem as AffixComboItem)!);
        }

        // ── Init ────────────────────────────────────────────────────────────
        var allTypes = CraftAffixCascadeHelper.GetAffixTypesForItemClass(ic, EffectiveEntries);
        cbType.ItemsSource = allTypes;
        if (!string.IsNullOrEmpty(data.AffixType) && allTypes.Contains(data.AffixType))
            cbType.SelectedItem = data.AffixType;
        else if (allTypes.Count > 0)
            cbType.SelectedIndex = 0;
        if (cbType.SelectedItem is string initType)
            data.AffixType = initType;

        RefillFamilies(data.AffixType);

        // ── Events ──────────────────────────────────────────────────────────
        cbType.SelectionChanged += (_, _) =>
        {
            if (gate[0] || cbType.SelectedItem is not string nt) return;
            data.AffixType = nt;
            data.AffixName = "";
            data.SelectedAffixNames.Clear();
            data.AffixTier = 0;
            data.Lines.Clear();
            data.StatTemplate = "";
            RefillFamilies(nt);
            RefreshOrAlternativesUi();
        };

        cbFamily.SelectionChanged += (_, _) =>
        {
            if (gate[0] || cbFamily.SelectedItem is not AffixComboItem fi) return;
            data.AffixName = "";
            data.SelectedAffixNames.Clear();
            data.AffixTier = 0;
            data.Lines.Clear();
            ApplyFamily(fi);
            RefreshOrAlternativesUi();
        };

        cbTier.SelectionChanged += (_, _) =>
        {
            if (gate[0] || cbTier.SelectedItem is not TierComboItem ti) return;
            data.AffixTier = ti.Tier;
            data.SelectedAffixNames.Clear();
            data.AffixName = "";
            RefillNames();
            RefreshOrAlternativesUi();
        };

        lb.SelectionChanged += (_, _) =>
        {
            if (gateN[0]) return;
            data.SelectedAffixNames.Clear();
            foreach (var o in lb.SelectedItems)
                if (o is string sn && !string.IsNullOrWhiteSpace(sn))
                    data.SelectedAffixNames.Add(sn);
            data.AffixName = data.SelectedAffixNames.Count > 0 ? data.SelectedAffixNames[0] : "";
            RebuildSliders();
            RefreshOrAlternativesUi();
        };

        // ── Layout ──────────────────────────────────────────────────────────
        var row1 = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row1.Children.Add(new WpfTextBlock { Text = "Тип:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row1.Children.Add(cbType);
        row1.Children.Add(new WpfTextBlock { Text = "Семейство:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) });
        row1.Children.Add(cbFamily);
        root.Children.Add(row1);

        var row2 = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row2.Children.Add(new WpfTextBlock { Text = "Тир:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row2.Children.Add(cbTier);
        root.Children.Add(row2);

        root.Children.Add(new WpfTextBlock
        {
            Text = "Имена (ИЛИ, Ctrl/Shift для нескольких):",
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = System.Windows.Media.Brushes.DimGray,
        });
        root.Children.Add(lb);
        root.Children.Add(lblStats);
        root.Children.Add(slidersPanel);

        if (removeAction is not null)
        {
            var removeBtn = new WpfButton
            {
                Content = removeButtonLabel,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(8, 2, 8, 2),
            };
            removeBtn.Click += (_, _) => removeAction();
            root.Children.Add(removeBtn);
        }

        return root;
    }

    private UIElement BuildSingleLineSliderRow(
        CraftSingleAffixData single,
        CraftWholeModifierLine line,
        AffixLibraryEntry refEntry,
        int statIndex)
    {
        var ic = SelectedItemClass ?? _plan.ExpectedItemClass ?? "";
        var slots = CraftAffixCascadeHelper.GetRollSlotCountForEntryStat(refEntry, statIndex);
        line.EnsureMinRollsSize(slots);
        var names = single.SelectedAffixNames.Count > 0
            ? single.SelectedAffixNames
            : single.EffectiveAffixNames().ToList();
        var (lo, hi) = CraftAffixCascadeHelper.GetUnionRollBoundsForWholeLine(
            ic, single.AffixType, refEntry, statIndex, names, single.AffixTier, _entries);
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
        var span   = hi - lo;
        var allInt = lo == Math.Truncate(lo) && hi == Math.Truncate(hi);
        slider.Minimum = lo;
        slider.Maximum = hi;
        slider.TickFrequency      = allInt && span <= 500 ? 1 : Math.Max(span / 20.0, 0.01);
        slider.IsSnapToTickEnabled = allInt && span <= 500;
        slider.IsEnabled           = !fixedRoll;
        var current = line.MinRolls.Count > 0 ? line.MinRolls[0] : line.MinRoll;
        var v = ResolveSliderThreshold(lo, hi, current);
        slider.Value = v;
        for (var i = 0; i < line.MinRolls.Count; i++) line.MinRolls[i] = v;
        line.MinRoll = v;
        lblVal.Text = fixedRoll
            ? $"Порог остановки: {FormatNum(v)} (совпадает с перекатом в библиотеке)"
            : $"Минимум переката: {FormatNum(v)}";

        slider.ValueChanged += (_, _) =>
        {
            if (fixedRoll) return;
            var nv = slider.Value;
            line.EnsureMinRollsSize(slots);
            for (var i = 0; i < line.MinRolls.Count; i++) line.MinRolls[i] = nv;
            line.MinRoll = nv;
            // Sync legacy field to first line
            single.MinRoll = single.Lines.Count > 0 ? single.Lines[0].MinRoll : 0;
            lblVal.Text = $"Минимум переката: {FormatNum(nv)}";
        };

        col.Children.Add(lblBounds);
        col.Children.Add(slider);
        col.Children.Add(lblVal);
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

    // ── Суммарный шанс ───────────────────────────────────────────────────────────────

    private void UpdateCombinedChanceLabel()
    {
        var ic = _plan.ExpectedItemClass;
        if (_stats == null || string.IsNullOrEmpty(ic) || _plan.OrAlternatives.Count == 0)
        {
            CombinedChanceLabel.Text = "";
            return;
        }

        if (!_stats.PerClass.TryGetValue(ic, out var cs) || cs.TotalSnapshots < 10)
        {
            CombinedChanceLabel.Text = cs?.TotalSnapshots > 0
                ? $"Суммарный шанс: мало данных ({cs.TotalSnapshots} предметов)"
                : "Суммарный шанс: нет данных по этому классу";
            return;
        }

        var hasPartialData = false;
        var pNone = 1.0;
        foreach (var alt in _plan.OrAlternatives)
            pNone *= 1.0 - CalcAndGroupProbability(alt, ic, cs, ref hasPartialData);

        var p = Math.Max(0.0, Math.Min(1.0, 1.0 - pNone));
        var pct = p * 100;
        var avgOrbs = p > 0 ? (int)Math.Round(1.0 / p) : -1;
        var avgStr = avgOrbs > 0 ? $" (~{avgOrbs} орбов)" : " (∞)";
        var dataNote = hasPartialData ? " ⚠ нет данных по части аффиксов" : "";

        CombinedChanceLabel.Text = $"Суммарный шанс: ~{pct:F1}%{avgStr}{dataNote}";
    }

    private double CalcAndGroupProbability(CraftAndGroup group, string ic, ClassStats cs, ref bool hasPartialData)
    {
        var p = 1.0;
        foreach (var clause in group.Clauses)
        {
            if (clause.Kind == CraftClauseKind.Single && clause.Single is { } s)
                p *= CalcSingleMemberProbability(s, ic, cs, ref hasPartialData);
            else if (clause.Kind == CraftClauseKind.Count && clause.Count is { } cnt)
                p *= CalcCountClauseProbability(cnt, ic, cs, ref hasPartialData);
            else if (clause.Kind == CraftClauseKind.WholeModifier && clause.Whole is { } w)
                p *= CalcWholeModifierProbability(w, ic, cs, ref hasPartialData);
            // Sum clause: no frequency-based estimate possible, treat as p=1
        }
        return Math.Max(0.0, Math.Min(1.0, p));
    }

    private double CalcSingleMemberProbability(CraftSingleAffixData s, string ic, ClassStats cs, ref bool hasPartialData)
    {
        var names = s.EffectiveAffixNames();
        if (names.Count == 0) return 0.0;

        var hasTpl = !string.IsNullOrEmpty(s.StatTemplate);
        var count = hasTpl
            ? names.Sum(n => cs.GetStatCount(n, s.StatTemplate))
            : names.Sum(n => cs.AffixCounts.TryGetValue(n, out var c) ? c : 0);
        if (count == 0) { hasPartialData = true; return 0.0; }

        var freq = (double)count / cs.TotalSnapshots;
        var (lo, hi) = CraftAffixCascadeHelper.GetUnionRollBoundsForSingleStat(
            ic, s.AffixType, s.StatTemplate, names, s.AffixTier, EffectiveEntries);
        return Math.Max(0.0, Math.Min(1.0, freq * CalcRollFraction(lo, hi, s.MinRoll)));
    }

    private double CalcCountClauseProbability(CraftCountAffixData cnt, string ic, ClassStats cs, ref bool hasPartialData)
    {
        var n = Math.Min(cnt.Members.Count, 20); // safety cap for bitmask
        if (n == 0) return 0.0;

        var probs = new double[n];
        for (var j = 0; j < n; j++)
            probs[j] = CalcWholeModifierProbability(cnt.Members[j], ic, cs, ref hasPartialData);
        var k = cnt.MinMatchCount;
        if (k <= 0) return 1.0;
        if (k > n) return 0.0;

        var pAtLeastK = 0.0;
        for (var mask = 0; mask < (1 << n); mask++)
        {
            if (BitCount(mask) < k) continue;
            var p = 1.0;
            for (var i = 0; i < n; i++)
                p *= (mask & (1 << i)) != 0 ? probs[i] : (1.0 - probs[i]);
            pAtLeastK += p;
        }
        return Math.Max(0.0, Math.Min(1.0, pAtLeastK));
    }

    private double CalcWholeModifierProbability(CraftWholeModifierAffixData whole, string ic, ClassStats cs, ref bool hasPartialData)
    {
        var names = whole.EffectiveWholeAffixNames();
        if (names.Count == 0) return 0.0;

        var count = names.Sum(n => cs.AffixCounts.TryGetValue(n, out var c) ? c : 0);
        if (count == 0) { hasPartialData = true; return 0.0; }

        var freq = (double)count / cs.TotalSnapshots;
        var rollFrac = 1.0;
        var entry = AffixCraftPatternBuilder.FindEntryByNameAndTierTypeCompatible(
            EffectiveEntries, ic, whole.AffixType, names[0], whole.AffixTier);
        if (entry != null)
        {
            var namesList = names.ToList();
            foreach (var line in whole.Lines)
            {
                var si = CraftAffixCascadeHelper.FindStatIndexInEntry(entry, line.StatTemplate);
                if (si < 0) continue;
                var (lo, hi) = CraftAffixCascadeHelper.GetUnionRollBoundsForWholeLine(
                    ic, whole.AffixType, entry, si, namesList, whole.AffixTier, _entries);
                var minRoll = line.MinRolls.Count > 0 ? line.MinRolls[0] : line.MinRoll;
                rollFrac *= CalcRollFraction(lo, hi, minRoll);
            }
        }
        return Math.Max(0.0, Math.Min(1.0, freq * rollFrac));
    }

    private static double CalcRollFraction(double lo, double hi, double minRoll)
    {
        if (hi <= lo) return minRoll <= lo ? 1.0 : 0.0;
        if (minRoll <= lo) return 1.0;
        if (minRoll > hi) return 0.0;
        return (hi - minRoll + 1.0) / (hi - lo + 1.0);
    }

    private static int BitCount(int x)
    {
        var c = 0;
        while (x != 0) { c += x & 1; x >>= 1; }
        return c;
    }
}
