using Photino.NET;

namespace Missive.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");

        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException("Angular assets are missing. Run dotnet build first.", indexPath);
        }

        new PhotinoWindow()
            .SetTitle("Missive")
            .SetUseOsDefaultSize(false)
            .SetSize(1024, 768)
            .Center()
            .Load(indexPath)
            .WaitForClose();
    }
}
