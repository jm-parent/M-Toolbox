using Avalonia.Controls;
using Avalonia.Interactivity;
using MToolbox.Core.Enrichment;

namespace MToolbox.App.Views;

public partial class SettingsWindow : Window
{
    private readonly ITokenStore _tokens = null!;

    public SettingsWindow() => InitializeComponent();

    public SettingsWindow(ITokenStore tokens) : this()
    {
        _tokens = tokens;
        // Le secret n'est jamais réaffiché : un champ vide conserve la valeur enregistrée.
        GitHubBox.PlaceholderText = Describe(TokenKeys.GitHub);
        DevOpsBox.PlaceholderText = Describe(TokenKeys.DevOps);
    }

    private string Describe(string key) =>
        string.IsNullOrEmpty(_tokens.Get(key)) ? "Aucun jeton enregistré" : "Enregistré — saisir un nouveau jeton pour le remplacer, ou « - » pour le supprimer";

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        Save(TokenKeys.GitHub, GitHubBox.Text);
        Save(TokenKeys.DevOps, DevOpsBox.Text);
        Close(true);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(false);

    private void Save(string key, string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        _tokens.Set(key, input.Trim() == "-" ? null : input);
    }
}
