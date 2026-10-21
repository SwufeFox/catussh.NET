using Catussh.Core.Utils;

namespace Catussh.Core.Tests;

/// <summary>
/// Port of <c>Foundation/CatusshModule/Tests/CatusshModuleTests/KnownHostsTests.swift</c>.
/// The Swift suite used a throwaway <c>UserDefaults</c> suite; here each test gets a
/// fresh in-memory store.
/// </summary>
public sealed class KnownHostsTests
{
    private readonly KnownHosts.HostKey _first = new("ssh-ed25519", [1, 2, 3, 4]);
    private readonly KnownHosts.HostKey _other = new("ssh-ed25519", [9, 9, 9, 9]);
    private readonly InMemoryKeyValueStore _defaults = new();
    private readonly KnownHosts _hosts;

    public KnownHostsTests()
    {
        _hosts = new KnownHosts(_defaults, storageKey: "knownHosts");
    }

    [Test]
    public async Task TheFirstKeyIsSavedAndAcceptedAgain()
    {
        await Assert.That(_hosts.Accepts(_first, "example.com", 22)).IsTrue();
        await Assert.That(_hosts.SavedKey("example.com", 22)).IsEqualTo(_first);
        await Assert.That(_hosts.Accepts(_first, "example.com", 22)).IsTrue();
        await Assert.That(_hosts.RefusalMessage("example.com", 22)).IsNull();
    }

    [Test]
    public async Task AChangedKeyIsRefusedAndTheSavedOneKept()
    {
        await Assert.That(_hosts.Accepts(_first, "example.com", 22)).IsTrue();
        await Assert.That(_hosts.Accepts(_other, "example.com", 22)).IsFalse();
        await Assert.That(_hosts.SavedKey("example.com", 22)).IsEqualTo(_first);
        await Assert.That(_hosts.RefusedKey("example.com", 22)).IsEqualTo(_other);
        var message = _hosts.RefusalMessage("example.com", 22);
        await Assert.That(message!.Contains(_first.Fingerprint)).IsTrue();
        await Assert.That(message.Contains(_other.Fingerprint)).IsTrue();
    }

    [Test]
    public async Task TheSameKeyOfAnotherTypeIsRefused()
    {
        await Assert.That(_hosts.Accepts(_first, "example.com", 22)).IsTrue();
        var retyped = new KnownHosts.HostKey("ssh-rsa", _first.Key);
        await Assert.That(_hosts.Accepts(retyped, "example.com", 22)).IsFalse();
    }

    [Test]
    public async Task AKeyIsSavedPerAddressAndPort()
    {
        await Assert.That(_hosts.Accepts(_first, "example.com", 22)).IsTrue();
        await Assert.That(_hosts.Accepts(_other, "example.com", 2222)).IsTrue();
        await Assert.That(_hosts.Accepts(_other, "other.example.com", 22)).IsTrue();
        // an address is the same in any case
        await Assert.That(_hosts.Accepts(_first, " Example.COM ", 22)).IsTrue();
        await Assert.That(_hosts.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ForgettingLetsTheNextKeyIn()
    {
        await Assert.That(_hosts.Accepts(_first, "example.com", 22)).IsTrue();
        await Assert.That(_hosts.Accepts(_other, "example.com", 22)).IsFalse();
        _hosts.Forget("example.com", 22);
        await Assert.That(_hosts.RefusalMessage("example.com", 22)).IsNull();
        await Assert.That(_hosts.Accepts(_other, "example.com", 22)).IsTrue();
        await Assert.That(_hosts.SavedKey("example.com", 22)).IsEqualTo(_other);
    }

    [Test]
    public async Task SavedKeysOutliveTheObject()
    {
        await Assert.That(_hosts.Accepts(_first, "example.com", 22)).IsTrue();
        var reopened = new KnownHosts(_defaults, storageKey: "knownHosts");
        await Assert.That(reopened.Accepts(_other, "example.com", 22)).IsFalse();
        reopened.ForgetAll();
        await Assert.That(reopened.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TheFingerprintIsTheOneSshPrints()
    {
        // SHA-256 of an empty key, base64 without padding.
        var empty = new KnownHosts.HostKey("ssh-ed25519", []);
        await Assert.That(empty.Fingerprint).IsEqualTo("SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU");
    }
}
