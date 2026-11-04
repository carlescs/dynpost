using Missive.Desktop.Hosting;
using Photino.NET;

namespace Missive.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var indexPath = Path.Combine(wwwroot, "index.html");

        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("Angular assets are missing. Run dotnet build first.", indexPath);
        }

        var assets = new BundledAssets(wwwroot);

        new PhotinoWindow()
            .SetTitle("Missive")
            .SetUseOsDefaultSize(false)
            .SetSize(1024, 768)
            .Center()
            .RegisterCustomSchemeHandler(BundledAssets.Scheme, assets.Handle)
            .Load(BundledAssets.StartUri)
            .WaitForClose();
    }
}
