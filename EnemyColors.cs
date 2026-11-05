using DotARPG.Core;

namespace DotARPG;

internal static class EnemyColors
{
    public static uint Get(EnemyKind kind) => kind switch
    {
        EnemyKind.Beast => 0xD7A66C,
        EnemyKind.Humanoid => 0xDCE27D,
        EnemyKind.Undead => 0xB69CDB,
        EnemyKind.Spirit => 0x73CECF,
        EnemyKind.Elite => 0xEE8066,
        EnemyKind.Midboss => 0xF3AA47,
        _ => 0xF46B9E
    };
    public static readonly (string Label, uint Color)[] Legend =
    [
        ("旅人", 0xFFF5D6), ("集落", 0xE6C17F), ("獣", Get(EnemyKind.Beast)),
        ("人型", Get(EnemyKind.Humanoid)), ("不死者", Get(EnemyKind.Undead)), ("妖魔", Get(EnemyKind.Spirit)),
        ("強敵", Get(EnemyKind.Elite)), ("中ボス", Get(EnemyKind.Midboss)), ("ボス", Get(EnemyKind.Boss))
    ];
}
