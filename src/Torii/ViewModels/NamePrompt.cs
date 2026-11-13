using System;

namespace Torii.ViewModels
{
    public sealed record NamePrompt(string Title, string Confirm, string Initial, Func<string, string?> Validate);
}
