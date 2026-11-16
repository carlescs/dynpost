using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Dynpost.Application.Common;
using Dynpost.Application.Secrets;

namespace Dynpost.Infrastructure.Secrets;

/// <summary>Picks this system's credential store: Windows Credential Manager, the macOS Keychain or libsecret.</summary>
public static class SystemKeychain
{
    /// <summary>The service name values are stored under.</summary>
    public const string Service = "Dynpost";

    public static IKeychain Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialManager();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychain();
        }

        return new LibSecretKeychain();
    }
}

/// <summary>Windows Credential Manager: one generic credential per account, <c>Dynpost:account</c>.</summary>
internal sealed partial class WindowsCredentialManager : IKeychain
{
    private const int GenericType = 1;
    private const int PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string Name => "Windows Credential Manager";

    private static string Target(string account) => $"{SystemKeychain.Service}:{account}";

    public Task<Result<string?>> GetAsync(string account, CancellationToken cancellationToken = default)
    {
        if (!CredRead(Target(account), GenericType, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            return Task.FromResult(error == ErrorNotFound
                ? Result<string?>.Success(null)
                : Result<string?>.Failure(ErrorCodes.Io, $"Could not read from {Name}: {new Win32Exception(error).Message}"));
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult(Result<string?>.Success(Encoding.UTF8.GetString(bytes)));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public Task<Result<Unit>> SetAsync(string account, string value, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var blob = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1));
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential
            {
                Type = GenericType,
                TargetName = Target(account),
                CredentialBlobSize = bytes.Length,
                CredentialBlob = blob,
                Persist = PersistLocalMachine,
                UserName = SystemKeychain.Service,
            };
            return Task.FromResult(CredWrite(ref credential, 0)
                ? Result<Unit>.Success(Unit.Value)
                : Result<Unit>.Failure(ErrorCodes.Io, $"Could not save to {Name}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}"));
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public Task<Result<Unit>> DeleteAsync(string account, CancellationToken cancellationToken = default)
    {
        if (CredDelete(Target(account), GenericType, 0) || Marshal.GetLastWin32Error() == ErrorNotFound)
        {
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        return Task.FromResult(Result<Unit>.Failure(ErrorCodes.Io, $"Could not delete from {Name}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}"));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>The macOS Keychain, through the <c>security</c> command: generic passwords of the service <c>Dynpost</c>.</summary>
internal sealed class MacKeychain : IKeychain
{
    private const string Tool = "/usr/bin/security";
    private const int NotFound = 44;

    public bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists(Tool);

    public string Name => "the macOS Keychain";

    public async Task<Result<string?>> GetAsync(string account, CancellationToken cancellationToken = default)
    {
        var run = await KeychainProcess.RunAsync(Tool, ["find-generic-password", "-s", SystemKeychain.Service, "-a", account, "-w"], null, cancellationToken);
        return run.ExitCode switch
        {
            0 => run.Output.TrimEnd('\n'),
            NotFound => (string?)null,
            _ => new Error(ErrorCodes.Io, $"Could not read from {Name}: {run.Error.Trim()}"),
        };
    }

    public async Task<Result<Unit>> SetAsync(string account, string value, CancellationToken cancellationToken = default)
    {
        var run = await KeychainProcess.RunAsync(Tool, ["add-generic-password", "-U", "-s", SystemKeychain.Service, "-a", account, "-w", value], null, cancellationToken);
        return run.ExitCode == 0 ? Unit.Value : new Error(ErrorCodes.Io, $"Could not save to {Name}: {run.Error.Trim()}");
    }

    public async Task<Result<Unit>> DeleteAsync(string account, CancellationToken cancellationToken = default)
    {
        var run = await KeychainProcess.RunAsync(Tool, ["delete-generic-password", "-s", SystemKeychain.Service, "-a", account], null, cancellationToken);
        return run.ExitCode is 0 or NotFound ? Unit.Value : new Error(ErrorCodes.Io, $"Could not delete from {Name}: {run.Error.Trim()}");
    }
}

/// <summary>
/// The Secret Service (GNOME Keyring, KWallet) through libsecret's <c>secret-tool</c>: items with
/// the attributes <c>service=Dynpost</c> and <c>account</c>.
/// </summary>
internal sealed class LibSecretKeychain : IKeychain
{
    private const string Tool = "secret-tool";
    private readonly Lazy<bool> _available = new(Probe);

    public bool IsAvailable => _available.Value;

    public string Name => "the Secret Service (libsecret)";

    public async Task<Result<string?>> GetAsync(string account, CancellationToken cancellationToken = default)
    {
        var run = await KeychainProcess.RunAsync(Tool, ["lookup", "service", SystemKeychain.Service, "account", account], null, cancellationToken);
        if (run.ExitCode == 0)
        {
            return run.Output;
        }

        return run.Error.Trim().Length == 0
            ? (string?)null
            : new Error(ErrorCodes.Io, $"Could not read from {Name}: {run.Error.Trim()}");
    }

    public async Task<Result<Unit>> SetAsync(string account, string value, CancellationToken cancellationToken = default)
    {
        var run = await KeychainProcess.RunAsync(
            Tool,
            ["store", $"--label=Dynpost secret {account}", "service", SystemKeychain.Service, "account", account],
            value,
            cancellationToken);
        return run.ExitCode == 0 ? Unit.Value : new Error(ErrorCodes.Io, $"Could not save to {Name}: {run.Error.Trim()}");
    }

    public async Task<Result<Unit>> DeleteAsync(string account, CancellationToken cancellationToken = default)
    {
        var run = await KeychainProcess.RunAsync(Tool, ["clear", "service", SystemKeychain.Service, "account", account], null, cancellationToken);
        return run.ExitCode == 0 || run.Error.Trim().Length == 0 ? Unit.Value : new Error(ErrorCodes.Io, $"Could not delete from {Name}: {run.Error.Trim()}");
    }

    /// <summary>Usable when <c>secret-tool</c> runs and reaches a Secret Service (a lookup reports no error).</summary>
    private static bool Probe()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var run = KeychainProcess.RunAsync(Tool, ["lookup", "service", SystemKeychain.Service, "account", "availability-check"], null, CancellationToken.None)
                .GetAwaiter().GetResult();
            return run.ExitCode is 0 or 1 && run.Error.Trim().Length == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}

/// <summary>Runs a credential tool with a timeout, never through a shell.</summary>
internal static class KeychainProcess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public sealed record Outcome(int ExitCode, string Output, string Error);

    public static async Task<Outcome> RunAsync(string tool, IReadOnlyList<string> arguments, string? input, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {tool}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
        }

        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return new Outcome(-1, string.Empty, $"{tool} did not answer in time.");
        }

        return new Outcome(process.ExitCode, await output, await error);
    }
}
