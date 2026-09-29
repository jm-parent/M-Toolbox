using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace MToolbox.App.Views;

public sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cancel = new Button { Content = "Annuler", IsCancel = true };
        var accept = new Button { Content = "Continuer", IsDefault = true };
        cancel.Click += (_, _) => Close(false);
        accept.Click += (_, _) => Close(true);

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, accept },
                },
            },
        };
    }
}
