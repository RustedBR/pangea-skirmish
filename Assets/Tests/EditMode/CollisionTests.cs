using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PangeaSkirmish;

/// <summary>Testes da mecânica de colisão real (GDD-028 fase 2).</summary>
public class CollisionTests
{
    [Test]
    public void CollisionGrid_OccupyRelease_IsFootprintFree()
    {
        var c = new CollisionGrid();
        c.Configure(20, 20);
        Assert.IsTrue(c.IsFootprintFree(new Vector2Int(3, 3), 3));
        object who = new object();
        c.Occupy(new Vector2Int(3, 3), 3, who);
        Assert.IsFalse(c.IsFootprintFree(new Vector2Int(3, 3), 3));
        Assert.IsFalse(c.IsFootprintFree(new Vector2Int(4, 4), 3)); // sobreposição parcial
        Assert.IsTrue(c.IsFootprintFree(new Vector2Int(3, 3), 3, who)); // ignore = quem ocupa
        c.Release(new Vector2Int(3, 3), 3);
        Assert.IsTrue(c.IsFootprintFree(new Vector2Int(3, 3), 3));
    }

    [Test]
    public void CollisionGrid_Walkable_Blocks()
    {
        var c = new CollisionGrid();
        c.Configure(10, 10);
        Assert.IsTrue(c.IsWalkable(2, 2));
        c.SetWalkable(2, 2, false);
        Assert.IsFalse(c.IsWalkable(2, 2));
    }

    [Test]
    public void CollisionGrid_ZLevel_RampDelta()
    {
        var c = new CollisionGrid();
        c.Configure(10, 10);
        c.SetZLevel(2, 2, 1);
        c.SetZLevel(3, 2, 2);
        Assert.AreEqual(1, c.HeightDelta(2, 2, 3, 2)); // Δ<=1 = rampa jogável
        c.SetZLevel(4, 2, 5);
        Assert.AreEqual(3, c.HeightDelta(3, 2, 4, 2)); // Δ>1 = bloqueia
    }

    [Test]
    public void UnitBridge_RegistersAndClearsOccupancy()
    {
        var grid = new GameObject("g").AddComponent<GridManager>();
        grid.width = 20; grid.height = 20;
        grid.Build();
        var u = TestKit.Unit("U", new Vector2Int(2, 2));
        u.Init(grid, new Vector2Int(2, 2), Color.white, "");
        UnitCollisionBridge.SyncAll(grid);
        Assert.IsFalse(grid.Collision.IsFootprintFree(new Vector2Int(2, 2), u.stats.Footprint));
        Object.DestroyImmediate(u.gameObject);
        UnitCollisionBridge.SyncAll(grid);
        Assert.IsTrue(grid.Collision.IsFootprintFree(new Vector2Int(2, 2), 3));
        Object.DestroyImmediate(grid.gameObject);
    }

    [Test]
    public void Barrier_RegistersAsOccupant()
    {
        var grid = new GameObject("g").AddComponent<GridManager>();
        grid.width = 20; grid.height = 20;
        grid.Build();
        var field = new SpellField();
        var wall = new SpellZone { Kind = SpellEffect.Barrier, Caster = null, Name = "Muralha" };
        wall.Cells.Add(new Vector2Int(5, 5));   // Cells é readonly: usa Add
        field.Add(wall, grid);
        Assert.IsTrue(grid.Collision.IsOccupied(5, 5));
        Object.DestroyImmediate(grid.gameObject);
    }

    [Test]
    public void Grid_IsAnchorInBounds_RejectsNonWalkableCell()
    {
        var grid = new GameObject("g").AddComponent<GridManager>();
        grid.width = 20; grid.height = 20;
        grid.Build();
        grid.Collision.SetWalkable(5, 5, false);
        // Footprint 3x3 em (3,3) cobre (5,5) → inválido; em (10,10) → válido.
        Assert.IsFalse(grid.IsAnchorInBounds(new Vector2Int(3, 3), 3));
        Assert.IsTrue(grid.IsAnchorInBounds(new Vector2Int(10, 10), 3));
        Object.DestroyImmediate(grid.gameObject);
    }

    [Test]
    public void Grid_CanStepBetween_BlocksZJumpAndNonWalkable()
    {
        var grid = new GameObject("g").AddComponent<GridManager>();
        grid.width = 20; grid.height = 20;
        grid.Build();
        grid.Collision.SetZLevel(2, 2, 1);
        grid.Collision.SetZLevel(3, 2, 2);
        Assert.IsTrue(grid.CanStepBetween(new Vector2Int(2, 2), new Vector2Int(3, 2))); // Δ1 = rampa
        grid.Collision.SetZLevel(3, 2, 4);
        Assert.IsFalse(grid.CanStepBetween(new Vector2Int(2, 2), new Vector2Int(3, 2))); // Δ3 = parede
        grid.Collision.SetZLevel(3, 2, 1);
        grid.Collision.SetWalkable(3, 2, false);
        Assert.IsFalse(grid.CanStepBetween(new Vector2Int(2, 2), new Vector2Int(3, 2))); // chão sólido
        Object.DestroyImmediate(grid.gameObject);
    }
}