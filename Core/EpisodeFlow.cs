namespace DotARPG.Core;

public enum EpisodePhase { Opening, Playing, Ending }

public sealed class EpisodeFlow
{
    public const double OpeningDuration = 6.5;
    public EpisodePhase Phase { get; private set; } = EpisodePhase.Opening;
    public double OpeningElapsed { get; private set; }

    public void Advance(double seconds)
    {
        if (Phase != EpisodePhase.Opening) return;
        OpeningElapsed = Math.Min(OpeningDuration, OpeningElapsed + Math.Max(0, seconds));
        if (OpeningElapsed >= OpeningDuration) Start();
    }
    public void Start()
    {
        if (Phase == EpisodePhase.Opening) Phase = EpisodePhase.Playing;
    }
    public void Finish() => Phase = EpisodePhase.Ending;
    public void Retry()
    {
        if (Phase == EpisodePhase.Ending) Phase = EpisodePhase.Playing;
    }
    public void Restore(EpisodePhase phase, double openingElapsed)
    {
        Phase = phase;
        OpeningElapsed = Math.Clamp(openingElapsed, 0, OpeningDuration);
    }
}
