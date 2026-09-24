using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Security;

namespace FinCore.Application.Features.Users.Login;

public sealed class LoginUserHandler
{
    private readonly IUserAuthenticationStore _store;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenGenerator _tokenGenerator;

    public LoginUserHandler(IUserAuthenticationStore store, IPasswordHasher hasher, IJwtTokenGenerator tokenGenerator)
    {
        _store = store;
        _hasher = hasher;
        _tokenGenerator = tokenGenerator;
    }

    public async Task<LoginUserResult> HandleAsync(LoginUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Email) || string.IsNullOrWhiteSpace(command.Password))
        {
            throw new InvalidCredentialsException();
        }

        var email = command.Email.Trim().ToLowerInvariant();
        var user = await _store.FindByEmailAsync(email, cancellationToken);
        if (user is null || !_hasher.Verify(user.PasswordHash, command.Password))
        {
            throw new InvalidCredentialsException();
        }

        if (!user.IsActive)
        {
            throw new InactiveUserException();
        }

        var token = _tokenGenerator.Generate(user);
        return new LoginUserResult(token.Token, token.ExpiresAtUtc);
    }
}
