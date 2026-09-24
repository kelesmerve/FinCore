using FinCore.Application.Features.Users.ListUsers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinCore.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminController(ListUsersHandler handler) : ControllerBase
{
    [HttpGet("users")]
    public async Task<ActionResult<ListUsersResult>> ListUsers(
        CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        try
        {
            return Ok(await handler.HandleAsync(new ListUsersQuery(page, pageSize), cancellationToken));
        }
        catch (ListUsersValidationException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid pagination", detail: exception.Message);
        }
    }
}
