using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MToolbox.App.Services;
using MToolbox.Core.Enrichment;
using MToolbox.Core.Models;

namespace MToolbox.App.ViewModels;

public sealed partial class ProjectViewModel(
    Project project,
    IPlatformService platform,
    Action<ProjectViewModel> showDetails,
    Func<ProjectViewModel, Task> launch) : ObservableObject
{
    private static readonly string[] AvatarColors =
        ["#0B5CAD", "#8A3B0A", "#0E7490", "#4D5157", "#6D28D9", "#15803D", "#B91C1C"];

    private ICommand? _showDetails, _launch, _openSource, _copySource;

    public Project Project { get; } = project;

    [ObservableProperty] private Bitmap? _logo;
    [ObservableProperty] private bool _isEnriching;
    [ObservableProperty] private ProjectEnrichment? _enrichment;

    public ICommand ShowDetailsCommand => _showDetails ??= new RelayCommand(() => showDetails(this));
    public ICommand LaunchCommand => _launch ??= new AsyncRelayCommand(() => launch(this));
    public ICommand OpenSourceCommand => _openSource ??= new AsyncRelayCommand(() => platform.OpenUrlAsync(SourceUrl ?? ""));
    public ICommand CopySourceCommand => _copySource ??= new AsyncRelayCommand(() => platform.CopyToClipboardAsync(SourceUrl ?? ""));

    public string Title => Project.Title;
    public string Description => Project.Description;
    public string Lead => Project.Lead;
    public IReadOnlyList<string> Technologies => Project.Technologies;
    public string ContributorsText => $"{Project.Contributors} cont.";
    public string? SourceUrl => Project.Source?.Url;
    public bool HasSource => SourceUrl is not null;
    public bool IsWebLink => LaunchPolicy.IsWebUrl(SourceUrl);
    public bool HasLogo => Logo is not null;

    public bool HasLaunch => Project.Launch is not null;

    public string LaunchLabel => Project.Launch?.Kind switch
    {
        LaunchKind.Url => "Ouvrir",
        LaunchKind.Path => "Lancer",
        LaunchKind.Command => "Copier la commande",
        LaunchKind.GitHubRelease => "Télécharger et installer",
        _ => "",
    };

    // Données live (GitHub / DevOps) prioritaires sur celles du catalogue.
    public string ContributorsDisplay => (Enrichment?.Contributors ?? Project.Contributors).ToString();

    public string LastActivityText
    {
        get
        {
            var date = Enrichment?.LastActivity is { } live ? DateOnly.FromDateTime(live.LocalDateTime) : Project.LastActivity;
            return date is { } d ? d.ToString("d MMMM yyyy") : "Inconnue";
        }
    }

    public string? LatestVersionText => Enrichment?.LatestVersion is { } v
        ? Enrichment.LatestReleaseDate is { } d ? $"{v} ({d.LocalDateTime:d MMMM yyyy})" : v
        : null;

    public string? LanguagesText => Enrichment is { Languages.Count: > 0 } e ? string.Join(", ", e.Languages) : null;
    public string? BuildStatusText => Enrichment?.BuildStatus;
    public string? EnrichmentError => Enrichment?.Error;

    public bool HasLatestVersion => LatestVersionText is not null;
    public bool HasLanguages => LanguagesText is not null;
    public bool HasBuildStatus => BuildStatusText is not null;
    public bool HasEnrichmentError => EnrichmentError is not null;

    public string LeadInitials
    {
        get
        {
            var parts = Lead.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant(),
            };
        }
    }

    // string.GetHashCode est aléatoire par processus : couleur instable d'un lancement à l'autre.
    public IBrush AvatarBrush => Brush.Parse(AvatarColors[Lead.Sum(c => c) % AvatarColors.Length]);

    public string TypeLabel => Project.Type switch
    {
        ProjectType.WindowsApp => "Windows App",
        ProjectType.WebApp => "Web App",
        ProjectType.Tools => "Tools",
        _ => "Script",
    };

    public IBrush TypeBrush => Brush.Parse(Project.Type switch
    {
        ProjectType.WindowsApp => "#38B6FF",
        ProjectType.WebApp => "#34D399",
        ProjectType.Tools => "#FB923C",
        _ => "#A6A6A6",
    });

    public string SourceLabel => Project.Source?.Kind switch
    {
        SourceKind.GitHub => "GitHub",
        SourceKind.DevOps => "Azure DevOps",
        SourceKind.Script => "Script",
        _ => "",
    };

    partial void OnLogoChanged(Bitmap? value) => OnPropertyChanged(nameof(HasLogo));

    partial void OnEnrichmentChanged(ProjectEnrichment? value)
    {
        foreach (var name in new[]
        {
            nameof(ContributorsDisplay), nameof(LastActivityText), nameof(LatestVersionText), nameof(HasLatestVersion),
            nameof(LanguagesText), nameof(HasLanguages), nameof(BuildStatusText), nameof(HasBuildStatus),
            nameof(EnrichmentError), nameof(HasEnrichmentError),
        })
            OnPropertyChanged(name);
    }
}

public sealed partial class TechnologyOption(string name, Action onChanged) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => onChanged();
}

public sealed record TypeOption(ProjectType? Type, string Label);

public sealed record SortOption(Core.Catalogue.ProjectSort Sort, string Label);
