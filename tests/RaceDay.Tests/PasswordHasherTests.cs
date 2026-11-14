using RaceDay.Api.Services;
using Xunit;

namespace RaceDay.Tests;

public class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new Pbkdf2PasswordHasher();

    [Fact]
    public void Hash_DoesNotStorePlainTextPassword()
    {
        var hash = _hasher.Hash("Sup3rSecret!");

        Assert.DoesNotContain("Sup3rSecret!", hash);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForCorrectPassword()
    {
        var hash = _hasher.Hash("Sup3rSecret!");

        Assert.True(_hasher.Verify("Sup3rSecret!", hash));
    }

    [Fact]
    public void Verify_ReturnsFalse_ForWrongPassword()
    {
        var hash = _hasher.Hash("Sup3rSecret!");

        Assert.False(_hasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Hash_UsesADifferentSaltEachTime()
    {
        var first = _hasher.Hash("Sup3rSecret!");
        var second = _hasher.Hash("Sup3rSecret!");

        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("v1.abc.def.ghi")]
    [InlineData("v2.1000.AAAA.BBBB")]
    public void Verify_ReturnsFalse_ForMalformedHash(string malformed)
    {
        Assert.False(_hasher.Verify("anything", malformed));
    }
}
