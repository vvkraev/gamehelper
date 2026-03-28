namespace GameHelper;

/// <summary>Настройки UI, сохраняемые в <c>settings.json</c> в корне проекта.</summary>
public sealed class AppSettings
{
    public ScreenRect OrbRect { get; set; }
    public ScreenRect ItemRect { get; set; }

    /// <summary>Шаблон аффикса; строчная <c>n</c> — место для цифр, например <c>+n to all skill gem</c>.</summary>
    public string AffixPattern { get; set; } = "";

    /// <summary>Минимальное число из шаблона (когда в шаблоне есть <c>n</c>).</summary>
    public int MinRoll { get; set; }

    public int MouseActionDelayMs { get; set; } = 80;
    public int ClipboardDelayMs { get; set; } = 220;
    public int MaxOps { get; set; } = 20;

    public bool TraceInput { get; set; }
    public bool StepConfirm { get; set; }
}
