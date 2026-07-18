using GameHelper.Services;
using Xunit;

namespace GameHelper.Tests;

/// <summary>
/// Тесты маппинга 0-based cellIdx ↔ (col, row) 1-based в column-major раскладке.
/// Формула: col = cellIdx / gridRows + 1, row = cellIdx % gridRows + 1.
/// Стандартная витрина: 12 колонок × 12 строк = 144 ячейки.
/// Стандартный инвентарь: 12 колонок × 5 строк = 60 ячеек.
/// </summary>
public sealed class TabletGridMappingTests
{
    private static (int Col, int Row) Map(int cellIdx, int gridRows) =>
        TabletListingsIndex.CellIndexToColRow(cellIdx, gridRows);

    // ── 12×5 (стандартный инвентарь) ──────────────────────────────────────────

    [Fact]
    public void Inventory12x5_FirstCell_IsCol1Row1()
    {
        var (col, row) = Map(0, gridRows: 5);
        Assert.Equal(1, col);
        Assert.Equal(1, row);
    }

    [Fact]
    public void Inventory12x5_LastCellInFirstColumn_IsCol1Row5()
    {
        var (col, row) = Map(4, gridRows: 5);
        Assert.Equal(1, col);
        Assert.Equal(5, row);
    }

    [Fact]
    public void Inventory12x5_FirstCellInSecondColumn_IsCol2Row1()
    {
        var (col, row) = Map(5, gridRows: 5);
        Assert.Equal(2, col);
        Assert.Equal(1, row);
    }

    [Fact]
    public void Inventory12x5_LastCell_IsCol12Row5()
    {
        var (col, row) = Map(59, gridRows: 5);
        Assert.Equal(12, col);
        Assert.Equal(5, row);
    }

    // ── 12×12 (стандартная витрина Ange) ──────────────────────────────────────

    [Fact]
    public void Showcase12x12_FirstCell_IsCol1Row1()
    {
        var (col, row) = Map(0, gridRows: 12);
        Assert.Equal(1, col);
        Assert.Equal(1, row);
    }

    [Fact]
    public void Showcase12x12_LastCellInFirstColumn_IsCol1Row12()
    {
        var (col, row) = Map(11, gridRows: 12);
        Assert.Equal(1, col);
        Assert.Equal(12, row);
    }

    [Fact]
    public void Showcase12x12_FirstCellInSecondColumn_IsCol2Row1()
    {
        var (col, row) = Map(12, gridRows: 12);
        Assert.Equal(2, col);
        Assert.Equal(1, row);
    }

    [Fact]
    public void Showcase12x12_LastCell_IsCol12Row12()
    {
        var (col, row) = Map(143, gridRows: 12);
        Assert.Equal(12, col);
        Assert.Equal(12, row);
    }

    // ── Нестандартная сетка ────────────────────────────────────────────────────

    [Fact]
    public void NonStandard_4Rows_CellIndex7_IsCol2Row4()
    {
        // 4-строчная сетка: col=(7/4)+1=2, row=(7%4)+1=4
        var (col, row) = Map(7, gridRows: 4);
        Assert.Equal(2, col);
        Assert.Equal(4, row);
    }

    [Fact]
    public void NonStandard_1Row_AnyCell_RowAlways1()
    {
        for (var i = 0; i < 10; i++)
        {
            var (col, row) = Map(i, gridRows: 1);
            Assert.Equal(i + 1, col);
            Assert.Equal(1, row);
        }
    }

    // ── Полный обход 12×5 — без пропусков и дублей ────────────────────────────

    [Fact]
    public void Inventory12x5_AllCells_UniqueColRow()
    {
        var seen = new HashSet<(int, int)>();
        for (var i = 0; i < 60; i++)
        {
            var cr = Map(i, gridRows: 5);
            Assert.True(seen.Add(cr), $"Дубль {cr} при cellIdx={i}");
            Assert.InRange(cr.Col, 1, 12);
            Assert.InRange(cr.Row, 1, 5);
        }
        Assert.Equal(60, seen.Count);
    }
}
