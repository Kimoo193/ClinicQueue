using ClinicQueue.Domain.Common;
using FluentAssertions;

namespace ClinicQueue.UnitTests.Domain;

public class BaseEntityTests
{
    private sealed class SampleEntity : BaseEntity;

    [Fact]
    public void New_entity_gets_non_empty_id_and_utc_created_at()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var entity = new SampleEntity();

        entity.Id.Should().NotBe(Guid.Empty);
        entity.CreatedAt.Should().BeOnOrAfter(before);
        entity.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        entity.UpdatedAt.Should().BeNull();
    }
}
