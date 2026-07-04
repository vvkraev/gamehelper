using GameHelper.Infrastructure;

namespace GameHelper.ViewModels;

/// <summary>
/// ViewModel для MainWindow. Хранит состояние запущенных операций и предоставляет
/// привязываемые свойства для кнопок Старт/Стоп на всех вкладках.
/// Code-behind изменяет свойства (напр. IsCraftRunning = true) вместо
/// прямых вызовов Button.IsEnabled — XAML-привязки обновляют UI через INPC.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    // ── Крафт ────────────────────────────────────────────────────────────────

    private bool _isCraftRunning;
    public bool IsCraftRunning
    {
        get => _isCraftRunning;
        set
        {
            if (!SetProperty(ref _isCraftRunning, value)) return;
            OnPropertyChanged(nameof(CanStartCraft));
        }
    }

    public bool CanStartCraft => !_isCraftRunning;

    // ── Перековка ─────────────────────────────────────────────────────────────

    private bool _isReforgeRunning;
    public bool IsReforgeRunning
    {
        get => _isReforgeRunning;
        set
        {
            if (!SetProperty(ref _isReforgeRunning, value)) return;
            OnPropertyChanged(nameof(CanStartReforge));
        }
    }

    private bool _isAutoReforgeRunning;
    public bool IsAutoReforgeRunning
    {
        get => _isAutoReforgeRunning;
        set
        {
            if (!SetProperty(ref _isAutoReforgeRunning, value)) return;
            OnPropertyChanged(nameof(CanStartReforge));
            OnPropertyChanged(nameof(CanStartAutoReforge));
        }
    }

    /// <summary>Доступна кнопка ▶ Старт (обычная перековка). Блокируется обоими режимами.</summary>
    public bool CanStartReforge => !_isReforgeRunning && !_isAutoReforgeRunning;

    /// <summary>Доступна кнопка ▶ Авто Старт.</summary>
    public bool CanStartAutoReforge => !_isAutoReforgeRunning;

    // ── Networth ──────────────────────────────────────────────────────────────

    private bool _isNetWorthRunning;
    public bool IsNetWorthRunning
    {
        get => _isNetWorthRunning;
        set
        {
            if (!SetProperty(ref _isNetWorthRunning, value)) return;
            OnPropertyChanged(nameof(CanStartNetWorth));
        }
    }

    public bool CanStartNetWorth => !_isNetWorthRunning;

    // ── Переоценка ────────────────────────────────────────────────────────────

    private bool _isRepricingRunning;
    public bool IsRepricingRunning
    {
        get => _isRepricingRunning;
        set
        {
            if (!SetProperty(ref _isRepricingRunning, value)) return;
            OnPropertyChanged(nameof(CanStartRepricing));
        }
    }

    public bool CanStartRepricing => !_isRepricingRunning;

    // ── Пайплайн крафта ───────────────────────────────────────────────────────

    private bool _isPipelineRunning;
    public bool IsPipelineRunning
    {
        get => _isPipelineRunning;
        set
        {
            if (!SetProperty(ref _isPipelineRunning, value)) return;
            OnPropertyChanged(nameof(CanStartPipeline));
        }
    }

    public bool CanStartPipeline => !_isPipelineRunning;

    private string _pipelineStatusText = "Готово.";
    public string PipelineStatusText
    {
        get => _pipelineStatusText;
        set => SetProperty(ref _pipelineStatusText, value);
    }

    // ── Шансинг ───────────────────────────────────────────────────────────────

    private bool _isChancingRunning;
    public bool IsChancingRunning
    {
        get => _isChancingRunning;
        set
        {
            if (!SetProperty(ref _isChancingRunning, value)) return;
            OnPropertyChanged(nameof(CanStartChancing));
        }
    }

    public bool CanStartChancing => !_isChancingRunning;

    // ── Статус-строки ─────────────────────────────────────────────────────────

    private string _networthStatus = "Не сканировалось.";
    public string NetworthStatus
    {
        get => _networthStatus;
        set => SetProperty(ref _networthStatus, value);
    }

    private string _repricingStatus = "Не запускалось.";
    public string RepricingStatus
    {
        get => _repricingStatus;
        set => SetProperty(ref _repricingStatus, value);
    }

    private string _chancingStatus = "";
    public string ChancingStatus
    {
        get => _chancingStatus;
        set => SetProperty(ref _chancingStatus, value);
    }

    private string _infoCollectionStatus = "Готово к запуску.";
    public string InfoCollectionStatus
    {
        get => _infoCollectionStatus;
        set => SetProperty(ref _infoCollectionStatus, value);
    }

    private string _poeNinjaStatus = "Цены не загружены.";
    public string PoeNinjaStatus
    {
        get => _poeNinjaStatus;
        set => SetProperty(ref _poeNinjaStatus, value);
    }

    // ── Режим крафта — видимость панелей ──────────────────────────────────────

    private bool _isFracturingOrbMode;
    public bool IsFracturingOrbMode
    {
        get => _isFracturingOrbMode;
        set => SetProperty(ref _isFracturingOrbMode, value);
    }

    // ── Вычисляемые output-строки (TextBlock-только, не настройки) ────────────

    private string _craftConditionSummary = "";
    public string CraftConditionSummary
    {
        get => _craftConditionSummary;
        set => SetProperty(ref _craftConditionSummary, value);
    }

    private string _fractOrbEvalResult = "";
    public string FractOrbEvalResult
    {
        get => _fractOrbEvalResult;
        set => SetProperty(ref _fractOrbEvalResult, value);
    }

    private string _rfCatalystSelectionStatus = "";
    public string RfCatalystSelectionStatus
    {
        get => _rfCatalystSelectionStatus;
        set => SetProperty(ref _rfCatalystSelectionStatus, value);
    }

    private string _rfRegistryScanStatus = "";
    public string RfRegistryScanStatus
    {
        get => _rfRegistryScanStatus;
        set => SetProperty(ref _rfRegistryScanStatus, value);
    }

    private string _desecrateSaveInfo = "";
    public string DesecrateSaveInfo
    {
        get => _desecrateSaveInfo;
        set => SetProperty(ref _desecrateSaveInfo, value);
    }

    private string _desecrateStateLabel = "Цикл 1 — Попытка 1 / 2";
    public string DesecrateStateLabel
    {
        get => _desecrateStateLabel;
        set => SetProperty(ref _desecrateStateLabel, value);
    }

    private string _refCategoryTitle = "Выберите категорию слева";
    public string RefCategoryTitle
    {
        get => _refCategoryTitle;
        set => SetProperty(ref _refCategoryTitle, value);
    }

    private string _refCategoryMeta = "";
    public string RefCategoryMeta
    {
        get => _refCategoryMeta;
        set => SetProperty(ref _refCategoryMeta, value);
    }

    private string _tradeHistoryStatus = "История не загружена.";
    public string TradeHistoryStatus
    {
        get => _tradeHistoryStatus;
        set => SetProperty(ref _tradeHistoryStatus, value);
    }

    private string _tradeHistorySummary = "";
    public string TradeHistorySummary
    {
        get => _tradeHistorySummary;
        set => SetProperty(ref _tradeHistorySummary, value);
    }

    private string _tradeGroupSummary = "";
    public string TradeGroupSummary
    {
        get => _tradeGroupSummary;
        set => SetProperty(ref _tradeGroupSummary, value);
    }
}
