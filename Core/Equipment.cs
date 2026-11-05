namespace DotARPG.Core;

public enum GearSlot { Weapon, Armor, Helmet, Gloves, Boots, Charm }
public sealed record GearItem(GearSlot Slot, string Name, int Tier, int Attack, int Defense, string Source)
{
    public int RequiredLevel => Tier switch { 2 => 5, 3 => 9, _ => 1 };
}

public sealed class EquipmentLoadout
{
    private readonly Dictionary<GearSlot, GearItem> slots;
    public EquipmentLoadout(WorldStyle style) => slots = Enum.GetValues<GearSlot>().ToDictionary(s => s, s => EquipmentCatalog.Get(style, s, 0, "初期装備"));
    public GearItem this[GearSlot slot] => slots[slot];
    public int Attack => slots.Values.Sum(i => i.Attack);
    public int Defense => slots.Values.Sum(i => i.Defense);
    public bool Equip(GearItem item, int heroLevel = int.MaxValue)
    {
        if (heroLevel < item.RequiredLevel || item.Tier <= slots[item.Slot].Tier) return false;
        slots[item.Slot] = item;
        return true;
    }
}

public static class EquipmentCatalog
{
    public static int Price(GearSlot slot, int tier) => tier == 1
        ? slot is GearSlot.Weapon or GearSlot.Armor ? 14 : 8
        : slot is GearSlot.Weapon or GearSlot.Armor ? 42 : 25;

    public static GearItem Get(WorldStyle style, GearSlot slot, int tier, string source)
    {
        tier = Math.Clamp(tier, 0, 3);
        string[] weapons = style switch
        {
            WorldStyle.Japanese => ["錆びた脇差", "鉄打ちの太刀", "銀打ちの太刀", "鬼断ちの神刀"],
            WorldStyle.Western => ["旅人の短剣", "鋼の長剣", "ミスリルソード", "竜断ちの聖剣"],
            WorldStyle.Chinese => ["古びた柳葉刀", "青鋼の剣", "玄鉄の剣", "龍淵・斬妖剣"],
            _ => ["欠けた曲刀", "鋼の曲刀", "月銀の曲刀", "暁の王剣"]
        };
        string finalArmor = style switch
        {
            WorldStyle.Japanese => "護国の鎧", WorldStyle.Western => "竜鱗の鎧",
            WorldStyle.Chinese => "玄武の仙甲", _ => "魔神避けの胴衣"
        };
        string[] names = slot switch
        {
            GearSlot.Weapon => weapons,
            GearSlot.Armor => ["旅装", "革のアーマー", "鋼のアーマー", finalArmor],
            GearSlot.Helmet => ["なし", "革のヘルム", "鋼のヘルム", "星守のヘルム"],
            GearSlot.Gloves => ["なし", "革のグローブ", "鋼のグローブ", "雷紋のグローブ"],
            GearSlot.Boots => ["旅人の靴", "革のブーツ", "鋼のブーツ", "風走りのブーツ"],
            _ => ["なし", "木守りの護符", "銀の護符", "封魔の護符"]
        };
        int attack = slot switch
        {
            GearSlot.Weapon => new[] { 2, 8, 18, 65 }[tier],
            GearSlot.Gloves => new[] { 0, 1, 2, 4 }[tier],
            GearSlot.Charm when tier == 3 => 2,
            _ => 0
        };
        int defense = slot switch
        {
            GearSlot.Armor => new[] { 1, 5, 9, 18 }[tier],
            GearSlot.Helmet => new[] { 0, 2, 4, 8 }[tier],
            GearSlot.Gloves => new[] { 0, 1, 2, 5 }[tier],
            GearSlot.Boots => new[] { 1, 2, 3, 5 }[tier],
            GearSlot.Charm => new[] { 0, 2, 3, 4 }[tier],
            _ => 0
        };
        return new(slot, names[tier], tier, attack, defense, source);
    }
}

public static class CombatRules
{
    public static int Damage(int attack, int defense, int variance = 0) => Math.Max(1, attack - defense + variance);

    public static int IncomingDamage(Creature enemy, int heroLevel, int defense, int variance = 0)
    {
        // Normal enemies judge missing armor against the hero's own level, so a modest
        // level gap does not amplify both the armor penalty and the level penalty.
        int requiredDefense = enemy.Kind is EnemyKind.Boss or EnemyKind.Midboss
            ? enemy.RequiredDefense : Math.Min(enemy.RequiredDefense, 2 + heroLevel * 2);
        int armorShortfall = Math.Max(0, requiredDefense - defense);
        int levelGap = Math.Max(0, enemy.Level - heroLevel);
        // Missing armor is increasingly costly; levels cannot substitute for boss equipment.
        int impact = Damage(enemy.Attack, defense, variance) + 2 * armorShortfall * armorShortfall;
        if (levelGap < 3) return impact;
        // A fair-level loadout takes roughly 50% HP at +4 and 80% HP at +6.
        double fraction = levelGap >= 7 ? 1.1 + (levelGap - 7) * .3 : .35 + (levelGap - 3) * .15;
        double armorFactor = Math.Clamp(1 + (requiredDefense - defense) * .02, .75, 1.5);
        int maximumHealth = 75 + (heroLevel - 1) * 6;
        return Math.Max(impact, (int)Math.Ceiling(maximumHealth * fraction * armorFactor) + variance);
    }
}
