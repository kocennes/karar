namespace Karar.UnitTests;

internal static class TestRepoPaths
{
    public static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "backend", "Karar.Api", "Karar.Api.csproj")) &&
                    File.Exists(Path.Combine(directory.FullName, "tests", "Karar.UnitTests", "Karar.UnitTests.csproj")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Repository root could not be found.");
        }
    }

    public static string FilePath(params string[] pathParts) =>
        Path.Combine(new[] { Root }.Concat(pathParts).ToArray());

    public static string ReadText(params string[] pathParts) =>
        File.ReadAllText(FilePath(pathParts));

    public static bool TryReadText(out string content, params string[] pathParts)
    {
        var path = FilePath(pathParts);
        if (!File.Exists(path))
        {
            content = string.Empty;
            return false;
        }

        content = File.ReadAllText(path);
        return true;
    }
}
