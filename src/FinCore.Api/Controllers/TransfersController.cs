using FinCore.Application.Features.Transfers.Transfer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinCore.Api.Controllers;

[ApiController]
[Route("api/transfers")]
[Authorize]
public sealed class TransfersController(TransferMoneyHandler handler) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TransferMoneyResult>> Transfer(
        TransferRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId == Guid.Empty)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");

        try
        {
            return Ok(await handler.HandleAsync(new TransferMoneyCommand(
                userId, request.SourceAccountId, request.DestinationAccountId, request.Amount), cancellationToken));
        }
        catch (TransferValidationException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid transfer input");
        }
        catch (TransferAccountAccessDeniedException)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Transfer forbidden");
        }
        catch (TransferAccountNotFoundException)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Account not found");
        }
        catch (TransferBusinessRuleException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Transfer cannot be completed");
        }
        catch (TransferConcurrencyException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Transfer conflict", detail: "Account data changed. Please retry the transfer.");
        }
    }
}

public sealed record TransferRequest(Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);
