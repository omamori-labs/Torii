using Torii.Core;

namespace Torii
{
    public sealed class MainViewModel
    {
        public MainViewModel(OmamoriInstall install)
        {
            DataDirectory = install.DataDirectory;
            Warning = OsuData.LooksLikeData(install.DataDirectory) ? null : "osu! hasn't been run with this as its data folder.";

            if (!install.IsInstalled)
                Summary = "Not installed";
            else
                Summary = install.ReadVersion() is string version ? $"Version {version} is installed" : "Installed, version unknown";
        }

        public string DataDirectory { get; }

        public string? Warning { get; }

        public string Summary { get; }
    }
}
