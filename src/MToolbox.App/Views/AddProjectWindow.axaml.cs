using Avalonia.Controls;
using Avalonia.Interactivity;
using MToolbox.App.ViewModels;

namespace MToolbox.App.Views;

public partial class AddProjectWindow : Window
{
    public AddProjectWindow() => InitializeComponent();

    public AddProjectWindow(AddProjectViewModel viewModel) : this() => DataContext = viewModel;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
