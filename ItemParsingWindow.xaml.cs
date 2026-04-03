using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using GameHelper.Services;

namespace GameHelper;

public partial class ItemParsingWindow : Window
{
    private readonly ParsedItem _parsedItem;

    public ItemParsingWindow(ParsedItem item)
    {
        InitializeComponent();
        _parsedItem = item ?? throw new ArgumentNullException(nameof(item));
        DisplayItem();
    }

    private void DisplayItem()
    {
        try
        {
            // Item Name
            ItemNameTextBox.Text = _parsedItem.Name ?? "(No Name)";

            // Item Type and Rarity
            ItemTypeTextBox.Text = $"{_parsedItem.ItemClass} ({_parsedItem.Rarity})";

            // Build properties text
            var properties = new StringBuilder();

            // Base Item
            if (!string.IsNullOrEmpty(_parsedItem.Base))
                properties.AppendLine($"Base: {_parsedItem.Base}");

            // Item Level
            properties.AppendLine($"Item Level: {_parsedItem.ItemLevel}");

            // Quality
            if (!string.IsNullOrEmpty(_parsedItem.Quality))
                properties.AppendLine($"Quality: {_parsedItem.Quality}");

            // Requirements
            if (!string.IsNullOrEmpty(_parsedItem.Requirements))
                properties.AppendLine($"Requires: {_parsedItem.Requirements}");

            // Sockets
            if (!string.IsNullOrEmpty(_parsedItem.Sockets))
                properties.AppendLine($"Sockets: {_parsedItem.Sockets}");

            // State (Corrupted, Sanctified, etc.)
            if (!string.IsNullOrEmpty(_parsedItem.State))
                properties.AppendLine($"State: {_parsedItem.State}");

            // Characteristics
            if (_parsedItem.Characteristics.Count > 0)
            {
                properties.AppendLine("\n═══ CHARACTERISTICS ═══");
                foreach (var characteristic in _parsedItem.Characteristics)
                {
                    properties.AppendLine($"  {characteristic}");
                }
            }

            if (_parsedItem.Augments.Count > 0)
            {
                properties.AppendLine("\n═══ AUGMENTS ═══");
                foreach (var line in _parsedItem.Augments)
                    properties.AppendLine($"  {line}");
            }

            // Inserted Items (Runes)
            if (_parsedItem.InsertedItems.Count > 0)
            {
                properties.AppendLine("\n═══ INSERTED ITEMS (RUNES) ═══");
                foreach (var item in _parsedItem.InsertedItems)
                {
                    properties.AppendLine($"  {item}");
                }
            }

            // Affixes (отдельный блок; строки эффектов не смешиваются с CHARACTERISTICS)
            if (_parsedItem.Affixes.Count > 0)
            {
                properties.AppendLine("\n═══ AFFIXES ═══");
                var affixIndex = 0;
                foreach (var affix in _parsedItem.Affixes)
                {
                    affixIndex++;
                    properties.AppendLine($"{affixIndex} аффикс:");
                    var typeShort = affix.Type switch
                    {
                        "Prefix Modifier" => "Prefix",
                        "Suffix Modifier" => "Suffix",
                        "Desecrated Prefix Modifier" => "Desecrated Prefix",
                        "Desecrated Suffix Modifier" => "Desecrated Suffix",
                        _ => string.IsNullOrEmpty(affix.Type) ? "—" : affix.Type,
                    };
                    properties.AppendLine($"Affix Type: {typeShort}");
                    properties.AppendLine($"Affix Name: {affix.Name}");
                    properties.AppendLine($"Affix Tier: {affix.Tier}");
                    properties.AppendLine($"Affix Tag: {string.Join(", ", affix.Tags)}");

                    var details = affix.EffectDetails.Count > 0
                        ? affix.EffectDetails
                        : affix.Effects.Select(e => new AffixEffectLine { Raw = e, StatText = e }).ToList();

                    if (details.Count > 0)
                    {
                        properties.AppendLine("Affix Stat:");
                        for (var i = 0; i < details.Count; i++)
                            properties.AppendLine($"{i + 1}: {details[i].StatText}");

                        properties.AppendLine("Affix Range:");
                        for (var i = 0; i < details.Count; i++)
                            properties.AppendLine($"{i + 1}: {(details[i].Range ?? "—")}");

                        properties.AppendLine("Affix Value:");
                        for (var i = 0; i < details.Count; i++)
                            properties.AppendLine($"{i + 1}: {(details[i].RolledValue ?? "—")}");
                    }

                    properties.AppendLine();
                }
            }

            ItemPropertiesTextBox.Text = properties.ToString();
        }
        catch (Exception ex)
        {
            ItemPropertiesTextBox.Text = $"Error displaying item: {ex.Message}";
            SessionLogger.Info($"ItemParsingWindow error: {ex.Message}");
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}