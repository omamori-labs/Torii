namespace Torii.Core.Mods
{
    internal static class ModApi
    {
        // Messages are the ones from Omamori's ApiVersions
        public static string? Problem(string modApiVersion, string provided)
        {
            if (!ModVersion.TryParse(provided, out ModVersion? have))
                return null;

            if (!ModVersion.TryParse(modApiVersion, out ModVersion? built))
                return $"invalid apiVersion \"{modApiVersion}\", expected major.minor like \"{provided}\"";

            if (built.Major != have.Major)
                return $"built for API {modApiVersion}, Omamori provides {provided} which isn't compatible";

            if (built.Minor > have.Minor)
                return $"built for API {modApiVersion}, needs a newer Omamori (this one provides {provided})";

            return null;
        }
    }
}
