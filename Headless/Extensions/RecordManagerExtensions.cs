using FrooxEngine;

namespace Headless.Extensions;

public static class RecordManagerExtensions
{
    public static async Task WaitForPendingUploadsAsync
    (
        this RecordManager recordManager,
        TimeSpan? delay = null,
        CancellationToken ct = default
    )
    {
        delay ??= TimeSpan.FromMilliseconds(100);

        while (recordManager.SyncingRecordsCount > 0 || recordManager.UploadingVariantsCount > 0)
        {
            ct.ThrowIfCancellationRequested();

            await Task.Delay(delay.Value, ct);
        }
    }
}
