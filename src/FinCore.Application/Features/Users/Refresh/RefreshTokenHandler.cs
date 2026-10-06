using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Features.Users.Login;
using FinCore.Application.Security;

namespace FinCore.Application.Features.Users.Refresh;

public sealed class RefreshTokenHandler(
    IRefreshTokenStore store, IRefreshTokenGenerator generator, IJwtTokenGenerator jwt)
{
    public async Task<LoginUserResult> HandleAsync(
        RefreshTokenCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null || string.IsNullOrWhiteSpace(command.RefreshToken) || command.RefreshToken.Length > 512)
            throw new InvalidRefreshTokenException();

        var hash = generator.Hash(command.RefreshToken);
        var token = await store.FindByHashAsync(hash, cancellationToken);
        if (token is null)
            throw new InvalidRefreshTokenException();
        var user = await store.FindUserAsync(token.UserId, cancellationToken);
        var now = DateTime.UtcNow;
        if (!token.IsActive(now) || user is null || !user.IsActive)
            throw new InvalidRefreshTokenException();

        var material = generator.Generate();
        var replacement = token.Rotate(material.TokenHash, now, now.AddDays(7));
        var access = jwt.Generate(user);
        await store.AddAsync(replacement, cancellationToken);
        await store.SaveChangesAsync(cancellationToken);
        return new LoginUserResult(access.Token, access.ExpiresAtUtc, material.RawToken, replacement.ExpiresAtUtc);
    }
}
