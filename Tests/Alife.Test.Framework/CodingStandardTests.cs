using System.Text.RegularExpressions;

namespace Alife.Test.Framework;

public class CodingStandardTests
{
    [Test]
    public void ProductionSourceShouldNotPrintCaughtExceptionsDirectly()
    {
        string repositoryRoot = FindRepositoryRoot();
        string[] sourceRoots = [
            Path.Combine(repositoryRoot, "sources", "Alife"),
            Path.Combine(repositoryRoot, "sources", "Alife.Function"),
            Path.Combine(repositoryRoot, "sources", "Alife.DeskPet"),
        ];
        Regex directExceptionPrintPattern = new(
            @"Console\.WriteLine\((e|ex|exception)\);",
            RegexOptions.Compiled);
        string[] violations = sourceRoots
            .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new {
                    File = Path.GetRelativePath(repositoryRoot, file),
                    Line = index + 1,
                    Text = line
                }))
            .Where(item => directExceptionPrintPattern.IsMatch(item.Text))
            .Select(item => $"{item.File}:{item.Line}: {item.Text.Trim()}")
            .ToArray();

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    [Test]
    public void ProductionSourceShouldUseTerminalAbstractionForConsoleLineOutput()
    {
        string repositoryRoot = FindRepositoryRoot();
        string terminalFile = Path.Combine(
            repositoryRoot,
            "sources",
            "Alife",
            "Alife.Platform",
            "AlifeTerminal.cs");
        string[] sourceRoots = [
            Path.Combine(repositoryRoot, "sources", "Alife"),
            Path.Combine(repositoryRoot, "sources", "Alife.Function"),
            Path.Combine(repositoryRoot, "sources", "Alife.DeskPet"),
        ];
        Regex consoleLineOutputPattern = new(
            @"Console\.WriteLine\(",
            RegexOptions.Compiled);
        string[] violations = sourceRoots
            .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            .Where(file => string.Equals(Path.GetFullPath(file), terminalFile, StringComparison.OrdinalIgnoreCase) == false)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new {
                    File = Path.GetRelativePath(repositoryRoot, file),
                    Line = index + 1,
                    Text = line
                }))
            .Where(item => consoleLineOutputPattern.IsMatch(item.Text))
            .Select(item => $"{item.File}:{item.Line}: {item.Text.Trim()}")
            .ToArray();

        Assert.That(violations, Is.Empty, string.Join(Environment.NewLine, violations));
    }

    static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "sources")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing sources folder.");
    }
}
