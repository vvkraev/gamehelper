using System.IO;
using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

public sealed class PipelineStepDetectorTests
{
    // ── Фикстуры ─────────────────────────────────────────────────────────────

    // Фиктивный предмет — содержимое неважно для unit-тестов (evaluator инжектируется)
    private static readonly ParsedItem AnyItem = new() { IsValid = true, ItemClass = "Jewels" };

    private static CraftPipelineStep MakeStep(
        string name,
        PipelineAction action,
        bool hasCondition = false) =>
        new()
        {
            Name = name,
            Action = action,
            EntryCondition = hasCondition ? new CraftConditionPlan() : null,
        };

    // Evaluator: условие совпадает, если имя шага содержится в переданном наборе
    private static PipelineStepDetector.EvaluateDelegate MatchByName(params string[] matchingNames) =>
        (plan, item, out explanation) =>
        {
            // Используем Description плана как метку — стандартного поля нет,
            // поэтому тесты ниже передают label через план напрямую.
            // Вместо этого matchByName работает через захват переменной в тестах.
            explanation = "";
            return false; // заглушка, не использовать напрямую
        };

    // Evaluator: true если label из набора
    private static PipelineStepDetector.EvaluateDelegate Eval(Func<CraftConditionPlan, bool> match) =>
        (plan, item, out explanation) =>
        {
            var result = match(plan);
            explanation = result ? "совпал" : "не совпал";
            return result;
        };

    // Метки для условий: используем Description (есть в CraftConditionPlan через ExpectedItemClass как суррогат)
    // Для чистоты тестов создаём планы с уникальным ExpectedItemClass-«тегом»
    private static CraftConditionPlan Tagged(string tag) =>
        new() { ExpectedItemClass = tag };

    // Evaluator, совпадающий по тегу (ExpectedItemClass)
    private static PipelineStepDetector.EvaluateDelegate MatchTags(params string[] tags) =>
        (plan, item, out explanation) =>
        {
            var ok = Array.IndexOf(tags, plan.ExpectedItemClass) >= 0;
            explanation = ok ? $"тег «{plan.ExpectedItemClass}» в наборе" : $"тег «{plan.ExpectedItemClass}» не в наборе";
            return ok;
        };

    private static CraftPipelineStep TaggedStep(string name, PipelineAction action, string? tag) =>
        new()
        {
            Name = name,
            Action = action,
            EntryCondition = tag is null ? null : Tagged(tag),
        };

    // ── Пустой пайплайн ───────────────────────────────────────────────────────

