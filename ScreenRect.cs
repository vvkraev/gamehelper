namespace GameHelper;

/// <summary>Прямоугольная область в экранных координатах.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public (int X, int Y) Center => (X + Math.Max(Width, 1) / 2, Y + Math.Max(Height, 1) / 2);

    public bool ContainsPoint(int x, int y, int inset = 0)
    {
        var w = Math.Max(Width, 1);
        var h = Math.Max(Height, 1);
        var minX = X + Math.Max(0, inset);
        var minY = Y + Math.Max(0, inset);
        var maxX = X + w - 1 - Math.Max(0, inset);
        var maxY = Y + h - 1 - Math.Max(0, inset);
        if (maxX < minX) (minX, maxX) = ((X + X + w - 1) / 2, (X + X + w - 1) / 2);
        if (maxY < minY) (minY, maxY) = ((Y + Y + h - 1) / 2, (Y + Y + h - 1) / 2);
        return x >= minX && x <= maxX && y >= minY && y <= maxY;
    }

    /// <summary>
    /// Точка внутри области: центр прямоугольника, сдвинутый от краёв на <paramref name="inset"/> px, если ширина/высота это позволяют.
    /// Перед Ctrl+Alt+C курсор ставится сюда (отдельно от точки ЛКМ).
    /// </summary>
    public (int X, int Y) GetInteriorPoint(int inset = 1)
    {
        var (minX, maxX, minY, maxY) = GetInteriorBounds(inset);
        return ((minX + maxX) / 2, (minY + maxY) / 2);
    }

    /// <summary>
    /// Случайная точка внутри области (с отступом от краёв как у <see cref="GetInteriorPoint"/>).
    /// Если задан <paramref name="centerAreaFraction"/>, выбор ограничивается центральной частью прямоугольника:
    /// например 0.8 означает "внутри центральных 80% по ширине и высоте", чтобы не кликать у краёв.
    /// </summary>
    public (int X, int Y) GetRandomInteriorPoint(int inset = 1, Random? rng = null, double centerAreaFraction = 1.0)
    {
        rng ??= Random.Shared;
        var (minX, maxX, minY, maxY) = GetInteriorBounds(inset);
        if (centerAreaFraction < 1.0)
        {
            if (centerAreaFraction <= 0)
                centerAreaFraction = 0.01;

            var cx = (minX + maxX) / 2;
            var cy = (minY + maxY) / 2;
            var halfW = (maxX - minX) / 2.0;
            var halfH = (maxY - minY) / 2.0;

            var shrunkHalfW = halfW * centerAreaFraction;
            var shrunkHalfH = halfH * centerAreaFraction;

            minX = Math.Max(minX, (int)Math.Ceiling(cx - shrunkHalfW));
            maxX = Math.Min(maxX, (int)Math.Floor(cx + shrunkHalfW));
            minY = Math.Max(minY, (int)Math.Ceiling(cy - shrunkHalfH));
            maxY = Math.Min(maxY, (int)Math.Floor(cy + shrunkHalfH));

            if (minX > maxX) (minX, maxX) = (cx, cx);
            if (minY > maxY) (minY, maxY) = (cy, cy);
        }

        return (rng.Next(minX, maxX + 1), rng.Next(minY, maxY + 1));
    }

    private (int minX, int maxX, int minY, int maxY) GetInteriorBounds(int inset)
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

        return (minX, maxX, minY, maxY);
    }

    /// <summary>Разбивает область на <paramref name="cols"/>×<paramref name="rows"/> ячеек (остаток пикселей распределяется по последним столбцам/строкам).</summary>
    public static List<ScreenRect> SplitIntoGrid(ScreenRect region, int cols, int rows)
    {
        if (cols < 1 || rows < 1)
            throw new ArgumentOutOfRangeException();

        var list = new List<ScreenRect>(cols * rows);
        for (var r = 0; r < rows; r++)
        {
            var y0 = region.Y + r * region.Height / rows;
            var y1 = region.Y + (r + 1) * region.Height / rows;
            for (var c = 0; c < cols; c++)
            {
                var x0 = region.X + c * region.Width / cols;
                var x1 = region.X + (c + 1) * region.Width / cols;
                list.Add(new ScreenRect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0)));
            }
        }

        return list;
    }
}
