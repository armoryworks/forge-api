namespace Forge.Tests.Architecture;

/// <summary>
/// Locates the forge-api repository root from the test binary's directory by walking up until
/// <c>forge.slnx</c> is found. Source-scanning architecture tests need the source tree, which is
/// present both locally and in CI (tests run from a checkout, never from a published artifact).
/// </summary>
internal static class RepoRoot
{
    private static readonly Lazy<string> Cached = new(Locate);

    public static string Path => Cached.Value;

    private static string Locate()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "forge.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not find forge.slnx above " + AppContext.BaseDirectory +
            " — architecture tests must run from a source checkout.");
    }

    /// <summary>All *.cs files under <paramref name="relativeDir"/>, skipping obj/bin, as repo-relative forward-slash paths.</summary>
    public static IEnumerable<(string RelativePath, string FullPath)> SourceFiles(string relativeDir)
    {
        var root = System.IO.Path.Combine(Path, relativeDir);
        if (!Directory.Exists(root)) yield break;
        foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}") ||
                f.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}"))
                continue;
            yield return (System.IO.Path.GetRelativePath(Path, f).Replace('\\', '/'), f);
        }
    }
}
