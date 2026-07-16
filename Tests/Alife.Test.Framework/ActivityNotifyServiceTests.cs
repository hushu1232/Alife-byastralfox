using System.Reflection;
using Alife.Components.Services;
using Alife.Framework;

namespace Alife.Test.Framework;

public class ActivityNotifyServiceTests
{
    [Test]
    public void LifecycleEventsPublishOnlySafeCharacterActivationStates()
    {
        ChatActivitySystem system = new(null!, null!, null!, new StorageSystem());
        ActivityNotifyService service = new(system);
        Character character = new() { Name = "SafeStateCharacter" };
        ChatActivity activity = new(character, null!, null!, new ChatBot(null!, null!), []);
        List<ActivityActivationStatus> notifications = [];
        service.OnActivationStateChanged += notifications.Add;

        RaiseEvent(system, "Activating", character);
        RaiseEvent(system, "Activated", activity);
        RaiseEvent(system, "ActivationFailed", character, new InvalidOperationException("secret=should-not-be-exposed"));
        RaiseEvent(system, "Destroyed", activity);

        Assert.That(notifications, Is.EqualTo([
            new ActivityActivationStatus(character.Name, ActivityActivationState.Initializing),
            new ActivityActivationStatus(character.Name, ActivityActivationState.Active),
            new ActivityActivationStatus(character.Name, ActivityActivationState.Failed),
            new ActivityActivationStatus(character.Name, ActivityActivationState.Destroyed)
        ]));
        Assert.That(service.GetActivationState(character.Name), Is.EqualTo(ActivityActivationState.Destroyed));
        Assert.That(notifications.Select(notification => notification.ToString()),
            Has.None.Contains("secret=should-not-be-exposed"));
    }

    [Test]
    public void EveryActivationLifecycleStateRefreshesExistingUiNotification()
    {
        ChatActivitySystem system = new(null!, null!, null!, new StorageSystem());
        ActivityNotifyService service = new(system);
        Character character = new() { Name = "RefreshStateCharacter" };
        ChatActivity activity = new(character, null!, null!, new ChatBot(null!, null!), []);
        int refreshes = 0;
        service.OnChanged += () => refreshes++;

        RaiseEvent(system, "Activating", character);
        RaiseEvent(system, "Activated", activity);
        RaiseEvent(system, "ActivationFailed", character, new InvalidOperationException());
        RaiseEvent(system, "Destroyed", activity);

        Assert.That(refreshes, Is.EqualTo(4));
    }

    [Test]
    public void UnknownCharacterHasNoActivationState()
    {
        ActivityNotifyService service = new(new ChatActivitySystem(null!, null!, null!, new StorageSystem()));

        Assert.That(service.GetActivationState("UnknownCharacter"), Is.Null);
    }

    [TestCase(ActivityActivationState.Initializing, "Initializing", "status-initializing")]
    [TestCase(ActivityActivationState.Active, "Active", "status-active")]
    [TestCase(ActivityActivationState.Failed, "Failed", "status-failed")]
    [TestCase(ActivityActivationState.Destroyed, "Stopped", "status-stopped")]
    public void ActivationStateHasFixedSafePresentation(ActivityActivationState state, string label, string cssClass)
    {
        ActivityActivationPresentation? presentation = ActivityNotifyService.GetActivationPresentation(state);

        Assert.That(presentation, Is.EqualTo(new ActivityActivationPresentation(label, cssClass)));
    }

    [Test]
    public void MissingActivationStateHasNoPresentation()
    {
        Assert.That(ActivityNotifyService.GetActivationPresentation(null), Is.Null);
    }

    static void RaiseEvent(object target, string eventName, params object?[] arguments)
    {
        FieldInfo? field = target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Event backing field '{eventName}' should exist.");
        (field!.GetValue(target) as MulticastDelegate)?.DynamicInvoke(arguments);
    }
}
