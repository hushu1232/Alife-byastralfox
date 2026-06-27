using Alife.Function.QChat;
using NUnit.Framework;
using System;
using System.IO;

namespace Alife.Test.QChat;

[TestFixture]
public class QChatUserProfileServiceTests
{
    [Test]
    public void ResolvePreferredAddressUsesSavedNicknameBeforeDisplayName()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "alife-qchat-profile-tests", Guid.NewGuid().ToString("N"));
        QChatUserProfileService service = new(rootPath);

        service.SetProfile(new QChatUserProfile(
            UserId: 10001,
            PreferredNickname: "小雨",
            CuteNicknames: ["雨宝", "小雨同学"],
            FormalName: "潇雨的吉他创作室",
            RelationshipLabel: "friend",
            AddressStyle: "cute",
            Source: "owner-set",
            Confidence: 1f,
            LastSeenGroupId: 867165927,
            LastSeenAt: DateTimeOffset.Parse("2026-06-17T12:00:00+08:00"),
            Notes: "测试资料"));

        QChatUserProfileService reloaded = new(rootPath);

        Assert.That(reloaded.ResolvePreferredAddress(10001, displayName: "潇雨的吉他创作室"), Is.EqualTo("小雨"));
    }

    [Test]
    public void ResolvePreferredAddressFallsBackToDisplayNameThenUserId()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "alife-qchat-profile-tests", Guid.NewGuid().ToString("N"));
        QChatUserProfileService service = new(rootPath);

        Assert.That(service.ResolvePreferredAddress(10002, displayName: "群名片"), Is.EqualTo("群名片"));
        Assert.That(service.ResolvePreferredAddress(10003, displayName: ""), Is.EqualTo("10003"));
    }

    [Test]
    public void ResolvePreferredAddressReloadsExternalProfileFileChanges()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "alife-qchat-profile-tests", Guid.NewGuid().ToString("N"));
        QChatUserProfileService liveService = new(rootPath);
        QChatUserProfileService editorService = new(rootPath);

        Assert.That(liveService.ResolvePreferredAddress(10004, displayName: "正式名"), Is.EqualTo("正式名"));

        editorService.SetProfile(new QChatUserProfile(
            UserId: 10004,
            PreferredNickname: "小眠",
            Source: "owner-set",
            Confidence: 1f));

        Assert.That(liveService.ResolvePreferredAddress(10004, displayName: "正式名"), Is.EqualTo("小眠"));
    }

    [Test]
    public void ScopedProfilesKeepAgentsSeparatedForSameQqUser()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "alife-qchat-profile-tests", Guid.NewGuid().ToString("N"));
        QChatUserProfileService service = new(rootPath);

        service.SetProfile("xiayu", 2905391496, new QChatUserProfile(
            UserId: 2001,
            PreferredNickname: "雨宝"));
        service.SetProfile("mixu", 3340947887, new QChatUserProfile(
            UserId: 2001,
            PreferredNickname: "小雨"));

        Assert.Multiple(() =>
        {
            Assert.That(service.ResolvePreferredAddress("xiayu", 2905391496, 2001, "formal"), Is.EqualTo("雨宝"));
            Assert.That(service.ResolvePreferredAddress("mixu", 3340947887, 2001, "formal"), Is.EqualTo("小雨"));
        });
    }

    [Test]
    public void ScopedProfilesReloadExternalFileChanges()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), "alife-qchat-profile-tests", Guid.NewGuid().ToString("N"));
        QChatUserProfileService liveService = new(rootPath);
        QChatUserProfileService editorService = new(rootPath);

        editorService.SetProfile("xiayu", 2905391496, new QChatUserProfile(
            UserId: 2001,
            PreferredNickname: "雨宝"));

        Assert.That(liveService.ResolvePreferredAddress("xiayu", 2905391496, 2001, "formal"), Is.EqualTo("雨宝"));
    }
}
