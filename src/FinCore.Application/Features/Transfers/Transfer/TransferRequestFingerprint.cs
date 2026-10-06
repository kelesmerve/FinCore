using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FinCore.Application.Features.Transfers.Transfer;

public static class TransferRequestFingerprint
{
    public static string Compute(Guid sourceAccountId, Guid destinationAccountId, decimal amount)
    {
        var canonical = string.Concat(
            sourceAccountId.ToString("N"), "|", destinationAccountId.ToString("N"), "|",
            amount.ToString("0.############################", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
