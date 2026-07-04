using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using GameHelper.Services;

namespace GameHelper.ViewModels;

public class TrackingViewModel : INotifyPropertyChanged
{
    private readonly TrackingStore _store;
    private readonly TradeSnapshotDiffService _diff;
    private readonly TradeImportService _importer;
    private readonly SoldLogStore _soldLog;
    private readonly string _projectRoot;

    private List<TrackingSession> _allSessions = new();
    private TrackingSession? _selectedSession;
    private string _filterStatus = "Все";
    private TrackingItemRow? _selectedItem;
    private string _importJson = "";
    private string _importStatus = "";
    private string _newSessionName = "";
    private List<TrackingItemRow> _allRows = new();

    public ObservableCollection<TrackingSession> Sessions { get; } = new();
    public ObservableCollection<TrackingItemRow> Rows { get; } = new();

    public static string[] FilterOptions { get; } =
        { "Все", "Ушли", "Вернулись", "Новые", "Висят", "Стабильные" };

    public TrackingViewModel(string projectRoot)
    {
        _projectRoot = projectRoot;
        _store = new TrackingStore(projectRoot);
        _diff = new TradeSnapshotDiffService(projectRoot);
        _importer = new TradeImportService(projectRoot);
        _soldLog = new SoldLogStore(projectRoot);
        Reload();
    }

    public TrackingSession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            _selectedSession = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSession));
            RefreshRows();
        }
    }

    public TrackingItemRow? SelectedItem
    {
        get => _selectedItem;
        set { _selectedItem = value; OnPropertyChanged(); OnPropertyChanged(nameof(DetailVisible)); }
    }

    public bool HasSession => _selectedSession != null;
    public bool DetailVisible => _selectedItem != null;

    public string FilterStatus
    {
        get => _filterStatus;
        set { _filterStatus = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public string ImportJson
    {
        get => _importJson;
        set { _importJson = value; OnPropertyChanged(); }
    }

    public string ImportStatus
    {
        get => _importStatus;
        set { _importStatus = value; OnPropertyChanged(); }
    }

    public string NewSessionName
    {
        get => _newSessionName;
        set { _newSessionName = value; OnPropertyChanged(); }
    }

    // --- Команды ---

    public void CreateSession()
    {
        var name = NewSessionName.Trim();
        if (string.IsNullOrEmpty(name)) return;

        var session = new TrackingSession { Name = name, CreatedAt = DateTime.Now };
        _allSessions.Add(session);
        _store.Save(_allSessions);
        NewSessionName = "";
        Reload();
        SelectedSession = Sessions.FirstOrDefault(s => s.Id == session.Id);
    }

    public void DeleteSelectedSession()
    {
        if (_selectedSession is null) return;
        _allSessions.RemoveAll(s => s.Id == _selectedSession.Id);
        _store.Save(_allSessions);
        SelectedSession = null;
        Reload();
    }

    public void DeleteSnapshot(SnapshotRef snap)
    {
        if (_selectedSession is null) return;
        _selectedSession.Snapshots.Remove(snap);
        _store.Save(_allSessions);
        RefreshRows();
        OnPropertyChanged(nameof(SelectedSession));
    }

    public void ImportSnapshot()
    {
        if (_selectedSession is null) { ImportStatus = "Выберите сессию"; return; }
        var json = ImportJson.Trim();
        if (string.IsNullOrEmpty(json)) { ImportStatus = "Вставьте JSON"; return; }

        try
        {
            var result = _importer.Import(json, _selectedSession.Name);
            if (!string.IsNullOrEmpty(result.Error))
            {
                ImportStatus = $"Ошибка: {result.Error}";
                return;
            }

            var snap = new SnapshotRef
            {
                FilePath = Path.Combine("trade_data", result.TradeDataFile),
                ImportedAt = DateTime.Now,
                ItemCount = result.ItemCount,
            };

            // Обновляем базовый тип из первого снимка
            if (string.IsNullOrEmpty(_selectedSession.BaseType))
            {
                var fullPath = Path.Combine(_projectRoot, snap.FilePath);
                _selectedSession.BaseType = TryReadBaseType(fullPath);
            }

            // Логируем проданные до добавления нового снимка
            int soldCount = 0;
            if (_selectedSession.Snapshots.Count > 0)
            {
                var previous = _selectedSession.Snapshots[^1];
                var disappeared = _diff.FindDisappeared(previous, snap);
                foreach (var item in disappeared)
                {
                    _soldLog.Append(new SoldLogEntry
                    {
                        SessionId    = _selectedSession.Id,
                        SessionName  = _selectedSession.Name,
                        SoldAtScan   = snap.ImportedAt,
                        ItemId       = item.Id,
                        Name         = item.Name,
                        PriceDivine  = item.PriceAmount,
                        PriceCurrency= item.PriceCurrency,
                        Ilvl         = item.Ilvl,
                        ListedAt     = item.ListedAt,
                        ModsFractured  = item.ModsFractured,
                        ModsDesecrated = item.ModsDesecrated,
                        ModsExplicit   = item.ModsExplicit,
                        ModsCrafted    = item.ModsCrafted,
                    });
                }
                soldCount = disappeared.Count;
            }

            _selectedSession.Snapshots.Add(snap);
            _store.Save(_allSessions);
            ImportJson = "";
            var soldMsg = soldCount > 0 ? $", записано в лог: {soldCount} продано" : "";
            ImportStatus = $"Импортировано {result.ItemCount} предметов{soldMsg} → {result.TradeDataFile}";
            RefreshRows();
        }
        catch (Exception ex)
        {
            ImportStatus = $"Ошибка: {ex.Message}";
        }
    }

    public void CloseDetail() => SelectedItem = null;

    // --- Внутренние методы ---

    private void Reload()
    {
        _allSessions = _store.Load();
        Sessions.Clear();
        foreach (var s in _allSessions)
            Sessions.Add(s);
    }

    private void RefreshRows()
    {
        Rows.Clear();
        _allRows.Clear();
        if (_selectedSession is null) return;

        _allRows = _diff.BuildRows(_selectedSession);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        var filtered = _filterStatus switch
        {
            "Ушли"       => _allRows.Where(r => r.Status == ItemStatus.Gone),
            "Вернулись"  => _allRows.Where(r => r.Status == ItemStatus.Returned),
            "Новые"      => _allRows.Where(r => r.Status == ItemStatus.New),
            "Висят"      => _allRows.Where(r => r.Status == ItemStatus.Lingering),
            "Стабильные" => _allRows.Where(r => r.Status == ItemStatus.Stable),
            _            => _allRows.AsEnumerable(),
        };
        foreach (var r in filtered)
            Rows.Add(r);
    }

    private static string TryReadBaseType(string path)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
            return node?["_meta"]?["base_type"]?.GetValue<string>() ?? "";
        }
        catch { return ""; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
