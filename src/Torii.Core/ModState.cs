namespace Torii.Core
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
