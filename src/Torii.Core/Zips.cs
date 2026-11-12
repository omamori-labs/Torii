using System.IO;
using System.IO.Compression;

namespace Torii.Core
{
    internal static class Zips
    {
        /// <exception cref="InvalidDataException">Not a zip</exception>
        public static ZipArchive Open(string path)
        {
            try
            {
                return ZipFile.OpenRead(path);
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException("This isn't a zip file.", e);
            }
        }
    }
}
