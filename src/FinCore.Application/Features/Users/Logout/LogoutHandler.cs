using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Security;

namespace FinCore.Application.Features.Users.Logout;

public sealed class LogoutHandler(IRefreshTokenStore store, IRefreshTokenGenerator generator)
{
    public async Task HandleAsync(LogoutCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.RefreshToken) || command.RefreshToken.Length > 512)
            throw new LogoutValidationException();

        var hash = generator.Hash(command.RefreshToken);
        var token = await store.FindByHashAsync(hash, cancellationToken);
        if (token is null)
            return;

        var family = await store.FindUnrevokedFamilyAsync(token.UserId, token.FamilyId, cancellationToken);
        if (family.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var member in family)
            member.Revoke(now);
        await store.SaveChangesAsync(cancellationToken);
    }
}
