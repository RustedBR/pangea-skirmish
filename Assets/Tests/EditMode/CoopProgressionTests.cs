using NUnit.Framework;
using PangeaSkirmish;

/// <summary>GDD-024: pontos por nível, XP linear, dinheiro, loja e migração dos personagens antigos.</summary>
public class CoopProgressionTests
{
    private static CharacterPreset Char(float each, int level = 0) => new CharacterPreset
    {
        level = level,
        stats = new UnitStatBlock { STR = each, VIT = each, DEX = each, AGI = each, INT = each, WIS = each },
    };

    [Test]
    public void Budget_15_Plus5PerLevel()
    {
        Assert.AreEqual(15, PlayerProfile.BudgetFor(Char(2)), "nível 0/1 = 15 pontos");
        Assert.AreEqual(15, PlayerProfile.BudgetFor(Char(2, 1)));
        Assert.AreEqual(30, PlayerProfile.BudgetFor(Char(5, 4)), "nível 4 = os 30 de antes");
    }

    [Test]
    public void OldCharacter_30Points_MigratesToLevel4_AndStaysValid()
    {
        var c = Char(5);   // 30 pontos, sem nível gravado
        PlayerProfile.MigrateLevel(c);
        Assert.AreEqual(4, c.level);
        Assert.IsTrue(PlayerProfile.IsValid(c), PlayerProfile.WhyInvalid(c));
    }

    [Test]
    public void LevelUp_LeavesPointsToSpend()
    {
        var c = Char(2.5f, 1);   // 15 pontos
        Assert.IsTrue(PlayerProfile.IsValid(c), PlayerProfile.WhyInvalid(c));
        int ups = PlayerProfile.AddXp(c, 20);
        Assert.AreEqual(1, ups);
        Assert.AreEqual(2, c.level);
        Assert.AreEqual(0, c.xp);
        StringAssert.Contains("5 pontos para distribuir", PlayerProfile.WhyInvalid(c));
    }

    [Test]
    public void XpCurve_Linear_AndRewardsGrowWithLayer()
    {
        Assert.AreEqual(20, PlayerProfile.XpToNext(1));
        Assert.AreEqual(60, PlayerProfile.XpToNext(3));
        var c = Char(2, 1);
        Assert.AreEqual(2, PlayerProfile.AddXp(c, 20 + 40 + 5), "20 + 40 sobe dois níveis, sobra 5");
        Assert.AreEqual(3, c.level); Assert.AreEqual(5, c.xp);

        var (xp1, gold1) = RoundManager.CoopReward(1, 3, cleared: true);
        var (xp3, gold3) = RoundManager.CoopReward(3, 3, cleared: true);
        Assert.AreEqual(3 * 5 + 15, xp1);
        Assert.AreEqual(3 * xp1, xp3, "recompensa linear na camada");
        Assert.AreEqual(3 * 3, gold1);
        Assert.AreEqual(3 * gold1, gold3);
        Assert.AreEqual(3 * 5, RoundManager.CoopReward(1, 3, cleared: false).xp, "derrota: só as feras abatidas");
    }

    [Test]
    public void DayNight_BeastsStrongerAtNight()
    {
        Assert.AreEqual(1f, DayNight.BeastMult(DayNight.Day));
        Assert.Greater(DayNight.BeastMult(DayNight.Night), DayNight.BeastMult(DayNight.Dusk));
        Assert.Greater(DayNight.BeastMult(DayNight.Dusk), 1f);
    }

    [Test]
    public void AboveLevel2_OnlyAddsPoints()
    {
        var lvl2 = Char(2.5f, 2);
        var free = PlayerProfile.Clone(lvl2); free.weaponId = "ShortBow"; free.stats.STR -= 0.5f; free.stats.VIT += 0.5f;
        Assert.AreEqual("", PlayerProfile.WhyEditBlocked(lvl2, free), "nível 2: edição livre");

        var lvl3 = Char(2.5f, 3); lvl3.weaponId = "IronSword"; lvl3.presetName = "Veterano";
        var add = PlayerProfile.Clone(lvl3); add.stats.STR += 5f;
        Assert.AreEqual("", PlayerProfile.WhyEditBlocked(lvl3, add), "somar pontos vale");
        var respec = PlayerProfile.Clone(lvl3); respec.stats.STR -= 1f; respec.stats.VIT += 1f;
        StringAssert.Contains("não voltam", PlayerProfile.WhyEditBlocked(lvl3, respec));
        var swap = PlayerProfile.Clone(lvl3); swap.weaponId = "ShortBow";
        StringAssert.Contains("só dá para adicionar", PlayerProfile.WhyEditBlocked(lvl3, swap));
    }
}
