using Dynypost.Application.Captures;
using Dynypost.Application.Common;
using Dynypost.Application.Environments;

namespace Dynypost.Application.Tests.Captures;

public class CapturedValuesTests
{
    /// <summary>A store that yields between reading and writing, as a file store does.</summary>
    private sealed class SlowStore : ICapturedValueStore
    {
        private IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _values =
            new Dictionary<string, IReadOnlyDictionary<string, string>>();

        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Values => _values;

        public async Task<Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>> LoadAsync(
            string root,
            CancellationToken cancellationToken = default)
        {
            var snapshot = _values;
            await Task.Delay(20, cancellationToken);
            return Result<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>.Success(snapshot);
        }

        public async Task<Result<Unit>> SaveAsync(
            string root,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> values,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(5, cancellationToken);
            _values = values;
            return Unit.Value;
        }
    }

    [Fact]
    public async Task ConcurrentUpdates_KeepEveryValue()
    {
        var store = new SlowStore();

        await Task.WhenAll(Enumerable.Range(0, 5).Select(i => CapturedValues.UpdateAsync(
            store,
            "/work",
            "dev.yaml",
            values => new Dictionary<string, string>(values) { [$"v{i}"] = i.ToString() },
            CancellationToken.None)));

        Assert.Equal(5, store.Values["dev.yaml"].Count);
    }
}
