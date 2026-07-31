using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using PangeaSkirmish;

/// <summary>
/// Regressões da ROTAÇÃO DE VISTA (Q/E), que é reindexação lógica de coordenadas
/// e não rotação física — ver GridManager._viewOrientation.
///
/// Cada teste aqui cobre um defeito real encontrado na auditoria de 2026-07-29.
/// </summary>
[TestFixture]
public class ViewOrientationTests
{
    private static readonly int[] Orientations = { 0, 90, 180, 270 };

    /// <summary>
    /// Cena limpa antes de cada teste. Necessário porque outros fixtures do projeto
    /// (GridManagerTests.InsertLine_*) lançam exceção ANTES do próprio cleanup e
    /// deixam GridManager órfãos na cena — o que tornaria estes testes dependentes
    /// da ordem de execução.
    /// </summary>
    [SetUp]
    public void ClearStrayGrids()
    {
        foreach (var stray in Object.FindObjectsByType<GridManager>(FindObjectsSortMode.None))
            Object.DestroyImmediate(stray.gameObject);
    }

    private static GridManager MakeGrid(int width, int height)
    {
        var go = new GameObject("Grid");
        var grid = go.AddComponent<GridManager>();
        grid.width = width;
        grid.height = height;
        grid.iso = ScriptableObject.CreateInstance<IsoConfig>();
        return grid;
    }

    /// <summary>
    /// O picking do mouse (WorldToCell) tem de ser a inversa exata de CellToWorld
    /// em TODAS as orientações — se não for, o jogador clica num tile e o jogo
    /// registra outro.
    /// </summary>
    [Test]
    public void WorldToCell_RoundTripsInEveryOrientation()
    {
        // Arrange
        var grid = MakeGrid(10, 10);

        foreach (int deg in Orientations)
        {
            grid.SetViewOrientation(deg);

            for (int x = 0; x < grid.width; x++)
            for (int y = 0; y < grid.height; y++)
            {
                var cell = new Vector2Int(x, y);

                // Act
                var world = grid.CellToWorld(cell);
                var back = grid.WorldToCell(world);

                // Assert
                Assert.AreEqual(cell, back, $"round-trip falhou em {deg}° na célula {cell}");
            }
        }

        // Cleanup
        Object.DestroyImmediate(grid.gameObject);
    }

    /// <summary>
    /// Regressão do bug de grid retangular: ApplyOrientation usava `width - 1`
    /// para os DOIS eixos, então num mapa 20×12 girado 90° a maioria das células
    /// caía fora da faixa válida — o mapa dobrava sobre si mesmo e o picking
    /// errava sistematicamente. Mapas não-quadrados são possíveis porque
    /// GridManager.InsertLine insere coluna OU linha de forma independente.
    /// </summary>
    [Test]
    public void WorldToCell_RoundTripsOnRectangularGrid()
    {
        // Arrange
        var grid = MakeGrid(20, 12);

        foreach (int deg in Orientations)
        {
            grid.SetViewOrientation(deg);

            for (int x = 0; x < grid.width; x++)
            for (int y = 0; y < grid.height; y++)
            {
                var cell = new Vector2Int(x, y);

                // Act
                var back = grid.WorldToCell(grid.CellToWorld(cell));

                // Assert
                Assert.AreEqual(cell, back, $"grid 20x12 quebrou em {deg}° na célula {cell}");
            }
        }

        // Cleanup
        Object.DestroyImmediate(grid.gameObject);
    }

    /// <summary>
    /// A reindexação é uma PERMUTAÇÃO: girar não pode criar buraco nem colocar
    /// duas células no mesmo ponto. Se o conjunto de posições físicas mudar de
    /// tamanho, há sobreposição — foi assim que o mapa "dobrava".
    /// </summary>
    [Test]
    public void CellToWorld_IsAPermutationInEveryOrientation()
    {
        // Arrange
        var grid = MakeGrid(20, 12);
        int expected = grid.width * grid.height;

        foreach (int deg in Orientations)
        {
            grid.SetViewOrientation(deg);

            // Act
            var occupied = new HashSet<Vector2Int>();
            for (int x = 0; x < grid.width; x++)
            for (int y = 0; y < grid.height; y++)
            {
                var w = grid.CellToWorld(new Vector2Int(x, y));
                // quantiza para comparar posições de ponto flutuante com segurança
                occupied.Add(new Vector2Int(
                    Mathf.RoundToInt(w.x * 100f),
                    Mathf.RoundToInt(w.z * 100f)));
            }

            // Assert
            Assert.AreEqual(expected, occupied.Count,
                $"em {deg}° duas células caíram na mesma posição física");
        }

        // Cleanup
        Object.DestroyImmediate(grid.gameObject);
    }

    /// <summary>
    /// O centro do tabuleiro é o ponto fixo da permutação: o overview de fim de
    /// round deve mirar o mesmo lugar em qualquer orientação. Antes o valor era
    /// cacheado uma vez no Setup do RoundManager e ficava obsoleto após Q/E.
    /// </summary>
    [Test]
    public void WorldCenter_IsInvariantAcrossOrientations()
    {
        // Arrange
        var grid = MakeGrid(20, 20);
        grid.SetViewOrientation(0);
        var reference = grid.WorldCenter;

        foreach (int deg in Orientations)
        {
            // Act
            grid.SetViewOrientation(deg);
            var center = grid.WorldCenter;

            // Assert
            Assert.AreEqual(reference.x, center.x, 0.001f, $"centro X mudou em {deg}°");
            Assert.AreEqual(reference.z, center.z, 0.001f, $"centro Z mudou em {deg}°");
        }

        // Cleanup
        Object.DestroyImmediate(grid.gameObject);
    }

