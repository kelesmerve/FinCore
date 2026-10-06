using FinCore.Application.Features.Accounts.GetMyAccounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinCore.Api.Controllers;

[ApiController]
[Route("api/accounts")]
[Authorize]
public sealed class AccountsController(GetMyAccountsHandler handler) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<IReadOnlyCollection<AccountListItem>>> GetMine(
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId == Guid.Empty)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");

        return Ok(await handler.HandleAsync(new GetMyAccountsQuery(userId), cancellationToken));
    }
}
