namespace Dynpost.Infrastructure.Tests.Git;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = Directory.CreateTempSubdirectory("dynpost-tests-").FullName;
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            // Git marks object files read-only, which blocks deleting them on Windows.
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder must not fail the test.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
