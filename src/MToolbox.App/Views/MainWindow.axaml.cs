using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MToolbox.App.ViewModels;
using MToolbox.Core.Enrichment;

namespace MToolbox.App.Views;

public partial class MainWindow : Window
{
    public ITokenStore? TokenStore { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        ListHost.SizeChanged += (_, e) => (DataContext as MainViewModel)?.UpdateColumns(e.NewSize.Width);
        Opened += async (_, _) =>
        {
            if (DataContext is MainViewModel vm) await vm.InitializeAsync();
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e is { Key: Key.F, KeyModifiers: KeyModifiers.Control })
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && DataContext is MainViewModel { IsDetailOpen: true } vm)
        {
            vm.CloseDetailsCommand.Execute(null);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.CloseDetailsCommand.Execute(null);

    private async void OnAddProjectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) await new AddProjectWindow(vm.CreateAddProject()).ShowDialog(this);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (TokenStore is null) return;

        var changed = await new SettingsWindow(TokenStore).ShowDialog<bool>(this);
        if (changed) (DataContext as MainViewModel)?.ResetEnrichment();
    }
}
