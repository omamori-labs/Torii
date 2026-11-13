using System;

namespace Torii.Core
{
    public sealed record LazerPackage(string Version, Uri Download, string Sha256, long Size);
}
