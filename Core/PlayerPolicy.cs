namespace DotARPG.Core;

public enum PlayerTemperament { Careful, Balanced, Reckless, Meticulous }

// Private decision parameters, never shown in the spectator UI.
public sealed record PlayerPolicy(int PreparationMargin, double PotionRatio, double RetreatRatio,
    double RestRatio, double DodgeChance, int RiskTolerance, float ApproachSpeed = .48f, int AttackCooldown = 9)
{
    public static PlayerPolicy For(PlayerTemperament temperament) => temperament switch
    {
        PlayerTemperament.Careful => new(1, .65, .45, .72, .84, 0, .44f, 10),
        PlayerTemperament.Meticulous => new(1, .60, .42, .68, .81, 0, .46f, 9),
        PlayerTemperament.Reckless => new(0, .48, .28, .48, .77, 1, .66f, 6),
        _ => new(0, .52, .33, .57, .78, 0)
    };
}
