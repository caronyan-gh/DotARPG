using System.Numerics;

namespace DotARPG.Core;

public enum EnemyKind { Beast, Humanoid, Undead, Spirit, Elite, Midboss, Boss }
public enum AttackShape { Circle, Sweep, Cross }
public enum BattlePhase { Approaching, Windup, Impact, Recovery }
public enum BattleEvent { None, HeroHit, HeroSpell, Dodged, HeroHurt }

public sealed class Battle
{
    public const float Width = 32, Height = 22;
    public Vector2 Hero { get; private set; } = new(9, 13);
    public Vector2 Enemy { get; private set; } = new(22, 10);
    public Vector2 PreviousHero { get; private set; }
    public Vector2 Target { get; private set; }
    public AttackShape Shape { get; private set; }
    public BattlePhase Phase { get; private set; }
    public int PhaseTick { get; private set; }
    public int TotalTicks { get; private set; }
    public int Flash { get; private set; }
    public bool Dashing { get; private set; }
    public bool Retreating { get; private set; }
    public bool Escaped => escapeProgress >= 18 + outclassed * 6;
    public bool Major { get; }
    public Creature Opponent { get; }
    public int AttackCount { get; private set; }
    public int SuccessfulDodges { get; private set; }
    public BattleEvent LastEvent { get; private set; }
    public int LastDamage { get; private set; }
    public float WindupProgress => Phase == BattlePhase.Windup ? PhaseTick / (float)WindupLength : 0;
    public int WindupLength => Math.Max(Major ? 12 : 8, (Major ? 20 : 12) - Math.Max(0, Opponent.Level - heroLevel));
    private int heroCooldown;
    private int enemyCooldown = 4;
    private Vector2 dashTarget;
    private bool attemptedDodge;
    private readonly Random random;
    private readonly PlayerPolicy policy;
    private readonly int heroLevel;
    private readonly float mobility;
    private readonly int outclassed;
    private int escapeProgress;

    public Battle(Creature opponent, Random random, PlayerPolicy? policy = null, int heroLevel = 1, float mobility = 0, bool ambushed = false,
        bool modernBalance = true)
    {
        Opponent = opponent;
        Major = opponent.Kind is EnemyKind.Midboss or EnemyKind.Boss;
        this.random = random;
        this.policy = policy ?? PlayerPolicy.For(PlayerTemperament.Balanced);
        this.heroLevel = heroLevel;
        this.mobility = mobility;
        outclassed = modernBalance ? Math.Max(0, opponent.Level - heroLevel - 2) : 0;
        if (ambushed) { Enemy = new Vector2(14, 12); enemyCooldown = 0; }
    }

