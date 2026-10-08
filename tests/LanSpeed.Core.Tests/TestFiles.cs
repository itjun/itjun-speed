namespace LanSpeed.Core.Tests;

public static class TestFiles
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    public static IReadOnlyList<string> ReadLines(string name) =>
        Read(name)
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();
}
