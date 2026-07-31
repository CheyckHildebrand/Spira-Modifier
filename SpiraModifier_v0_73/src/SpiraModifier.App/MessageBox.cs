using System.Windows;

namespace SpiraModifier.App;

internal static class MessageBox
{
    public static MessageBoxResult Show(
        Window owner,
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        return System.Windows.MessageBox.Show(
            owner,
            UiLocalization.Translate(messageBoxText),
            UiLocalization.Translate(caption),
            button,
            icon);
    }

    public static MessageBoxResult Show(
        Window owner,
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon,
        MessageBoxResult defaultResult)
    {
        return System.Windows.MessageBox.Show(
            owner,
            UiLocalization.Translate(messageBoxText),
            UiLocalization.Translate(caption),
            button,
            icon,
            defaultResult);
    }
}
