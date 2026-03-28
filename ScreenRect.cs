namespace GameHelper;

/// <summary>Прямоугольная область в экранных координатах. Клик — в центре.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public (int X, int Y) Center => (X + Math.Max(Width, 1) / 2, Y + Math.Max(Height, 1) / 2);

    /// <summary>
    /// Точка внутри области: центр прямоугольника, сдвинутый от краёв на <paramref name="inset"/> px, если ширина/высота это позволяют.
    /// Перед Ctrl+C курсор ставится сюда (отдельно от точки ЛКМ).
    /// </summary>
    public (int X, int Y) GetInteriorPoint(int inset = 1)
    {
        var w = Math.Max(Width, 1);
        var h = Math.Max(Height, 1);
        var minX = X;
        var maxX = X + w - 1;
        var minY = Y;
        var maxY = Y + h - 1;
        if (w > 2 * inset)
        {
            minX = X + inset;
            maxX = X + w - 1 - inset;
        }

        if (h > 2 * inset)
        {
            minY = Y + inset;
            maxY = Y + h - 1 - inset;
        }

        return ((minX + maxX) / 2, (minY + maxY) / 2);
    }
}
