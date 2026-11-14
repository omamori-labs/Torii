namespace Torii.Core.Mods
{
    public enum ModState
    {
        // Omamori can still refuse it, e.g. a required hook that isn't available
        On,

        Off,

        Crashed,

        Problem,
    }
}
