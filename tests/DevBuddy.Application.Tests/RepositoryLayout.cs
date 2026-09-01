using System.Reflection;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Locates the repository and the built assemblies so the architecture tests can inspect both
/// the project files and the compiled output.
/// </summary>
internal static class RepositoryLayout
{
    /// <summary>
    /// Walks up from the test output directory to the folder holding the solution file. Throws
    /// rather than skipping if it cannot be found: a rule that quietly stops being checked is
    /// worse than no rule.
    /// </summary>
    public static DirectoryInfo Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null)
            {
                if (directory.EnumerateFiles("DevBuddy.slnx").Any())
                {
                    return directory;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException(
                $"Could not find DevBuddy.slnx above {AppContext.BaseDirectory}. The architecture "
                + "tests inspect the project files and cannot run without the repository present.");
        }
    }

    public static string ProjectFile(string relativePath) =>
        Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Loads a built assembly from the test output directory by file, so the check does not
    /// depend on the compiler having kept a reference the test project never uses.
    /// </summary>
    public static Assembly Load(string assemblyName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"{assemblyName}.dll was not found in {AppContext.BaseDirectory}. "
                + "DevBuddy.Application.Tests must reference every project it inspects.");
        }

        return Assembly.LoadFrom(path);
    }
}
