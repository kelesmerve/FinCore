namespace FinCore.Application.Features.Accounts.GetMyAccounts;

public sealed record AccountListItem(
    Guid Id,
    string AccountNumber,
    decimal Balance,
    string Currency,
    string Status,
    DateTime CreatedAtUtc);
