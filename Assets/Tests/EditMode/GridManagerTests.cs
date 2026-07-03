using NUnit.Framework;
using UnityEngine;
using PangeaSkirmish;

[TestFixture]
public class GridManagerTests
{
    [Test]
    public void CellToWorld_ReturnsCorrectPosition()
    {
        // Arrange
        var go = new GameObject("Grid");
        var grid = go.AddComponent<GridManager>();
        grid.width = 10;
        grid.height = 10;
        grid.iso = ScriptableObject.CreateInstance<IsoConfig>();

        // Act
        var worldPos = grid.CellToWorld(new Vector2Int(0, 0));

        // Assert
        Assert.AreEqual(0f, worldPos.x, 0.001f);
        Assert.AreEqual(0f, worldPos.y, 0.001f);

        // Cleanup
        Object.DestroyImmediate(go);
    }

    [Test]
    public void CellToWorld_MovesCorrectlyOnGrid()
    {
        // Arrange
        var go = new GameObject("Grid");
        var grid = go.AddComponent<GridManager>();
        grid.width = 10;
        grid.height = 10;
        grid.iso = ScriptableObject.CreateInstance<IsoConfig>();

        // Act
        var pos00 = grid.CellToWorld(new Vector2Int(0, 0));
        var pos10 = grid.CellToWorld(new Vector2Int(1, 0));
        var pos01 = grid.CellToWorld(new Vector2Int(0, 1));

        // Assert - Moving right in col should increase x
        Assert.Greater(pos10.x, pos00.x);
        // Moving down in row should decrease y
        Assert.Less(pos01.y, pos00.y);

        // Cleanup
        Object.DestroyImmediate(go);
    }

    [Test]
    public void FootprintsOverlap_ReturnsTrue_WhenOverlapping()
    {
        // Arrange
        var a = new Vector2Int(0, 0);
        var b = new Vector2Int(1, 1);

        // Act
        bool overlap = GridManager.FootprintsOverlap(a, 3, b, 3);

        // Assert
        Assert.IsTrue(overlap);
    }

    [Test]
    public void FootprintsOverlap_ReturnsFalse_WhenSeparated()
    {
        // Arrange
        var a = new Vector2Int(0, 0);
        var b = new Vector2Int(10, 10);

        // Act
        bool overlap = GridManager.FootprintsOverlap(a, 3, b, 3);

        // Assert
        Assert.IsFalse(overlap);
    }

    [Test]
    public void FootprintGap_ReturnsCorrectDistance()
    {
        // Arrange
        var a = new Vector2Int(0, 0);
        var b = new Vector2Int(3, 3);

        // Act
        int gap = GridManager.FootprintGap(a, 1, b, 1);

        // Assert
        Assert.AreEqual(3, gap);
    }

    [Test]
    public void FootprintGap_ReturnsZero_WhenSamePosition()
    {
        // Arrange
        var a = new Vector2Int(5, 5);
        var b = new Vector2Int(5, 5);

        // Act
        int gap = GridManager.FootprintGap(a, 3, b, 3);

        // Assert
        Assert.AreEqual(0, gap);
    }

    [Test]
    public void WorldToCell_RoundTripsCorrectly()
    {
        // Arrange
        var go = new GameObject("Grid");
        var grid = go.AddComponent<GridManager>();
        grid.width = 10;
        grid.height = 10;
        grid.iso = ScriptableObject.CreateInstance<IsoConfig>();

        var originalCell = new Vector2Int(3, 5);

        // Act
        var worldPos = grid.CellToWorld(originalCell);
        var backToCell = grid.WorldToCell(worldPos);

        // Assert
        Assert.AreEqual(originalCell.x, backToCell.x);
        Assert.AreEqual(originalCell.y, backToCell.y);

        // Cleanup
        Object.DestroyImmediate(go);
    }

    // ── PHYSICAL FOLD (GRID EXPAND) ────────────────

    [Test]
    public void InsertLine_ColumnInsert_IncreasesWidth()
    {
        // Arrange
        var go = new GameObject("Grid");
        var grid = go.AddComponent<GridManager>();
        grid.width = 10;
        grid.height = 8;
        grid.iso = ScriptableObject.CreateInstance<IsoConfig>();
        grid.Build();

        // Act
        var map = grid.InsertLine(column: true, index: 5, copyFrom: 3);

        // Assert
        Assert.AreEqual(11, map.width,  "Width deve aumentar em 1");
        Assert.AreEqual(8,  map.height, "Height não deve mudar");
        Assert.IsNotNull(map.tileIndices);
        Assert.AreEqual(11 * 8, map.tileIndices.Length);

        // Cleanup
        Object.DestroyImmediate(go);
    }

