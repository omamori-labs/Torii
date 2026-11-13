using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Torii.Core.Lazer;

namespace Torii.Core.Instances
{
    public sealed class InstanceStore
    {
        private const int max_name_length = 50;

        private static readonly char[] invalid_characters = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

        private static readonly string[] reserved_names =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ];

        private readonly string? legacy;
        private readonly Func<LazerInstance, bool> isRunning;
        private readonly GameCache? cache;

        public InstanceStore(string directory, string? legacy = null, Func<LazerInstance, bool>? isRunning = null, GameCache? cache = null)
        {
            Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            this.legacy = legacy == null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacy));
            this.isRunning = isRunning ?? (instance => OsuProcess.IsRunningIn(instance.Directory));
            this.cache = cache;
        }

        public string Root { get; }

        public static string DefaultDirectory() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torii", "instances");

        public IReadOnlyList<LazerInstance> List()
        {
            var found = new List<LazerInstance>();

            if (Directory.Exists(Root))
                found.AddRange(Directory.EnumerateDirectories(Root).Select(folder => new LazerInstance(folder)));

            if (legacy != null && Directory.Exists(legacy))
                found.Add(new LazerInstance(legacy));

            return [.. found.OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)];
        }

        public string? Validate(string name, LazerInstance? except = null)
        {
            string trimmed = name.Trim();

            if (trimmed.Length == 0)
                return "Give the instance a name.";

            if (trimmed.Length > max_name_length)
                return $"Use at most {max_name_length} characters.";

            if (trimmed.Any(c => char.IsControl(c) || invalid_characters.Contains(c)))
                return "The name can't have \\ / : * ? \" < > | in it.";

            if (trimmed.EndsWith('.'))
                return "The name can't end with a dot.";

            if (reserved_names.Contains(trimmed.Split('.')[0].ToUpperInvariant()))
                return "Windows doesn't allow that name.";

            if (List().Any(instance => (except == null || !Paths.Same(instance.Directory, except.Directory)) && string.Equals(instance.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                return "There is already an instance with that name.";

            return null;
        }

        public LazerInstance Create(string name)
        {
            if (Validate(name) is string problem)
                throw new InvalidOperationException(problem);

            var instance = new LazerInstance(Path.Combine(Root, name.Trim()));
            Directory.CreateDirectory(instance.Directory);

            return instance;
        }

        public LazerInstance Rename(LazerInstance instance, string name)
        {
            if (!Manages(instance))
                throw new InvalidOperationException("Torii only renames instances it manages.");

            if (Validate(name, instance) is string problem)
                throw new InvalidOperationException(problem);

            if (isRunning(instance))
                throw new InvalidOperationException("Close osu! in this instance first.");

            string target = Path.Combine(Root, name.Trim());

            if (string.Equals(instance.Directory, target, StringComparison.Ordinal))
                return instance;

            Directory.CreateDirectory(Root);

            // Windows won't move a folder onto the same name in another case
            if (Paths.Same(instance.Directory, target))
            {
                string temporary = Path.Combine(Root, $"renaming-{Guid.NewGuid():N}");
                Directory.Move(instance.Directory, temporary);
                Directory.Move(temporary, target);
            }
            else
            {
                Directory.Move(instance.Directory, target);
            }

            return new LazerInstance(target);
        }

        public void Delete(LazerInstance instance)
        {
            if (!Manages(instance))
                throw new InvalidOperationException("Torii only deletes instances it manages.");

            if (isRunning(instance))
                throw new InvalidOperationException("Close osu! in this instance first.");

            Directory.Delete(instance.Directory, recursive: true);

            cache?.Prune(List().Select(other => other.ReadVersion()).OfType<string>());
        }

        public bool Manages(LazerInstance instance) =>
            (legacy != null && Paths.Same(instance.Directory, legacy))
            || Paths.Same(Path.GetDirectoryName(instance.Directory) ?? string.Empty, Root);
    }
}
