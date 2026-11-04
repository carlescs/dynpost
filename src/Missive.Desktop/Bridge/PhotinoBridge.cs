using Photino.NET;

namespace Missive.Desktop.Bridge;

/// <summary>Connects the Photino window's web messages to the <see cref="BridgeDispatcher"/>.</summary>
internal sealed class PhotinoBridge(BridgeDispatcher dispatcher)
{
    public void OnWebMessageReceived(object? sender, string message)
    {
        if (sender is not PhotinoWindow window)
        {
            return;
        }

        // Handle off the UI thread so long git commands don't freeze the window. SendWebMessage
        // marshals back to the UI thread.
        _ = Task.Run(async () =>
        {
            var response = await dispatcher.DispatchAsync(message);
            if (response is null)
            {
                Console.Error.WriteLine("Ignored a bridge message that is not a valid request.");
                return;
            }

            try
            {
                window.SendWebMessage(response);
            }
            catch (Exception ex)
            {
                // The window may be closing.
                Console.Error.WriteLine($"Could not send a bridge response: {ex.Message}");
            }
        });
    }
}
