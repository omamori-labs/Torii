namespace Torii.Core.Mods
{
    public enum ModState
    {
        // Omamori can still refuse it, e.g. a missing dependency
        On,

        Off,

        Crashed,

        Problem,
    }
}
