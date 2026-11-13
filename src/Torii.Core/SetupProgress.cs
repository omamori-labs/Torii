namespace Torii.Core
{
    public enum SetupStage
    {
        DownloadingOmamori,
        DownloadingOsu,
        Installing,
    }

    public readonly record struct SetupProgress(SetupStage Stage, double Fraction);
}
