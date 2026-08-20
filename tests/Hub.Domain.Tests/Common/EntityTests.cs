using Hub.Domain.Common;

namespace Hub.Domain.Tests.Common;

public sealed class EntityTests
{
    private sealed class TestEntity(Guid id) : Entity<Guid>(id);

    [Fact]
    public void Entity_ShouldExposeId()
    {
        var id = Guid.NewGuid();
        var entity = new TestEntity(id);

        Assert.Equal(id, entity.Id);
    }

    [Fact]
    public void Entities_WithSameId_ShouldBeEqual()
    {
        var id = Guid.NewGuid();
        var entity1 = new TestEntity(id);
        var entity2 = new TestEntity(id);

        Assert.Equal(entity2, entity1);
        Assert.True(entity1 == entity2);
    }

    [Fact]
    public void Entities_WithDifferentId_ShouldNotBeEqual()
    {
        var entity1 = new TestEntity(Guid.NewGuid());
        var entity2 = new TestEntity(Guid.NewGuid());

        Assert.NotEqual(entity2, entity1);
        Assert.True(entity1 != entity2);
    }

    [Fact]
    public void Entity_ComparedWithNull_ShouldNotBeEqual()
    {
        var entity = new TestEntity(Guid.NewGuid());

        Assert.False(entity.Equals(null));
        Assert.False(entity == null);
    }

    [Fact]
    public void Entities_WithSameId_ShouldHaveSameHashCode()
    {
        var id = Guid.NewGuid();
        var entity1 = new TestEntity(id);
        var entity2 = new TestEntity(id);

        Assert.Equal(entity2.GetHashCode(), entity1.GetHashCode());
    }

    [Fact]
    public void BothNull_ShouldBeEqual()
    {
        TestEntity? entity1 = null;
        TestEntity? entity2 = null;

        Assert.True(entity1 == entity2);
    }
}
