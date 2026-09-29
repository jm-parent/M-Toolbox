using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MToolbox.App.Services;
using MToolbox.Core.Catalogue;
using MToolbox.Core.Models;

namespace MToolbox.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MinCardWidth = 340;

    private readonly ICatalogueProvider _catalogue;
    private readonly IPlatformService _platform;
    private readonly IUpdateService _updates;
    private IReadOnlyList<ProjectViewModel> _all = [];
    private bool _suspendRefresh;

    public MainViewModel(ICatalogueProvider catalogue, IPlatformService platform, IUpdateService updates, string version)
    {
        _catalogue = catalogue;
        _platform = platform;
        _updates = updates;
        Version = version;

        TypeOptions =
        [
            new(null, "Tous"),
            new(ProjectType.WindowsApp, "Windows App"),
            new(ProjectType.WebApp, "Web App"),
            new(ProjectType.Tools, "Tools"),
            new(ProjectType.Script, "Script"),
        ];
        SortOptions = [new(ProjectSort.Activity, "Activité"), new(ProjectSort.Name, "Nom")];
        _selectedType = TypeOptions[0];
        _selectedSort = SortOptions[0];

        var clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        clock.Tick += (_, _) => OnPropertyChanged(nameof(SyncText));
        clock.Start();
    }

    public string Version { get; }
    public IReadOnlyList<TypeOption> TypeOptions { get; }
    public IReadOnlyList<SortOption> SortOptions { get; }
    public ObservableCollection<TechnologyOption> Technologies { get; } = [];

    [ObservableProperty] private IReadOnlyList<ProjectViewModel> _visibleProjects = [];
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private TypeOption _selectedType;
    [ObservableProperty] private SortOption _selectedSort;
    [ObservableProperty] private bool _isGridView = true;
    [ObservableProperty] private int _columns = 3;
    [ObservableProperty] private ProjectViewModel? _selectedProject;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private DateTimeOffset? _syncedAt;
    [ObservableProperty] private string? _availableUpdate;

    public int TotalCount => _all.Count;
    public string CountText => $"{VisibleProjects.Count} / {TotalCount} affichés";
    public string TotalText => $"{TotalCount} projets au total";
    public bool IsListView => !IsGridView;
    public bool HasNoResults => !IsLoading && VisibleProjects.Count == 0;
    public bool IsDetailOpen => SelectedProject is not null;
    public string ConnectionText => IsOnline ? "Catalogue : Connecté" : "Catalogue : Hors ligne";
    public string StatusText => IsLoading ? "Chargement…" : "Prêt";
    public string? UpdateText => AvailableUpdate is null ? null : $"Mise à jour {AvailableUpdate} disponible";

    public string SyncText => SyncedAt is not { } at ? "Jamais synchronisé" : (DateTimeOffset.Now - at) switch
    {
        { TotalMinutes: < 1 } => "Synchronisé à l'instant",
        { TotalMinutes: < 60 } t => $"Synchronisé il y a {(int)t.TotalMinutes}m",
        { TotalHours: < 24 } t => $"Synchronisé il y a {(int)t.TotalHours}h",
        var t => $"Synchronisé il y a {(int)t.TotalDays}j",
    };

    public async Task InitializeAsync()
    {
        await RefreshAsync();
        try
        {
            AvailableUpdate = await _updates.CheckAsync();
        }
        catch (Exception)
        {
            // La mise à jour est optionnelle : un échec réseau ne doit pas gêner l'utilisation.
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await _catalogue.LoadAsync();
            _all = result.Catalogue.Projects.Select(p => new ProjectViewModel(p, _platform, ShowDetails)).ToList();
            IsOnline = !result.FromCache;
            SyncedAt = result.SyncedAt;
            RebuildTechnologies();
            ApplyFilters();
        }
        catch (Exception ex)
        {
            IsOnline = false;
            ErrorMessage = $"Impossible de charger le catalogue : {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand] private void ShowGrid() => IsGridView = true;
    [RelayCommand] private void ShowList() => IsGridView = false;
    [RelayCommand] private void CloseDetails() => SelectedProject = null;
    [RelayCommand] private Task ApplyUpdate() => _updates.ApplyAndRestartAsync();

    private void ShowDetails(ProjectViewModel project) => SelectedProject = project;

    [RelayCommand]
    private void ResetFilters()
    {
        _suspendRefresh = true;
        SearchText = "";
        SelectedType = TypeOptions[0];
        foreach (var tech in Technologies) tech.IsSelected = false;
        _suspendRefresh = false;
        ApplyFilters();
    }

    public void UpdateColumns(double availableWidth) =>
        Columns = Math.Max(1, (int)(availableWidth / MinCardWidth));

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnSelectedTypeChanged(TypeOption value) => ApplyFilters();
    partial void OnSelectedSortChanged(SortOption value) => ApplyFilters();

    partial void OnIsGridViewChanged(bool value) => OnPropertyChanged(nameof(IsListView));
    partial void OnIsOnlineChanged(bool value) => OnPropertyChanged(nameof(ConnectionText));
    partial void OnSyncedAtChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(SyncText));
    partial void OnSelectedProjectChanged(ProjectViewModel? value) => OnPropertyChanged(nameof(IsDetailOpen));
    partial void OnAvailableUpdateChanged(string? value) => OnPropertyChanged(nameof(UpdateText));

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasNoResults));
    }

    partial void OnVisibleProjectsChanged(IReadOnlyList<ProjectViewModel> value)
    {
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasNoResults));
    }

    private void RebuildTechnologies()
    {
        var previous = Technologies.Where(t => t.IsSelected).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Technologies.Clear();
        foreach (var name in ProjectFilter.AllTechnologies(_all.Select(p => p.Project)))
            Technologies.Add(new TechnologyOption(name, ApplyFilters) { IsSelected = previous.Contains(name) });

        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(TotalText));
    }

    private void ApplyFilters()
    {
        if (_suspendRefresh) return;

        var selected = Technologies.Where(t => t.IsSelected).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var query = new ProjectQuery(SearchText, SelectedType.Type, selected, SelectedSort.Sort);

        var matching = ProjectFilter.Apply(_all.Select(p => p.Project), query);
        var byId = _all.ToDictionary(p => p.Project.Id);
        VisibleProjects = matching.Select(p => byId[p.Id]).ToList();
    }
}
