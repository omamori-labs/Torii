using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Torii.Core.Mods
{
    public static class ModOrder
    {
        public static IReadOnlyList<string>? Load(string file)
        {
            try
            {
                if (!File.Exists(file))
                    return null;

                string[] names = [.. File.ReadAllLines(file).Select(line => line.Trim()).Where(line => line.Length > 0)];

                return names.Length == 0 ? null : names;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static void Save(string file, IEnumerable<string> names)
        {
            string temporary = file + ".tmp";

            File.WriteAllLines(temporary, names);
            File.Move(temporary, file, overwrite: true);
        }

        public static IReadOnlyList<InstalledMod> Apply(IReadOnlyList<InstalledMod> mods, IReadOnlyList<string>? order)
        {
            if (order == null)
                return mods;

            List<string> names = [.. order];

            return [.. mods.OrderBy(mod =>
            {
                int index = names.FindIndex(name => string.Equals(name, Path.GetFileName(mod.Directory), StringComparison.OrdinalIgnoreCase));
                return index < 0 ? int.MaxValue : index;
            })];
        }
    }
}
