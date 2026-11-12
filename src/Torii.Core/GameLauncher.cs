using System;
using System.Diagnostics;
using System.IO;

namespace Torii.Core
{
    /// <summary>Starts an instance's game</summary>
    public sealed class GameLauncher
    {
        private readonly Action<ProcessStartInfo> start;

        public GameLauncher(Action<ProcessStartInfo>? start = null)
        {
            this.start = start ?? (info => Process.Start(info)?.Dispose());
        }

        /// <exception cref="InvalidOperationException">The folder isn't a set up instance, starting it could open the player's real data</exception>
        /// <exception cref="System.ComponentModel.Win32Exception">The game couldn't be started</exception>
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
