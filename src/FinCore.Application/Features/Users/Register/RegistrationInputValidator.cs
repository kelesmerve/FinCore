using System.Net.Mail;

namespace FinCore.Application.Features.Users.Register;

internal static class RegistrationInputValidator
{
    public static string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new RegistrationValidationException("Email is required.");

        var normalized = email.Trim().ToLowerInvariant();
        if (normalized.Length > 320 || !MailAddress.TryCreate(normalized, out var address) ||
            address.Address != normalized)
            throw new RegistrationValidationException("Email must be a valid address of at most 320 characters.");
        return normalized;
    }

    public static void ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || password.Length > 128
            || !password.Any(char.IsUpper) || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character)))
            throw new RegistrationValidationException(
                "Password must contain 8-128 characters, including uppercase, lowercase, digit and special character.");
    }
}
