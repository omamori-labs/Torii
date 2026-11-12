namespace Torii.Core
{
    public enum SetupStage
    {
        DownloadingOmamori,
        DownloadingOsu,
        Installing,
    }

    /// <summary>Fraction is 0 to 1 within the stage</summary>
    public readonly record struct SetupProgress(SetupStage Stage, double Fraction);
}
