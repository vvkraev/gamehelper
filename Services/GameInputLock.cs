namespace GameHelper.Services;

/// <summary>
/// Глобальный мьютекс ввода в игру: гарантирует, что одновременно только один сервис
/// (переоценка или авто-перековка) выполняет действия с мышью/клавиатурой в игре.
/// Захватывается перед каждой рабочей итерацией, освобождается по завершении.
/// </summary>
public static class GameInputLock
{
    private static readonly SemaphoreSlim _sem = new(1, 1);

    /// <summary>Ожидает получения мьютекса. Может быть отменено через <paramref name="ct"/>.</summary>
    public static Task WaitAsync(CancellationToken ct) => _sem.WaitAsync(ct);

    /// <summary>Освобождает мьютекс. Вызывать строго в finally-блоке после WaitAsync.</summary>
    public static void Release() => _sem.Release();
}
