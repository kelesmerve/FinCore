using System.Net.Mail;
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
        if (string.IsNullOrWhiteSpace(command.Email))
        {
            throw new RegistrationValidationException("Email is required.");
        }

        var email = command.Email.Trim().ToLowerInvariant();
        if (email.Length > 320 || !MailAddress.TryCreate(email, out var address) || address.Address != email)
        {
            throw new RegistrationValidationException("Email must be a valid address of at most 320 characters.");
        }

        var password = command.Password;
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || password.Length > 128
            || !password.Any(char.IsUpper) || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character)))
        {
            throw new RegistrationValidationException(
                "Password must contain 8-128 characters, including uppercase, lowercase, digit and special character.");
        }

        if (await _store.EmailExistsAsync(email, cancellationToken))
        {
            throw new DuplicateEmailException();
        }

        var passwordHash = _passwordHasher.Hash(password);
        var user = User.CreateCustomer(email, passwordHash);
        var account = new Account(user.Id, _accountNumberGenerator.Generate());
        await _store.AddAsync(user, account, cancellationToken);
        await _store.SaveChangesAsync(cancellationToken);

        return new RegisterUserResult(user.Id, account.Id, account.AccountNumber, user.Email);
    }
}
