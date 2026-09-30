using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MToolbox.App.Services;
using MToolbox.Core.Catalogue;
using MToolbox.Core.Enrichment;
using MToolbox.Core.Models;

namespace MToolbox.App.ViewModels;

public sealed record Option(object? Value, string Label);

public sealed partial class AddProjectViewModel : ObservableObject
{
    private readonly IProjectPrefillService _prefill;
    private readonly IPlatformService _platform;
    private readonly IReadOnlyCollection<string> _existingIds;
    private readonly string _repoUrl;
    private bool _idEdited, _updatingId;

    public AddProjectViewModel(IProjectPrefillService prefill, IPlatformService platform, IReadOnlyCollection<string> existingIds, string repoUrl)
    {
        (_prefill, _platform, _existingIds, _repoUrl) = (prefill, platform, existingIds, repoUrl);

        TypeOptions =
        [
            new(ProjectType.WindowsApp, "Windows App"),
            new(ProjectType.WebApp, "Web App"),
            new(ProjectType.Tools, "Tools"),
            new(ProjectType.Script, "Script"),
        ];
        SourceOptions = [new(null, "Aucune"), new(SourceKind.GitHub, "GitHub"), new(SourceKind.DevOps, "Azure DevOps")];
        LaunchOptions =
        [
            new(null, "Aucun"),
            new(LaunchKind.GitHubRelease, "Release GitHub (motif du fichier, ex. *Setup.exe)"),
            new(LaunchKind.Url, "Lien web"),
            new(LaunchKind.Path, "Chemin local ou réseau"),
            new(LaunchKind.Command, "Commande à copier"),
        ];
        _selectedType = TypeOptions[0];
        _selectedSource = SourceOptions[0];
        _selectedLaunch = LaunchOptions[0];
    }

    public IReadOnlyList<Option> TypeOptions { get; }
    public IReadOnlyList<Option> SourceOptions { get; }
    public IReadOnlyList<Option> LaunchOptions { get; }

    [ObservableProperty] private string _prefillUrl = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private Option _selectedType;
    [ObservableProperty] private string _technologiesText = "";
    [ObservableProperty] private string _lead = "";
    [ObservableProperty] private string _contributorsText = "0";
    [ObservableProperty] private string _lastActivityText = "";
    [ObservableProperty] private Option _selectedSource;
    [ObservableProperty] private string _sourceUrl = "";
    [ObservableProperty] private Option _selectedLaunch;
    [ObservableProperty] private string _launchValue = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _infoText;
    [ObservableProperty] private string? _errorText;

    partial void OnTitleChanged(string value)
    {
        if (_idEdited) return;
        _updatingId = true;
        Id = ProjectSubmission.Slugify(value);
        _updatingId = false;
    }

    partial void OnIdChanged(string value)
    {
        if (!_updatingId) _idEdited = true;
    }

    [RelayCommand]
    private async Task PrefillAsync()
    {
        ErrorText = InfoText = null;
        IsBusy = true;
        try
        {
            var result = await _prefill.PrefillAsync(PrefillUrl);
            if (result.Project is not { } p)
            {
                ErrorText = result.Message;
                return;
            }

            Apply(p);
            InfoText = result.Message ?? "Formulaire prérempli : complétez le responsable et vérifiez les champs.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        ErrorText = InfoText = null;

        var errors = new List<string>();
        var project = Build(errors);
        if (project is not null) errors.AddRange(ProjectSubmission.Validate(project, _existingIds));
        if (errors.Count > 0)
        {
            ErrorText = string.Join(Environment.NewLine, errors.Select(e => "• " + e));
            return;
        }

        var request = AddProjectIssue.BuildRequest(_repoUrl, project!);
        if (!request.JsonInUrl) await _platform.CopyToClipboardAsync(request.ProjectJson);

        await _platform.OpenUrlAsync(request.Url.AbsoluteUri);
        InfoText = request.JsonInUrl
            ? "Issue ouverte dans le navigateur : cliquez sur « Submit new issue » (joignez le logo PNG si besoin). Une PR sera créée automatiquement."
            : "Issue ouverte dans le navigateur. Le projet est trop volumineux pour l'URL : collez le contenu du presse-papiers dans le champ « Projet (JSON) », puis validez.";
    }

    private void Apply(Project p)
    {
        _idEdited = false;
        Title = p.Title;
        Description = p.Description;
        TechnologiesText = string.Join(", ", p.Technologies);
        ContributorsText = p.Contributors.ToString(CultureInfo.InvariantCulture);
        LastActivityText = p.LastActivity?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        SelectedType = TypeOptions.First(o => Equals(o.Value, p.Type));
        SelectedSource = SourceOptions.First(o => Equals(o.Value, p.Source?.Kind));
        SourceUrl = p.Source?.Url ?? "";
        SelectedLaunch = LaunchOptions.First(o => Equals(o.Value, p.Launch?.Kind));
        LaunchValue = p.Launch?.Value ?? "";
    }

    private Project? Build(List<string> errors)
    {
        if (!int.TryParse(ContributorsText, NumberStyles.None, CultureInfo.InvariantCulture, out var contributors))
            errors.Add("Le nombre de contributeurs doit être un entier positif.");

        DateOnly? lastActivity = null;
        if (!string.IsNullOrWhiteSpace(LastActivityText))
        {
            if (DateOnly.TryParseExact(LastActivityText.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                lastActivity = date;
            else
                errors.Add("La dernière activité doit être au format AAAA-MM-JJ.");
        }

        if (errors.Count > 0) return null;

        var technologies = TechnologiesText
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Project
        {
            Id = Id.Trim(),
            Title = Title.Trim(),
            Description = Description.Trim(),
            Type = (ProjectType)SelectedType.Value!,
            Technologies = technologies,
            Lead = Lead.Trim(),
            Contributors = contributors,
            LastActivity = lastActivity,
            Source = SelectedSource.Value is SourceKind sk ? new ProjectSource(sk, SourceUrl.Trim()) : null,
            Launch = SelectedLaunch.Value is LaunchKind lk ? new LaunchSpec(lk, LaunchValue.Trim()) : null,
        };
    }
}
