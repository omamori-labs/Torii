namespace Torii.Core
{
    public enum ModState
    {
        /// <summary>Omamori will try to load it. It can still refuse, e.g. for a missing dependency</summary>
        On,

        Off,

        /// <summary>Skipped by Omamori after crashing in consecutive sessions</summary>
        Crashed,

        /// <summary>Can't load as it is on disk</summary>
        Problem,
    }
}
