using System;
using System.IO;

namespace Torii.Core.Releases
{
    public sealed class UpdateLog
    {
        private const long max_bytes = 64 * 1024;

        public UpdateLog(string file)
        {
            FilePath = Path.GetFullPath(file);
        }

        public string FilePath { get; }

        public static string DefaultFile() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Torii", "update.log");

        public void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > max_bytes)
                    File.Delete(FilePath);

                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
