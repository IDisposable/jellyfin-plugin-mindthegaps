using System;
using System.IO;

namespace Jellyfin.Plugin.MindTheGaps.Tests;

/// <summary>
/// Loads the captured provider responses under TestData/ (copied next to the test assembly).
/// </summary>
internal static class TestData
{
    public static string Read(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));
}
