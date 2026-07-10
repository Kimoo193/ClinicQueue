using ClinicQueue.Infrastructure.Identity;
using FluentAssertions;

namespace ClinicQueue.UnitTests.Identity;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_and_verify_round_trip()
    {
        var hash = _hasher.Hash("CorrectHorseBatteryStaple!");
        hash.Should().NotBeNullOrWhiteSpace();
        _hasher.Verify("CorrectHorseBatteryStaple!", hash).Should().BeTrue();
        _hasher.Verify("wrong-password", hash).Should().BeFalse();
    }
}
