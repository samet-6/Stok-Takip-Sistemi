namespace StokTakip.Application.Users;

public interface IUserService
{
    Task<IReadOnlyList<UserListDto>> GetAllAsync(CancellationToken ct);
    Task<UserListDto> CreateAsync(CreateUserRequest request, CancellationToken ct);
    Task UpdateAsync(string id, UpdateUserRequest request, CancellationToken ct);
    Task SetStatusAsync(string id, UpdateUserStatusRequest request, CancellationToken ct);
}
