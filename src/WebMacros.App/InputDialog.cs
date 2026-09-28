using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WebMacros.App;

/// <summary>Simple modal text input used by PROMPT, unhandled JS prompt() and "Save as".</summary>
public sealed class InputDialog : Window
{
    private readonly TextBox _box;

    private InputDialog(string title, string message, string? defaultValue, bool showInput)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 380;
        MaxWidth = 700;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        _box = new TextBox { Text = defaultValue ?? "", Visibility = showInput ? Visibility.Visible : Visibility.Collapsed };
        panel.Children.Add(_box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;

        Loaded += (_, _) =>
        {
            if (showInput) { _box.Focus(); _box.SelectAll(); }
            else ok.Focus();
        };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
    }

    /// <summary>Returns the entered text, or null if cancelled.</summary>
    public static string? Ask(Window? owner, string title, string message, string? defaultValue = null)
    {
        var d = new InputDialog(title, message, defaultValue, showInput: true);
        if (owner is { IsLoaded: true }) d.Owner = owner;
        return d.ShowDialog() == true ? d._box.Text : null;
    }

    public static bool Confirm(Window? owner, string title, string message)
    {
        var d = new InputDialog(title, message, null, showInput: false);
        if (owner is { IsLoaded: true }) d.Owner = owner;
        return d.ShowDialog() == true;
    }
}
