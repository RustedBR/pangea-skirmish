using NUnit.Framework;
using UnityEngine;
using PangeaSkirmish;

[TestFixture]
public class SpellBookTests
{
    private GameObject casterGo;
    private Unit caster;
    private GameTuning tuning;

    [SetUp]
    public void SetUp()
    {
        // Create test GameTuning
        tuning = ScriptableObject.CreateInstance<GameTuning>();
        tuning.spellMinDamage = 1;
        tuning.conduitAffinityBonus = 0.25f;
        tuning.spellRangePerMana = 1;
        tuning.spellRangeBase = 0;
        RuntimeTuning.Active = tuning;

        // Create caster
        casterGo = new GameObject("Caster");
        caster = casterGo.AddComponent<Unit>();
        caster.stats = new AttributeStats { INT = 4f, WIS = 3f, VIT = 2f };
        caster.team = 0;
    }

    [TearDown]
    public void TearDown()
    {
        RuntimeTuning.Active = null;
        Object.DestroyImmediate(casterGo);
    }

    [Test]
    public void Potency_FireElement_UsesINTAndVIT()
    {
        // Arrange
        var element = SpellElement.Fire;

        // Act
        int potency = SpellBook.Potency(caster, element, 1); // manaPower=1 (sem multiplicador)

        // Assert
        // Fire: INT (4) + VIT (2) = 6
        // Raw: 6 × 1 = 6
        Assert.AreEqual(6, potency);
    }

    [Test]
    public void Potency_WaterElement_UsesVITAndINT()
    {
        // Arrange
        var element = SpellElement.Water;

        // Act
        int potency = SpellBook.Potency(caster, element, 1);

        // Assert
        // Water: VIT (2) + INT (4) = 6
        // Raw: 6 × 1 = 6
        Assert.AreEqual(6, potency);
    }

    [Test]
    public void Potency_MagicElement_UsesINTAndWIS()
    {
        // Arrange
        var element = SpellElement.Magic;

        // Act
        int potency = SpellBook.Potency(caster, element, 1);

        // Assert
        // Magic: INT (4) + WIS (3) = 7
        // Raw: 7 × 1 = 7
        Assert.AreEqual(7, potency);
    }

    [Test]
    public void Potency_RespectsMinimumDamage()
    {
        // Arrange
        caster.stats = new AttributeStats { INT = 1f, WIS = 1f };
        var element = SpellElement.Magic;

        // Act
        int potency = SpellBook.Potency(caster, element, 1);

        // Assert
        // Raw: (1+1) × 1 = 2, min is 1, so potency=2 ≥ 1
        Assert.GreaterOrEqual(potency, tuning.spellMinDamage);
    }

    [Test]
    public void Potency_ScalesWithManaPower()
    {
        // Arrange
        caster.stats = new AttributeStats { INT = 4f, WIS = 3f };
        var element = SpellElement.Magic; // INT+WIS = 7

        // Act
        int pot1 = SpellBook.Potency(caster, element, 1);
        int pot3 = SpellBook.Potency(caster, element, 3);

        // Assert
        // pot1 = 7 × 1 = 7
        // pot3 = 7 × 3 = 21
        Assert.AreEqual(7, pot1);
        Assert.AreEqual(21, pot3);
    }

    [Test]
    public void SpellRange_DependsOnMana_WithoutConduit()
    {
        // Arrange
        caster.weaponId = null; // no conduit
        int mana = 5;

        // Act
        int range = SpellBook.SpellRange(caster, mana);

        // Assert
        // Range = mana × spellRangePerMana + 0 (no conduit)
        // = 5 × 1 = 5
        Assert.AreEqual(5, range);
    }

    [Test]
    public void ElementName_Fire_ReturnsCorrectName()
    {
        // Act
        string name = SpellBook.ElementName(SpellElement.Fire);

        // Assert
        Assert.AreEqual("Fogo", name);
    }

    [Test]
    public void ElementName_Water_ReturnsCorrectName()
    {
        // Act
        string name = SpellBook.ElementName(SpellElement.Water);

        // Assert
        Assert.AreEqual("Água", name);
    }

    [Test]
    public void ElementName_Air_ReturnsCorrectName()
    {
        // Act
        string name = SpellBook.ElementName(SpellElement.Air);

        // Assert
        Assert.AreEqual("Ar", name);
    }

    [Test]
    public void ElementName_Earth_ReturnsCorrectName()
    {
        // Act
        string name = SpellBook.ElementName(SpellElement.Earth);

        // Assert
        Assert.AreEqual("Terra", name);
    }

    [Test]
    public void ElementColor_Fire_ReturnsColor()
    {
        // Act
        Color color = SpellBook.ElementColor(SpellElement.Fire);

        // Assert
        Assert.AreNotEqual(Color.clear, color);
    }

    [Test]
    public void Potency_AirElement_UsesAGIAndINT()
    {
        // Arrange
        caster.stats = new AttributeStats { AGI = 5f, INT = 3f };
        var element = SpellElement.Air;

        // Act
        int potency = SpellBook.Potency(caster, element, 1);

        // Assert
        // Air: AGI (5) + INT (3) = 8
        // Raw: 8 × 1 = 8
        Assert.AreEqual(8, potency);
    }

    [Test]
    public void Potency_EarthElement_UsesVITAndSTR()
    {
        // Arrange
        caster.stats = new AttributeStats { VIT = 4f, STR = 3f };
        var element = SpellElement.Earth;

        // Act
        int potency = SpellBook.Potency(caster, element, 1);

        // Assert
        // Earth: VIT (4) + STR (3) = 7
        // Raw: 7 × 1 = 7
        Assert.AreEqual(7, potency);
    }
}
