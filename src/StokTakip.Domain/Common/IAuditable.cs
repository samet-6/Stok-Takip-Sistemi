namespace StokTakip.Domain.Common;

public interface IAuditable : IHasCreatedAt
{
    DateTime UpdatedAt { get; set; }
}
