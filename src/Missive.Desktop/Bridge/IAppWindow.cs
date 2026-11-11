namespace Missive.Desktop.Bridge;

/// <summary>The app's window, as the bridge handlers see it.</summary>
internal interface IAppWindow
{
    /// <summary>Closes the window, which ends the app.</summary>
    void Close();
}
