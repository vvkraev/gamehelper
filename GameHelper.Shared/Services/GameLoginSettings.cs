namespace GameHelper.Services;

/// <summary>
/// Настройки автоматического входа в игру после перезапуска клиента.
/// Используется в GameLoginService.
/// </summary>
public sealed class GameLoginSettings
{
    // ── Экран входа на сервер ─────────────────────────────────────────────────
    public ScreenRect? LoginScreenDetectRegion { get; set; }
    public string LoginScreenText { get; set; } = "LOG IN";
    public ScreenRect? LoginClickRegion { get; set; }

    // ── Экран выбора персонажа ────────────────────────────────────────────────
    public ScreenRect? CharSelectDetectRegion { get; set; }
    public string CharSelectText { get; set; } = "ENTER GAME";
    public ScreenRect? CharSelectClickRegion { get; set; }

    // ── Иконка домика (вход сразу в хайдаут) ─────────────────────────────────
    public ScreenRect? HideoutIconRegion { get; set; }         // область для сравнения с эталоном
    public ScreenRect? HideoutIconClickRegion { get; set; }    // клик если иконка найдена

    // ── Экран подтверждения хайдаута (появляется после клика иконки) ─────────
    public ScreenRect? HideoutConfirmDetectRegion { get; set; }
    public string HideoutConfirmText { get; set; } = "ENTER";
    public ScreenRect? HideoutConfirmClickRegion { get; set; }

    // ── Тайминги ──────────────────────────────────────────────────────────────
    public int LoginInitialWaitMs { get; set; } = 20_000;  // пауза после запуска до ожидания экрана входа
    public int LoginTimeoutMs { get; set; } = 60_000;      // таймаут на каждый шаг
    public int PostLoginWaitMs { get; set; } = 60_000;     // пауза после успешного входа до проверки локации
}