    public BattleEvent Step(int attackPower, bool canCast, bool retreat = false)
    {
        TotalTicks++;
        PhaseTick++;
        if (Flash > 0) Flash--;
        if (heroCooldown > 0) heroCooldown--;
        if (enemyCooldown > 0) enemyCooldown--;
        Dashing = false;
        Retreating |= retreat;
        if (Retreating && Phase is not BattlePhase.Windup and not BattlePhase.Impact)
        {
            Vector2 away = Hero - Enemy;
            if (away.LengthSquared() < .01f) away = Vector2.UnitX;
            PreviousHero = Hero;
            Hero = MoveTowards(Hero, Hero + Vector2.Normalize(away) * 8, .9f);
            Dashing = true;
            if (Vector2.Distance(Hero, Enemy) > 9) escapeProgress++;
        }
        BattleEvent result = BattleEvent.None;

        if (Phase == BattlePhase.Windup)
        {
            // Aim is locked at the start. The hero must physically leave the danger area.
            if (!attemptedDodge && PhaseTick >= WindupLength / 2)
            {
                attemptedDodge = true;
                double dodgeChance = Math.Clamp(policy.DodgeChance - Math.Max(0, Opponent.Level - heroLevel) * .06, .18, .9);
                if (random.NextDouble() < dodgeChance)
                {
                    var candidates = new[]
                    {
                        Hero + new Vector2(0, -7), Hero + new Vector2(0, 7),
                        Hero + new Vector2(-7, 0), Hero + new Vector2(7, 0),
                        Hero + new Vector2(-5, -5), Hero + new Vector2(5, 5)
                    }.Select(Clamp).Where(p => !Threatens(p)).ToArray();
                    if (candidates.Length > 0) dashTarget = candidates[random.Next(candidates.Length)];
                    else dashTarget = Hero;
                    PreviousHero = Hero;
                }
                else dashTarget = Hero;
            }
            if (attemptedDodge && Vector2.Distance(Hero, dashTarget) > .1f)
            {
                Hero = MoveTowards(Hero, dashTarget, 1.35f + mobility);
                Dashing = true;
            }
            if (PhaseTick >= WindupLength)
            {
                AttackCount++;
                bool hit = Threatens(Hero);
                if (!hit) SuccessfulDodges++;
                result = hit ? BattleEvent.HeroHurt : BattleEvent.Dodged;
                Phase = BattlePhase.Impact;
                PhaseTick = 0;
                Flash = 4;
            }
        }
        else if (Phase == BattlePhase.Impact)
        {
            if (PhaseTick >= 4) { Phase = BattlePhase.Recovery; PhaseTick = 0; }
        }
        else
        {
            float distance = Vector2.Distance(Hero, Enemy);
            if (!Retreating && distance > 2.4f) Hero = MoveTowards(Hero, Enemy, (Phase == BattlePhase.Recovery ? .7f : policy.ApproachSpeed) + mobility);
            if (Phase == BattlePhase.Approaching)
            {
                if (distance > 5) Enemy = MoveTowards(Enemy, Hero, (Major ? .22f : .29f) + outclassed * .13f);
                if (enemyCooldown == 0 && distance < 7.5f)
                {
                    Phase = BattlePhase.Windup;
                    PhaseTick = 0;
                    Target = Hero;
                    Shape = Major ? (AttackShape)random.Next(3) : AttackShape.Circle;
                    attemptedDodge = false;
                    dashTarget = Hero;
                }
            }
            else if (Phase == BattlePhase.Recovery && PhaseTick > (Major ? 22 : 10))
            {
                Phase = BattlePhase.Approaching;
                PhaseTick = 0;
                enemyCooldown = Major ? 9 : 6;
            }
            // Reaching melee range or having enough MP is required to hit.
            distance = Vector2.Distance(Hero, Enemy);
            if (!Retreating && heroCooldown == 0 && (distance < 2.8f || canCast && distance < 10 && Phase == BattlePhase.Recovery))
            {
                bool spell = canCast && distance > 3.0f;
                LastDamage = CombatRules.Damage(attackPower + (spell ? 2 : 0), Opponent.Defense, random.Next(3));
                Opponent.Health = Math.Max(0, Opponent.Health - LastDamage);
                heroCooldown = Major ? policy.AttackCooldown : Math.Max(5, policy.AttackCooldown - 2);
                result = spell ? BattleEvent.HeroSpell : BattleEvent.HeroHit;
                Flash = 3;
            }
        }
        if (result != BattleEvent.None) LastEvent = result;
        return result;
    }

    public bool Threatens(Vector2 point) => Shape switch
    {
        AttackShape.Sweep => Math.Abs(point.Y - Target.Y) <= 2.1f,
        AttackShape.Cross => Math.Abs(point.X - Target.X) <= 1.7f || Math.Abs(point.Y - Target.Y) <= 1.7f,
        _ => Vector2.Distance(point, Target) <= (Major ? 4 : 2.7f)
    };

    private static Vector2 Clamp(Vector2 p) => new(Math.Clamp(p.X, 2, Width - 2), Math.Clamp(p.Y, 2, Height - 2));
    private static Vector2 MoveTowards(Vector2 from, Vector2 to, float speed)
    {
        var delta = to - from;
        return Clamp(delta.Length() <= speed ? to : from + Vector2.Normalize(delta) * speed);
    }
}
