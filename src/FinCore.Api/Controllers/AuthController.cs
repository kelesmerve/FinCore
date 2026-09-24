using System.Net.Mail;
using FinCore.Application.Features.Users.Login;
using Microsoft.AspNetCore.Authorization;
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

    [HttpPost("login")]
    public async Task<ActionResult<LoginUserResult>> Login(
        LoginRequest request, [FromServices] LoginUserHandler handler, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) ||
            request.Email.Trim().Length > 320 ||
            !MailAddress.TryCreate(request.Email.Trim(), out var address) ||
            !string.Equals(address.Address, request.Email.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid login input");
        }

        try
        {
            return Ok(await handler.HandleAsync(
                new LoginUserCommand(request.Email, request.Password), cancellationToken));
        }
        catch (InvalidCredentialsException)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password");
        }
        catch (InactiveUserException)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Account is inactive");
        }
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        UserId = User.FindFirst("sub")?.Value,
        Email = User.FindFirst("email")?.Value,
        Role = User.FindFirst("role")?.Value
    });
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

public sealed record LoginRequest(string Email, string Password);

public sealed record RegisterRequest(string Email, string Password);
