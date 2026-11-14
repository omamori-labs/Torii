namespace Torii.Core.Mods
{
    public enum ModState
    {
        // Omamori can still refuse it, e.g. an incompatible API version
        On,

        Off,

        Crashed,

        Problem,
    }
}
