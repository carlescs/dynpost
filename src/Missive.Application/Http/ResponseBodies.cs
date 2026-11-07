namespace Missive.Application.Http;

/// <summary>
/// The raw bodies of recent responses, by the id of the send that got them, so they can be saved
/// to a file exactly as received. Older bodies are dropped once <see cref="MaxBytes"/> is reached,
/// but the most recent one is always kept. Registered as a singleton.
/// </summary>
public sealed class ResponseBodies
{
    /// <summary>The total size of the bodies kept, beyond the most recent one.</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>How many bodies are kept at most.</summary>
    public const int MaxCount = 32;

    private readonly object _lock = new();
    private readonly LinkedList<(string Id, StoredBody Body)> _bodies = [];

    /// <summary>Keeps a body, replacing one kept under the same id.</summary>
    public void Add(string id, StoredBody body)
    {
        lock (_lock)
        {
            Remove(id);
            _bodies.AddFirst((id, body));
            var total = _bodies.Sum(entry => (long)entry.Body.Content.Length);
            while (_bodies.Count > 1 && (total > MaxBytes || _bodies.Count > MaxCount))
            {
                total -= _bodies.Last!.Value.Body.Content.Length;
                _bodies.RemoveLast();
            }
        }
    }

    public StoredBody? Find(string id)
    {
        lock (_lock)
        {
            return _bodies.FirstOrDefault(entry => entry.Id == id).Body;
        }
    }

    private void Remove(string id)
    {
        for (var node = _bodies.First; node is not null; node = node.Next)
        {
            if (node.Value.Id == id)
            {
                _bodies.Remove(node);
                return;
            }
        }
    }
}

/// <summary>A response body as received, with the file name suggested for it.</summary>
public sealed record StoredBody(byte[] Content, string FileName);
