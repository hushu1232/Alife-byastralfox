namespace Alife.Test.Framework;

public class NavMenuActivationStatusContractTests
{
    [Test]
    public void NavMenuRendersSafeActivationPresentationAndRefreshesForStateChanges()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "sources",
            "Alife",
            "Alife.Client",
            "Components",
            "Layout",
            "NavMenu.razor"));

        Assert.That(source, Does.Contain("ActivityNotifyService.GetActivationState(character.Name)"));
        Assert.That(source, Does.Contain("ActivityNotifyService.GetActivationPresentation(activationState)"));
        Assert.That(source, Does.Contain("activityPresentation.Label"));
        Assert.That(source, Does.Contain("activityPresentation.CssClass"));
        Assert.That(source, Does.Contain("role=\"status\""));
        Assert.That(source, Does.Contain("<span class=\"visually-hidden\">@activityPresentation.Label</span>"));
        Assert.That(source, Does.Contain("ActivityNotifyService.OnActivationStateChanged += RefreshActivationState"));
        Assert.That(source, Does.Contain("ActivityNotifyService.OnActivationStateChanged -= RefreshActivationState"));
        Assert.That(source, Does.Not.Contain("ActivationFailed"));
        Assert.That(source, Does.Not.Contain("DataAgent"));
        Assert.That(source, Does.Not.Contain("LangGraph"));
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
