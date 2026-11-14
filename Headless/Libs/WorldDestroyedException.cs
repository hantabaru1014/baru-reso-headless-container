namespace Headless.Libs;

/// <summary>
/// ワールド上の処理を待っている間にワールドが破棄された
/// </summary>
public sealed class WorldDestroyedException : Exception
{
    public WorldDestroyedException(string? worldName)
        : base($"World {worldName} was destroyed before the operation completed")
    {
    }
}
