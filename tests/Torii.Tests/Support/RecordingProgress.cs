using System;
using System.Collections.Generic;

namespace Torii.Tests
{
    // Progress<T> reports on another thread and loses the order
    internal sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Reported { get; } = [];

        public void Report(T value) => Reported.Add(value);
    }
}
