namespace GameHelper.Services;

/// <summary>
/// Свойства орба, влияющие на выбор тира аффикса при крафте.
/// Используется в расчёте вероятностей доступных тиров (шаги 2–3 алгоритма).
/// См. docs/AFFIX_TIER_SELECTION_MECHANICS.md.
/// </summary>
public sealed class OrbCraftProperties
{
    public string Name { get; init; } = "";

    /// <summary>
    /// Минимальный <c>modifier_level</c> тира для включения в пул выбора (шаг 3).
    /// Тиры ниже этого значения недоступны для орба (за исключением T1).
    /// </summary>
    public int MinModifierLevel { get; init; } = 1;

    /// <summary>
    /// True — орб выбирает новый тир аффикса (Chaos, Exalt, Aug и т.д.).
    /// False — орб не выбирает тир (Divine перебрасывает значение, Annul удаляет).
    /// </summary>
    public bool SelectsTier { get; init; } = true;

    /// <summary>
    /// True — орб удаляет один случайный существующий модификатор и добавляет новый (Chaos Orb).
    /// False — орб добавляет новый модификатор без удаления (Aug, Exalt).
    /// При True вероятность рассчитывается через Monte Carlo, а не через single-shot формулу.
    /// </summary>
    public bool ReplacesOneExisting { get; init; } = false;

    /// <summary>
    /// Известные орбы. Ключ совпадает с <see cref="Name"/>.
    /// Дополнять по мере изучения механики новых орбов.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, OrbCraftProperties> Known =
        new Dictionary<string, OrbCraftProperties>(StringComparer.OrdinalIgnoreCase)
        {
            // Transmutation: Normal → Magic (1 modifier)
            ["Orb of Transmutation"]         = new() { Name = "Orb of Transmutation",         MinModifierLevel = 1,  SelectsTier = true  },
            ["Greater Orb of Transmutation"] = new() { Name = "Greater Orb of Transmutation", MinModifierLevel = 44, SelectsTier = true  },
            ["Perfect Orb of Transmutation"] = new() { Name = "Perfect Orb of Transmutation", MinModifierLevel = 70, SelectsTier = true  },

            // Augmentation: добавляет 1 модификатор на Magic предмет
            ["Orb of Augmentation"]          = new() { Name = "Orb of Augmentation",          MinModifierLevel = 1,  SelectsTier = true  },
            ["Greater Orb of Augmentation"]  = new() { Name = "Greater Orb of Augmentation",  MinModifierLevel = 44, SelectsTier = true  },
            ["Perfect Orb of Augmentation"]  = new() { Name = "Perfect Orb of Augmentation",  MinModifierLevel = 70, SelectsTier = true  },

            // Regal: Magic → Rare (добавляет 1 модификатор)
            ["Regal Orb"]                    = new() { Name = "Regal Orb",                    MinModifierLevel = 1,  SelectsTier = true  },
            ["Greater Regal Orb"]            = new() { Name = "Greater Regal Orb",            MinModifierLevel = 35, SelectsTier = true  },
            ["Perfect Regal Orb"]            = new() { Name = "Perfect Regal Orb",            MinModifierLevel = 50, SelectsTier = true  },

            // Chaos: удаляет 1 случайный мод и добавляет 1 новый на Rare предмете
            ["Chaos Orb"]                    = new() { Name = "Chaos Orb",                    MinModifierLevel = 1,  SelectsTier = true, ReplacesOneExisting = true  },
            ["Greater Chaos Orb"]            = new() { Name = "Greater Chaos Orb",            MinModifierLevel = 35, SelectsTier = true, ReplacesOneExisting = true  },
            ["Perfect Chaos Orb"]            = new() { Name = "Perfect Chaos Orb",            MinModifierLevel = 50, SelectsTier = true, ReplacesOneExisting = true  },

            // Exalted: добавляет 1 модификатор на Rare предмет
            ["Exalted Orb"]                  = new() { Name = "Exalted Orb",                  MinModifierLevel = 1,  SelectsTier = true  },
            ["Greater Exalted Orb"]          = new() { Name = "Greater Exalted Orb",          MinModifierLevel = 35, SelectsTier = true  },
            ["Perfect Exalted Orb"]          = new() { Name = "Perfect Exalted Orb",          MinModifierLevel = 50, SelectsTier = true  },

            // Не выбирают тир / не добавляют новый мод
            ["Divine Orb"]                   = new() { Name = "Divine Orb",                   MinModifierLevel = 1,  SelectsTier = false },
            ["Orb of Annulment"]             = new() { Name = "Orb of Annulment",             MinModifierLevel = 1,  SelectsTier = false },
            ["Orb of Chance"]                = new() { Name = "Orb of Chance",                MinModifierLevel = 1,  SelectsTier = false },
            ["Fracturing Orb"]               = new() { Name = "Fracturing Orb",               MinModifierLevel = 1,  SelectsTier = false },
        };
}
