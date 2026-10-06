using FinCore.Application.Features.Transfers.Transfer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinCore.Api.Controllers;

[ApiController]
[Route("api/transfers")]
[Authorize]
public sealed class TransfersController(IdempotentTransferHandler handler) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<TransferMoneyResult>> Transfer(
        TransferRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId == Guid.Empty)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");

        if (!Request.Headers.TryGetValue("Idempotency-Key", out var values) || values.Count != 1 ||
            string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > 128)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid Idempotency-Key");

        try
        {
            var result = await handler.HandleAsync(new TransferMoneyCommand(
                userId, request.SourceAccountId, request.DestinationAccountId, request.Amount),
                values[0], cancellationToken);
            if (result.IsReplay) Response.Headers["Idempotency-Replayed"] = "true";
            return Ok(result.Transfer);
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
        catch (IdempotencyConflictException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Idempotency conflict");
        }
    }
}

public sealed record TransferRequest(Guid SourceAccountId, Guid DestinationAccountId, decimal Amount);
