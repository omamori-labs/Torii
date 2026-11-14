using System.Collections.Generic;

namespace Torii.Core.Settings
{
    public sealed record AppSettings(string? SelectedInstance = null, double? WindowWidth = null, double? WindowHeight = null, IReadOnlyList<string>? InstanceOrder = null, string? SkippedUpdate = null);
}
