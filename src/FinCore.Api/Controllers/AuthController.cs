using FinCore.Application.Features.Users.Register;
using Microsoft.AspNetCore.Mvc;

namespace FinCore.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly RegisterUserHandler _handler;

    public AuthController(RegisterUserHandler handler)
    {
        _handler = handler;
    }

    [HttpPost("register")]
    public async Task<ActionResult<RegisterUserResult>> Register(
        RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _handler.HandleAsync(
                new RegisterUserCommand(request.Email, request.Password), cancellationToken);
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (RegistrationValidationException exception)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Registration validation failed", detail: exception.Message);
        }
        catch (DuplicateEmailException exception)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Email already registered", detail: exception.Message);
        }
    }
}

public sealed record RegisterRequest(string Email, string Password);