    [Test]
    public void InsertLine_RowInsert_IncreasesHeight()
    {
        // Arrange
        var go = new GameObject("Grid");
        var grid = go.AddComponent<GridManager>();
        grid.width = 10;
        grid.height = 8;
        grid.iso = ScriptableObject.CreateInstance<IsoConfig>();
        grid.Build();

        // Act
        var map = grid.InsertLine(column: false, index: 4, copyFrom: 2);

        // Assert
        Assert.AreEqual(10, map.width,  "Width não deve mudar");
        Assert.AreEqual(9,  map.height, "Height deve aumentar em 1");
        Assert.IsNotNull(map.tileIndices);
        Assert.AreEqual(10 * 9, map.tileIndices.Length);

        // Cleanup
        Object.DestroyImmediate(go);
    }

    [Test]
    public void InsertLine_ColumnAtStart_ShiftsDataRight()
    {
        // Arrange
        var go = new GameObject("Grid");
        var grid = go.AddComponent<GridManager>();
        grid.width = 5;
        grid.height = 5;
        grid.iso = ScriptableObject.CreateInstance<IsoConfig>();
        grid.Build();

        // Act — insere coluna no índice 0 (início)
        var map = grid.InsertLine(column: true, index: 0, copyFrom: 0);

        // Assert — dados originais foram deslocados para a direita
        // célula (1, y) do novo map deve ter o mesmo índice que (0, y) do grid original
        for (int y = 0; y < 5; y++)
            Assert.AreEqual(grid.GetTileIndex(0, y), map.TileAt(1, y),
                $"Dado da posição (0,{y}) deve ter sido deslocado para (1,{y})");

        // Cleanup
        Object.DestroyImmediate(go);
    }

    [Test]
    public void InsertLine_ColumnAtEnd_AddsColumnAtRightEdge()
    {
        // Arrange
        var go = new GameObject("Grid");
        var grid = go.AddComponent<GridManager>();
        grid.width = 5;
        grid.height = 5;
        grid.iso = ScriptableObject.CreateInstance<IsoConfig>();
        grid.Build();

        // Act — insere coluna no índice 5 (final do grid 5x5)
        var map = grid.InsertLine(column: true, index: 5, copyFrom: 4);

        // Assert
        Assert.AreEqual(6, map.width);
        // Coluna 0-4 devem estar intactas
        for (int y = 0; y < 5; y++)
            Assert.AreEqual(grid.GetTileIndex(0, y), map.TileAt(0, y),
                $"Coluna 0 deve estar intacta após inserção no final");

        // Cleanup
        Object.DestroyImmediate(go);
    }

    [Test]
    public void InsertLine_NewRow_InsertsHeightData()
    {
        // Arrange
        var go = new GameObject("Grid");
        var grid = go.AddComponent<GridManager>();
        grid.width = 4;
        grid.height = 4;
        grid.iso = ScriptableObject.CreateInstance<IsoConfig>();
        grid.Build();

        // Act — insere linha no índice 2
        var map = grid.InsertLine(column: false, index: 2, copyFrom: 1);

        // Assert — linhas 0 e 1 intactas, linha 2 é nova, linha 3 veio da antiga 2
        Assert.AreEqual(5, map.height);
        Assert.AreEqual(grid.GetTileIndex(0, 0), map.TileAt(0, 0), "Canto (0,0) intacto");
        Assert.AreEqual(grid.GetTileIndex(0, 1), map.TileAt(0, 1), "Linha 1 intacta");
        Assert.AreEqual(grid.GetTileIndex(0, 2), map.TileAt(0, 3), "Antiga linha 2 deslocada para 3");

        // Cleanup
        Object.DestroyImmediate(go);
    }

    [Test]
    public void InsertLine_HeightDataPreservedDuringColumnInsert()
    {
        // Arrange
        var go = new GameObject("Grid");
        var grid = go.AddComponent<GridManager>();
        grid.width = 10;
        grid.height = 10;
        grid.iso = ScriptableObject.CreateInstance<IsoConfig>();
        grid.Build();

        // Act
        var map = grid.InsertLine(column: true, index: 3, copyFrom: 2);

        // Assert — heights preservados nas células originais
        for (int y = 0; y < 10; y++)
        {
            if (y >= grid.height) continue;
            Assert.AreEqual(grid.HeightAt(0, y), map.HeightAt(0, y),
                $"Height em (0,{y}) deve ser preservado");
        }
        Assert.IsNotNull(map.heights);
        Assert.AreEqual(11 * 10, map.heights.Length);

        // Cleanup
        Object.DestroyImmediate(go);
    }

}
