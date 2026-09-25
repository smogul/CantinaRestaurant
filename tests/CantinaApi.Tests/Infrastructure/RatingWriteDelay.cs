using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CantinaApi.Tests.Infrastructure;

// Test-only: pauses after a rating is written, so concurrent requests reliably overlap in the window where stats updates could be lost.
public sealed class RatingWriteDelay : DbCommandInterceptor
{
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        await PauseIfRatingWriteAsync(command, cancellationToken);
        return result;
    }

    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await PauseIfRatingWriteAsync(command, cancellationToken);
        return result;
    }

    private async Task PauseIfRatingWriteAsync(DbCommand command, CancellationToken cancellationToken)
    {
        var isRatingWrite = command.CommandText.Contains("INSERT INTO \"Ratings\"", StringComparison.Ordinal)
            || command.CommandText.Contains("UPDATE \"Ratings\"", StringComparison.Ordinal);

        if (Delay > TimeSpan.Zero && isRatingWrite)
        {
            await Task.Delay(Delay, cancellationToken);
        }
    }
}
