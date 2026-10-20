namespace HMS.Domain.Common;

/// <summary>Base type for every persisted record.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}
