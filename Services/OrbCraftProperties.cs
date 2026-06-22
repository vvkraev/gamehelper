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
    /// Известные орбы. Ключ совпадает с <see cref="Name"/>.
    /// Дополнять по мере изучения механики новых орбов.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, OrbCraftProperties> Known =
        new Dictionary<string, OrbCraftProperties>(StringComparer.OrdinalIgnoreCase)
        {
            ["Chaos Orb"]        = new() { Name = "Chaos Orb",        MinModifierLevel = 1,  SelectsTier = true  },
            ["Perfect Chaos Orb"]= new() { Name = "Perfect Chaos Orb",MinModifierLevel = 50, SelectsTier = true  },
            ["Exalted Orb"]      = new() { Name = "Exalted Orb",      MinModifierLevel = 1,  SelectsTier = true  },
            ["Aug Orb"]          = new() { Name = "Aug Orb",          MinModifierLevel = 1,  SelectsTier = true  },
            ["Regal Orb"]        = new() { Name = "Regal Orb",        MinModifierLevel = 1,  SelectsTier = true  },
            ["Divine Orb"]       = new() { Name = "Divine Orb",       MinModifierLevel = 1,  SelectsTier = false },
            ["Annulment Orb"]    = new() { Name = "Annulment Orb",    MinModifierLevel = 1,  SelectsTier = false },
        };
}