    [Fact]
    public void EmptyPipeline_ReturnsNull()
    {
        var pipeline = new CraftPipeline { Steps = [] };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags());
        Assert.Null(result.StepIndex);
    }

    // ── Все шаги без EntryCondition — не совпадает ничего ────────────────────

    [Fact]
    public void AllStepsWithoutCondition_ReturnsNull()
    {
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("A", PipelineAction.ChaosCraft, null),
                TaggedStep("B", PipelineAction.DivineCraft, null),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("A", "B"));
        Assert.Null(result.StepIndex);
    }

    // ── Один шаг совпадает ────────────────────────────────────────────────────

    [Fact]
    public void SingleMatchingStep_ReturnsItsIndex()
    {
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("A", PipelineAction.ChaosCraft, "X"),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Equal(0, result.StepIndex);
    }

    // ── Два шага совпадают — возвращает последний (наибольший индекс) ─────────

    [Fact]
    public void TwoMatchingSteps_ReturnsLast()
    {
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("A", PipelineAction.ChaosCraft,   "X"),
                TaggedStep("B", PipelineAction.AugAnnulCraft, "X"),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Equal(1, result.StepIndex);
    }

    // ── Правило n-1: CheckItem / OmenActivation перед совпавшим шагом ──────────

    [Fact]
    public void NMinus1Rule_CheckItemBeforeMatch_ReturnsCheckItem()
    {
        // Шаги: [0] CheckItem «Verify» (тег X)  [1] AugAnnul «Annul» (тег X)
        // Предмет → оба совпадают → алгоритм берёт n=1, смотрит n-1=0 (CheckItem, тоже X) → возвращает 0
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("Verify", PipelineAction.CheckItem,   "X"),
                TaggedStep("Annul",  PipelineAction.AugAnnulCraft, "X"),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Equal(0, result.StepIndex);
    }

    [Fact]
    public void NMinus1Rule_CheckItemConditionDoesNotMatch_ReturnsN()
    {
        // Шаги: [0] CheckItem «Verify» (тег Y — не совпадает)  [1] AugAnnul «Annul» (тег X — совпадает)
        // n=1, n-1=0 (CheckItem), но тег Y не в наборе → n-1 не совпадает → возвращает 1
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("Verify", PipelineAction.CheckItem,   "Y"),
                TaggedStep("Annul",  PipelineAction.AugAnnulCraft, "X"),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Equal(1, result.StepIndex);
    }

    [Fact]
    public void NMinus1Rule_OmenActivationBeforeMatch_ReturnsOmenStep()
    {
        // Шаги: [0] OmenActivation «Omen» (тег X)  [1] AugAnnul «Annul» (тег X)
        // OmenActivation не меняет предмет → правило n-1 применяется → возвращает 0
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("Omen",  PipelineAction.OmenActivation, "X"),
                TaggedStep("Annul", PipelineAction.AugAnnulCraft,  "X"),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Equal(0, result.StepIndex);
    }

    [Fact]
    public void NMinus1Rule_PrevStepIsNotCheckItem_ReturnsN()
    {
        // Шаги: [0] ChaosCraft (тег X)  [1] AugAnnul (тег X)
        // n=1, n-1=0, но 0 — ChaosCraft, не state-preserving → правило n-1 не применяется → возвращает 1
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("Chaos", PipelineAction.ChaosCraft,   "X"),
                TaggedStep("Annul", PipelineAction.AugAnnulCraft, "X"),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Equal(1, result.StepIndex);
    }

    [Fact]
    public void NMinus1Rule_PrevCheckItemHasNoCondition_ReturnsN()
    {
        // Шаги: [0] CheckItem без EntryCondition  [1] AugAnnul (тег X)
        // n=1, n-1=0 (CheckItem), но EntryCondition == null → правило n-1 не применяется → возвращает 1
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("Verify", PipelineAction.CheckItem, null),
                TaggedStep("Annul",  PipelineAction.AugAnnulCraft, "X"),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Equal(1, result.StepIndex);
    }

    // ── Правило n-1 не каскадируется ─────────────────────────────────────────

    [Fact]
    public void NMinus1Rule_IsNotCascaded_CheckItemAtNMinus2IsIgnored()
    {
        // Шаги: [0] CheckItem (Z)  [1] CheckItem (X)  [2] AugAnnul (X)
        // Предмет совпадает с X, не с Z
        // n=2 (AugAnnul), n-1=1 (CheckItem, тег X совпадает) → возвращает 1
        // Не должен продолжить и вернуть 0
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("CheckZ", PipelineAction.CheckItem,    "Z"),
                TaggedStep("CheckX", PipelineAction.CheckItem,    "X"),
                TaggedStep("Annul",  PipelineAction.AugAnnulCraft, "X"),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Equal(1, result.StepIndex);
    }

    // ── Выбирается наибольший совпадающий индекс (сканирование с конца) ───────

    [Fact]
    public void ScanFromEnd_FirstMatchFromEnd_IsSelected()
    {
        // Шаги: [0] Chaos (X)  [1] Divine (Z — не совпадает)  [2] Annul (X)
        // Сканирование: [2] совпадает первым → n=2; n-1=[1] — не CheckItem → возвращает 2
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("Chaos",  PipelineAction.ChaosCraft,   "X"),
                TaggedStep("Divine", PipelineAction.DivineCraft,   "Z"),
                TaggedStep("Annul",  PipelineAction.AugAnnulCraft, "X"),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Equal(2, result.StepIndex);
    }

    // ── Explanation содержит ключевые маркеры ─────────────────────────────────

    [Fact]
    public void Explanation_WhenDetected_ContainsStepName()
    {
        var pipeline = new CraftPipeline
        {
            Steps = [ TaggedStep("МойШаг", PipelineAction.DivineCraft, "X") ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Contains("МойШаг", result.Explanation);
    }

    [Fact]
    public void Explanation_WhenNotDetected_ContainsNotRecognizedText()
    {
        var pipeline = new CraftPipeline
        {
            Steps = [ TaggedStep("A", PipelineAction.ChaosCraft, "Z") ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Null(result.StepIndex);
        Assert.Contains("не распознан", result.Explanation);
    }

    [Fact]
    public void Explanation_NMinus1Rule_MentionsRuleInText()
    {
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("Проверка", PipelineAction.CheckItem,   "X"),
                TaggedStep("Аннул",    PipelineAction.AugAnnulCraft, "X"),
            ]
        };
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("X"));
        Assert.Contains("n-1", result.Explanation);
    }

    // ── Интеграционный тест: TLS крафт (BATCH-1e) ────────────────────────────

    static PipelineStepDetectorTests()
    {
        AffixLibrary.ReloadFromDisk(FindRepoFile("affix_library.json"));
    }

    private static string FindRepoFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, name);
            if (File.Exists(path))
                return path;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"{name} не найден выше {AppContext.BaseDirectory}");
    }

    // ── TLS крафт: 3 состояния × 4 шага ──────────────────────────────────────
    //
    // Пайплайн (упрощённый вариант TLS Crit Suffix):
    //   [0] CheckItem   EntryCondition: есть фрактурный суффикс (IncludeFractured=true; ≥1 криткрит-суффикс)
    //   [1] ChaosCraft   EntryCondition: то же что шаг 0 (= базовая пригодность)
    //   [2] CheckItem   EntryCondition: ≥2 криткрит-суффикса (включая фрактурный)
    //   [3] AugAnnul    EntryCondition: то же что шаг 2
    //
    // Предмет А (только фрактурный CDS) → шаг 0
    // Предмет Б (фрак CDS + нативный CH)    → шаг 2
    // Предмет В (без крит-суффиксов)         → null

    private static CraftPipeline BuildTlsPipeline()
    {
        // Условие «хотя бы 1 из крит-суффиксов (включая фрактурный)»
        var cond1 = new CraftConditionPlan
        {
            ExpectedItemClass = "Jewels",
            OrAlternatives =
            {
                new CraftAndGroup
                {
                    Clauses =
                    {
                        new CraftClause
                        {
                            Kind = CraftClauseKind.Count,
                            Count = new CraftCountAffixData
                            {
                                MinMatchCount = 1,
                                IncludeFractured = true,
                                Members = TlsCritSuffixMembers(),
                            }
                        }
                    }
                }
            }
        };

        // Условие «хотя бы 2 из крит-суффиксов (включая фрактурный)»
        var cond2 = new CraftConditionPlan
        {
            ExpectedItemClass = "Jewels",
            OrAlternatives =
            {
                new CraftAndGroup
                {
                    Clauses =
                    {
                        new CraftClause
                        {
                            Kind = CraftClauseKind.Count,
                            Count = new CraftCountAffixData
                            {
                                MinMatchCount = 2,
                                IncludeFractured = true,
                                Members = TlsCritSuffixMembers(),
                            }
                        }
                    }
                }
            }
        };

        return new CraftPipeline
        {
            Name = "TLS Crit Test",
            ItemClass = "Jewels",
            Steps =
            [
                new CraftPipelineStep { Name = "CheckFrac",  Action = PipelineAction.CheckItem,   EntryCondition = cond1 },
                new CraftPipelineStep { Name = "Chaos",      Action = PipelineAction.ChaosCraft,   EntryCondition = cond1 },
                new CraftPipelineStep { Name = "CheckTwo",   Action = PipelineAction.CheckItem,   EntryCondition = cond2 },
                new CraftPipelineStep { Name = "AugAnnul",   Action = PipelineAction.AugAnnulCraft, EntryCondition = cond2 },
            ]
        };
    }

    // Три крит-суффикса TLS: CHS, CDS, CH — реальные афиксы библиотеки
    private static List<CraftWholeModifierAffixData> TlsCritSuffixMembers() =>
    [
        TlsMember("of Annihilating", "Notable Passive Skills in Radius also grant #% increased Critical Hit Chance for Spells"),
        TlsMember("of Unmaking",     "Notable Passive Skills in Radius also grant #% increased Critical Spell Damage Bonus"),
        TlsMember("of Annihilation", "Notable Passive Skills in Radius also grant #% increased Critical Hit Chance"),
    ];

    private static CraftWholeModifierAffixData TlsMember(string name, string stat) =>
        new()
        {
            AffixType = "Suffix Modifier",
            AffixName = name,
            SelectedAffixNames = [name],
            AffixTier = 1,
            Lines = [ new CraftWholeModifierLine { StatTemplate = stat, MinRoll = 1 } ],
        };

    // Реальный формат текста TLS из буфера обмена PoE2:
    // числа удаляются парсером, остаётся шаблон вида «Notable ... % increased ...»
    private const string TlsItemOnlyFrac = """
        Item Class: Jewels
        Rarity: Rare
        Dragon Ornament
        --------
        { Fractured Suffix Modifier "of Annihilating" (Tier: 1) — Crit }
        Notable Passive Skills in Radius also grant 5% increased Critical Hit Chance for Spells
        --------
        Fractured Item
        """;

    private const string TlsItemFracPlusNative = """
        Item Class: Jewels
        Rarity: Rare
        Dragon Ornament
        --------
        { Fractured Suffix Modifier "of Annihilating" (Tier: 1) — Crit }
        Notable Passive Skills in Radius also grant 5% increased Critical Hit Chance for Spells
        { Suffix Modifier "of Unmaking" (Tier: 1) — Crit }
        Notable Passive Skills in Radius also grant 7% increased Critical Spell Damage Bonus
        --------
        Fractured Item
        """;

    private const string TlsItemNoCrit = """
        Item Class: Jewels
        Rarity: Rare
        Iron Ornament
        --------
        { Suffix Modifier "of Life" (Tier: 1) — Life }
        +50 to maximum Life
        --------
        """;

    [Fact]
    public void TlsPipeline_ItemWithOnlyFrac_DetectsStep0_CheckFrac()
    {
        var pipeline = BuildTlsPipeline();
        var item = ItemParser.Parse(TlsItemOnlyFrac);
        Assert.NotNull(item);

        var result = PipelineStepDetector.Detect(pipeline, item!);

        Assert.Equal(0, result.StepIndex);
        Assert.Contains("CheckFrac", result.Explanation);
        Assert.Contains("n-1", result.Explanation);
    }

    [Fact]
    public void TlsPipeline_ItemWithFracPlusNative_DetectsStep2_CheckTwo()
    {
        var pipeline = BuildTlsPipeline();
        var item = ItemParser.Parse(TlsItemFracPlusNative);
        Assert.NotNull(item);

        var result = PipelineStepDetector.Detect(pipeline, item!);

        Assert.Equal(2, result.StepIndex);
        Assert.Contains("CheckTwo", result.Explanation);
        Assert.Contains("n-1", result.Explanation);
    }

    [Fact]
    public void TlsPipeline_ItemWithNoCrit_ReturnsNull()
    {
        var pipeline = BuildTlsPipeline();
        var item = ItemParser.Parse(TlsItemNoCrit);
        Assert.NotNull(item);

        var result = PipelineStepDetector.Detect(pipeline, item!);

        Assert.Null(result.StepIndex);
        Assert.Contains("не распознан", result.Explanation);
    }

    // ── Правило TravelToLocation ───────────────────────────────────────────────

    /// <summary>
    /// Предмет совпадает с шагом ПОСЛЕ TravelToLocation, но НЕ совпадает с последним
    /// checkpoint'ом ДО вехи → должен быть определён в фазе до вехи.
    /// Воспроизводит кейс: предмет получил десекрейт-префикс в прошлом батче
    /// (совпадает с шагом 15), но не имеет 3 суффиксов (не совпадает с шагом 12).
    /// </summary>
    [Fact]
    public void TravelToLocation_CandidatePastMilestone_CheckpointFails_FallsBackToPrePhase()
    {
        // Pipeline: [0] Chaos(A)  [1] CheckSuffixes(B)  [2] Travel(—)  [3] ChaosDesecrate(C)
        // Предмет: совпадает с C (десекрейт-префикс), НЕ совпадает с B (нет 3 суффиксов) → ожидаем шаг 0
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("Chaos",          PipelineAction.ChaosCraft,       "A"),
                TaggedStep("CheckSuffixes",  PipelineAction.CheckItem,        "B"),
                TaggedStep("Travel",         PipelineAction.TravelToLocation, null),
                TaggedStep("ChaosDesecrate", PipelineAction.ChaosCraft,       "C"),
            ]
        };
        // Предмет имеет «C» (десекрейт) но не «B» (суффиксы)
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("A", "C"));

        // Шаг 3 найден первым (C совпадает), но checkpoint шаг 1 (B) не совпадает
        // → откат в фазу [0..1]; там совпадает шаг 0 (A)
        Assert.Equal(0, result.StepIndex);
        Assert.Contains("Travel-check", result.Explanation);
    }

    /// <summary>
    /// Предмет совпадает с шагом ПОСЛЕ вехи И с checkpoint'ом ДО вехи → остаётся за вехой.
    /// </summary>
    [Fact]
    public void TravelToLocation_CandidatePastMilestone_CheckpointPasses_StaysInPostPhase()
    {
        var pipeline = new CraftPipeline
        {
            Steps =
            [
                TaggedStep("Chaos",          PipelineAction.ChaosCraft,       "A"),
                TaggedStep("CheckSuffixes",  PipelineAction.CheckItem,        "B"),
                TaggedStep("Travel",         PipelineAction.TravelToLocation, null),
                TaggedStep("ChaosDesecrate", PipelineAction.ChaosCraft,       "C"),
            ]
        };
        // Предмет имеет и «B» (3 суффикса) и «C» (десекрейт)
        var result = PipelineStepDetector.DetectCore(pipeline, AnyItem, MatchTags("B", "C"));

        Assert.Equal(3, result.StepIndex);
        Assert.DoesNotContain("Travel-check", result.Explanation);
    }
}
