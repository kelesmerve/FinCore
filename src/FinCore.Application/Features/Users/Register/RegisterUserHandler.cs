using FinCore.Application.Abstractions.Persistence;
using FinCore.Application.Abstractions.Security;
using FinCore.Application.Security;
using FinCore.Domain.Entities;

namespace FinCore.Application.Features.Users.Register;

public sealed class RegisterUserHandler
{
    private readonly IUserRegistrationStore _store;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAccountNumberGenerator _accountNumberGenerator;

    public RegisterUserHandler(
        IUserRegistrationStore store,
        IPasswordHasher passwordHasher,
        IAccountNumberGenerator accountNumberGenerator)
    {
        _store = store;
        _passwordHasher = passwordHasher;
        _accountNumberGenerator = accountNumberGenerator;
    }

    public async Task<RegisterUserResult> HandleAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var email = RegistrationInputValidator.NormalizeEmail(command.Email);
        RegistrationInputValidator.ValidatePassword(command.Password);

        if (await _store.EmailExistsAsync(email, cancellationToken))
        {
            throw new DuplicateEmailException();
        }

        var passwordHash = _passwordHasher.Hash(command.Password);
        var user = User.CreateCustomer(email, passwordHash);
        var account = new Account(user.Id, _accountNumberGenerator.Generate());
        await _store.AddAsync(user, account, cancellationToken);
        await _store.SaveChangesAsync(cancellationToken);

        return new RegisterUserResult(user.Id, account.Id, account.AccountNumber, user.Email);
    }
}
