using System;

namespace Torii.Core.Lazer
{
    public sealed record LazerPackage(string Version, Uri Download, string Sha256, long Size);
}
