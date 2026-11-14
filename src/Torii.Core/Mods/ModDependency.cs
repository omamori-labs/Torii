namespace Torii.Core.Mods
{
    public sealed record ModDependency(string Id, string MinVersion, bool Optional);
}
