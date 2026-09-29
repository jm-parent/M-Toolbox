using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MToolbox.App.Services;
using MToolbox.Core.Models;

namespace MToolbox.App.ViewModels;

public sealed class ProjectViewModel(Project project, IPlatformService platform, Action<ProjectViewModel> showDetails)
{
    private static readonly string[] AvatarColors =
        ["#0B5CAD", "#8A3B0A", "#0E7490", "#4D5157", "#6D28D9", "#15803D", "#B91C1C"];

    public Project Project { get; } = project;

    private ICommand? _showDetails, _openSource, _copySource;

    public ICommand ShowDetailsCommand => _showDetails ??= new RelayCommand(() => showDetails(this));
    public ICommand OpenSourceCommand => _openSource ??= new AsyncRelayCommand(() => platform.OpenUrlAsync(SourceUrl ?? ""));
    public ICommand CopySourceCommand => _copySource ??= new AsyncRelayCommand(() => platform.CopyToClipboardAsync(SourceUrl ?? ""));

    public string Title => Project.Title;
    public string Description => Project.Description;
    public string Lead => Project.Lead;
    public IReadOnlyList<string> Technologies => Project.Technologies;
    public string ContributorsText => $"{Project.Contributors} cont.";
    public string? SourceUrl => Project.Source?.Url;

    public string LastActivityText =>
        Project.LastActivity is { } d ? d.ToString("d MMMM yyyy") : "Inconnue";

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
    public IBrush AvatarBrush => Brush.Parse(
        AvatarColors[Lead.Sum(c => c) % AvatarColors.Length]);

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

    public bool IsWebLink => SourceUrl is { } u &&
        Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";

    public bool HasSource => SourceUrl is not null;
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
