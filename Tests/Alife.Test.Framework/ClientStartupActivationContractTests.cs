namespace Alife.Test.Framework;

public class ClientStartupActivationContractTests
{
    [Test]
    public void StartupDefersAutomaticCharacterActivationUntilMainWindowLoadedAndSchedulesItOnce()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "sources", "Alife", "Alife.Client", "App.xaml.cs"));

        Assert.That(source, Does.Contain("ServiceProvider.GetRequiredService<ActivityNotifyService>();"));
        Assert.That(source, Does.Contain("mainWindow.Loaded +="));
        Assert.That(source, Does.Contain("Interlocked.Exchange(ref automaticActivationScheduled, 1)"));
        Assert.That(source, Does.Contain("mainWindow.Show();"));
        Assert.That(source.IndexOf("mainWindow.Loaded +=", StringComparison.Ordinal),
            Is.LessThan(source.IndexOf("mainWindow.Show();", StringComparison.Ordinal)));
        Assert.That(source.IndexOf("ActivateAutoActivateCharacters", StringComparison.Ordinal),
            Is.GreaterThan(source.IndexOf("mainWindow.Loaded +=", StringComparison.Ordinal)));
    }

    [Test]
    public void StartupLogsOnlyTheAutomaticActivationExceptionType()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "sources", "Alife", "Alife.Client", "App.xaml.cs"));

        Assert.That(source, Does.Contain("Automatic character activation failed: {ex.GetType().Name}"));
        Assert.That(source, Does.Not.Contain("AlifeTerminal.LogError(ex.ToString())"));
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