    /// <summary>
    /// Girar 4× em 90° tem de voltar exatamente ao estado inicial.
    /// </summary>
    [Test]
    public void SetViewOrientation_FullTurnRestoresOriginalLayout()
    {
        // Arrange
        var grid = MakeGrid(20, 12);
        var cell = new Vector2Int(3, 7);
        grid.SetViewOrientation(0);
        var original = grid.CellToWorld(cell);

        // Act
        for (int i = 0; i < 4; i++)
            grid.SetGridRotation(true);

        // Assert
        Assert.AreEqual(0, grid.ViewOrientation, "quatro passos de 90° deveriam zerar a orientação");
        var after = grid.CellToWorld(cell);
        Assert.AreEqual(original.x, after.x, 0.001f);
        Assert.AreEqual(original.z, after.z, 0.001f);

        // Cleanup
        Object.DestroyImmediate(grid.gameObject);
    }

    /// <summary>
    /// Regressão do facing: uma volta completa de vista (4 × 90°) tem de devolver o
    /// personagem EXATAMENTE à pose inicial. Antes SetViewOrientation escrevia o
    /// facing de tela com valores absolutos assumindo que toda unidade encarava NE,
    /// então a unidade nascia em SE e terminava em NE depois de girar 360°.
    /// </summary>
    [Test]
    public void Unit_FullViewTurnRestoresOriginalFacing()
    {
        // Arrange
        var go = new GameObject("UnidadeTeste");
        var unit = go.AddComponent<Unit>();
        int inicial = unit.ScreenFacingQuadrant;
        int logicoInicial = unit.FacingQuadrant;

        // Act + Assert — no meio da volta a pose TEM de mudar (senão o teste não prova nada)
        var vistos = new List<int>();
        for (int i = 1; i <= 4; i++)
        {
            unit.SetViewOrientation(i * 90);
            vistos.Add(unit.ScreenFacingQuadrant);
            Assert.AreEqual(logicoInicial, unit.FacingQuadrant,
                "a direção LÓGICA não pode mudar por causa da vista");
        }

        Assert.AreEqual(4, new HashSet<int>(vistos).Count,
            "cada quarto de volta deveria mostrar um quadrante de tela diferente");
        Assert.AreEqual(inicial, vistos[3],
            "após 360° o personagem deveria voltar à pose inicial");

        // Cleanup
        Object.DestroyImmediate(go);
    }

    /// <summary>
    /// A conversão CONTÍNUA é o que permite à câmera manter o mesmo pedaço de
    /// tabuleiro sob a tela ao girar. Ela precisa ser inversa exata inclusive em
    /// posições fracionárias (entre células) — arredondar daria um solavanco de
    /// até meia célula a cada Q/E, que é justamente o que se quer evitar.
    /// </summary>
    [Test]
    public void CellContinuous_RoundTripsAtFractionalPositions()
    {
        // Arrange
        var grid = MakeGrid(20, 12);
        var pontos = new[]
        {
            new Vector2(0f, 0f), new Vector2(3.5f, 7.25f),
            new Vector2(19f, 11f), new Vector2(9.5f, 5.5f), new Vector2(-2.75f, 14.1f),
        };

        foreach (int deg in Orientations)
        {
            grid.SetViewOrientation(deg);
            foreach (var p in pontos)
            {
                // Act
                var world = grid.CellContinuousToWorld(p);
                var back = grid.WorldToCellContinuous(world);

                // Assert
                Assert.AreEqual(p.x, back.x, 0.0001f, $"x divergiu em {deg}° no ponto {p}");
                Assert.AreEqual(p.y, back.y, 0.0001f, $"y divergiu em {deg}° no ponto {p}");
            }
        }

        // Cleanup
        Object.DestroyImmediate(grid.gameObject);
    }

    /// <summary>
    /// Regressão do GridAnchored: um objeto solto preso a uma célula lógica
    /// (ghost de alcance, waypoint, overlay de fogo) tem de acompanhar a
    /// reindexação. Antes eles ficavam desenhados no slot da orientação antiga.
    /// </summary>
    [Test]
    public void GridAnchored_FollowsCellAcrossOrientationChange()
    {
        // Arrange
        var grid = MakeGrid(20, 20);
        var cell = new Vector2Int(2, 14);
        var ghost = new GameObject("GhostDeTeste");
        GridAnchored.Attach(ghost, cell, 1);
        grid.SetViewOrientation(0);

        foreach (int deg in Orientations)
        {
            // Act
            grid.SetViewOrientation(deg);
            var expected = grid.AnchorToWorldCenter(cell, 1);

            // Assert
            Assert.AreEqual(expected.x, ghost.transform.position.x, 0.001f,
                $"ghost não acompanhou a rotação para {deg}° (eixo X)");
            Assert.AreEqual(expected.z, ghost.transform.position.z, 0.001f,
                $"ghost não acompanhou a rotação para {deg}° (eixo Z)");
        }

        // Cleanup
        Object.DestroyImmediate(ghost);
        Object.DestroyImmediate(grid.gameObject);
    }
}
