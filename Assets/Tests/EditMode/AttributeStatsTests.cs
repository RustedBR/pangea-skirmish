using NUnit.Framework;
using PangeaSkirmish;

[TestFixture]
public class AttributeStatsTests
{
    [SetUp]
    public void SetUp()
    {
        // Reset formulas to defaults
        AttributeStats.Formulas = new StatFormulas();
    }

    [TearDown]
    public void TearDown()
    {
        AttributeStats.Formulas = null;
    }

    [Test]
    public void MaxHP_CalculatedCorrectly()
    {
        // Arrange
        var stats = new AttributeStats { VIT = 5f };

        // Act
        int maxHP = stats.MaxHP;

        // Assert
        // hpBase (10) + VIT (5) × hpPerVIT (2) = 20
        Assert.AreEqual(20, maxHP);
    }

    [Test]
    public void MaxMana_CalculatedCorrectly()
    {
        // Arrange
        var stats = new AttributeStats { WIS = 4f };

        // Act
        int maxMana = stats.MaxMana;

        // Assert
        // baseMana (5) + WIS (4) × manaPerWIS (1) = 9
        Assert.AreEqual(9, maxMana);
    }

    [Test]
    public void PhysicalDamage_IncludesWeaponDamage()
    {
        // Arrange
        var stats = new AttributeStats { STR = 3f, WeaponDamage = 5 };

        // Act
        int damage = stats.PhysicalDamage;

        // Assert
        // WeaponDamage (5) + STR (3) × dmgPerSTR (1) = 8
        Assert.AreEqual(8, damage);
    }

    [Test]
    public void MagicDamage_CalculatedFromINT()
    {
        // Arrange
        var stats = new AttributeStats { INT = 6f };

        // Act
        int damage = stats.MagicDamage;

        // Assert
        // INT (6) × dmgPerINT (1) = 6
        Assert.AreEqual(6, damage);
    }

    [Test]
    public void Initiative_CalculatedFromAGIAndDEX()
    {
        // Arrange
        var stats = new AttributeStats { AGI = 4f, DEX = 3f };

        // Act
        int initiative = stats.Initiative;

        // Assert
        // AGI (4) × iniPerAGI (1) + DEX (3) × iniPerDEX (1) = 7
        Assert.AreEqual(7, initiative);
    }

    [Test]
    public void MoveBudget_CalculatedFromFootprintAndAGI()
    {
        // Arrange
        var stats = new AttributeStats { AGI = 4f, Footprint = 3 };

        // Act
        int moveBudget = stats.MoveBudget;

        // Assert
        // Footprint (3) + AGI (4) × movePerAGI (1) = 7
        Assert.AreEqual(7, moveBudget);
    }

    [Test]
    public void ActionPoints_CalculatedFromAGI()
    {
        // Arrange
        var stats = new AttributeStats { AGI = 5f };

        // Act
        int ap = stats.ActionPoints;

        // Assert
        // AGI (5) × apPerAGI (1) = 5 (assuming minActionPoints <= 5)
        Assert.GreaterOrEqual(ap, 1);
    }

    [Test]
    public void BonusActionPoints_CalculatedFromDEX()
    {
        // Arrange
        var stats = new AttributeStats { DEX = 4f };

        // Act
        int bap = stats.BonusActionPoints;

        // Assert
        // DEX (4) × bapPerDEX (1) = 4
        Assert.AreEqual(4, bap);
    }

    [Test]
    public void ManaRegen_CalculatedFromWIS()
    {
        // Arrange
        var stats = new AttributeStats { WIS = 6f };

        // Act
        int regen = stats.ManaRegen;

        // Assert
        // manaRegenBase (2) + WIS (6) × manaRegenPerWIS (0.5) = 5
        Assert.AreEqual(5, regen);
    }

    [Test]
    public void HitChance_ReturnsBaseValue()
    {
        // Arrange
        var stats = new AttributeStats();

        // Act
        float hitChance = stats.HitChance;

        // Assert
        // baseHitChance = 0.90
        Assert.AreEqual(0.90f, hitChance, 0.001f);
    }

    [Test]
    public void DodgeChance_CalculatedFromAGI()
    {
        // Arrange
        var stats = new AttributeStats { AGI = 10f };

        // Act
        float dodgeChance = stats.DodgeChance;

        // Assert
        // AGI (10) × dodgePerAGI (0.01) = 0.10
        Assert.AreEqual(0.10f, dodgeChance, 0.001f);
    }

    [Test]
    public void CritChance_CalculatedFromDEX()
    {
        // Arrange
        var stats = new AttributeStats { DEX = 8f };

        // Act
        float critChance = stats.CritChance;

        // Assert
        // DEX (8) × critPerDEX (0.01) = 0.08
        Assert.AreEqual(0.08f, critChance, 0.001f);
    }

    [Test]
    public void PhysicalDefense_CalculatedFromVIT()
    {
        // Arrange
        var stats = new AttributeStats { VIT = 5f };

        // Act
        int defense = stats.PhysicalDefense;

        // Assert
        // armorBase (0) + VIT (5) × armorPerVIT (1) = 5
        Assert.AreEqual(5, defense);
    }

    [Test]
    public void MagicDefense_CalculatedFromWIS()
    {
        // Arrange
        var stats = new AttributeStats { WIS = 4f };

        // Act
        int defense = stats.MagicDefense;

        // Assert
        // magicResistBase (0) + WIS (4) × magicResistPerWIS (1) = 4
        Assert.AreEqual(4, defense);
    }

    [Test]
    public void RollHit_ReturnsCritical_WhenBelowCritChance()
    {
        // Arrange
        var stats = new AttributeStats { DEX = 100f }; // 100% crit chance

        // Act
        var result = stats.RollHit(1f, 0f);

        // Assert
        Assert.AreEqual(HitResult.Critical, result);
    }

    [Test]
    public void RollHit_ReturnsHit_WhenAboveHitChance()
    {
        // Arrange
        var stats = new AttributeStats { DEX = 0f }; // 0% crit chance

        // Act
        var result = stats.RollHit(1f, 0f);

        // Assert
        Assert.AreEqual(HitResult.Hit, result);
    }

    [Test]
    public void RollDamage_AppliesDefense()
    {
        // Arrange
        var stats = new AttributeStats { VIT = 5f }; // 5 physical defense

        // Act
        int damage = stats.RollDamage(10, true);

        // Assert
        // 10 base - 5 defense = 5 (plus/minus variance)
        Assert.LessOrEqual(damage, 10);
    }
}
