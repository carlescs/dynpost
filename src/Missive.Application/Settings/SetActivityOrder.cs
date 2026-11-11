using Missive.Application.Common;

namespace Missive.Application.Settings;

/// <summary>Remembers the order the user arranged the activity bar's views in.</summary>
public sealed class SetActivityOrder(IAppSettingsStore store)
{
    public const string InvalidOrderCode = "settings.invalid_activity_order";

    /// <summary>More ids than any activity bar will have; a longer list is not an order but a mistake.</summary>
    public const int MaxActivities = 32;

    /// <param name="order">The views' ids, in order; an empty list restores the default order.</param>
    public async Task<Result<AppSettingsDto>> ExecuteAsync(
        IReadOnlyList<string>? order,
        CancellationToken cancellationToken = default)
    {
        order ??= [];
        if (order.Count > MaxActivities
            || order.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 64)
            || order.Distinct(StringComparer.Ordinal).Count() != order.Count)
        {
            return new Error(InvalidOrderCode, "The activity order must list distinct, non-empty view ids.");
        }

        var saved = await store.UpdateAsync(
            settings => settings.ActivityOrder is { } current && current.SequenceEqual(order)
                ? settings
                : settings with { ActivityOrder = order.Count == 0 ? null : [.. order] },
            cancellationToken);
        return saved.IsSuccess ? AppSettingsDto.From(saved.Value) : saved.Error!;
    }
}
