using System;
using System.Diagnostics;
using System.IO;
using Torii.Core.Lazer;

namespace Torii.Core
{
    public sealed class GameLauncher
    {
        private readonly Action<ProcessStartInfo> start;

        public GameLauncher(Action<ProcessStartInfo>? start = null)
        {
            this.start = start ?? (info => Process.Start(info)?.Dispose());
        }

        public void Launch(LazerInstance instance)
        {
            if (!instance.IsInstalled || !File.Exists(instance.Executable))
                throw new InvalidOperationException("The modded osu! isn't set up yet.");

            start(new ProcessStartInfo(instance.Executable)
            {
                WorkingDirectory = instance.Directory,
                UseShellExecute = false,
            });
        }
    }
}
