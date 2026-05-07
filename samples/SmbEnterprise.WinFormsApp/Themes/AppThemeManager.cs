namespace SmbEnterprise.WinFormsApp.Themes;

public enum AppTheme
{
    Light,
    Dark
}

public sealed class AppThemeManager
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public void ApplyTheme(Control root, AppTheme theme)
    {
        CurrentTheme = theme;
        ApplyRecursive(root, theme);
    }

    private static void ApplyRecursive(Control control, AppTheme theme)
    {
        if (theme == AppTheme.Dark)
        {
            control.BackColor = Color.FromArgb(32, 32, 32);
            control.ForeColor = Color.Gainsboro;
        }
        else
        {
            control.BackColor = Color.White;
            control.ForeColor = Color.FromArgb(32, 32, 32);
        }

        if (control is ListView listView)
        {
            listView.BackColor = theme == AppTheme.Dark ? Color.FromArgb(40, 40, 40) : Color.White;
            listView.ForeColor = theme == AppTheme.Dark ? Color.Gainsboro : Color.FromArgb(32, 32, 32);
        }

        if (control is TreeView treeView)
        {
            treeView.BackColor = theme == AppTheme.Dark ? Color.FromArgb(40, 40, 40) : Color.White;
            treeView.ForeColor = theme == AppTheme.Dark ? Color.Gainsboro : Color.FromArgb(32, 32, 32);
        }

        foreach (Control child in control.Controls)
        {
            ApplyRecursive(child, theme);
        }
    }
}
