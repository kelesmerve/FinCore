using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinCore.Application.Security;
using FinCore.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FinCore.Infrastructure.Security;

public sealed class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;

    public JwtTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.SecretKey) ||
            Encoding.UTF8.GetByteCount(_options.SecretKey) < 32)
            throw new InvalidOperationException("Jwt:SecretKey must contain at least 32 UTF-8 bytes.");
        if (string.IsNullOrWhiteSpace(_options.Issuer) || string.IsNullOrWhiteSpace(_options.Audience))
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience are required.");
        if (_options.AccessTokenExpirationMinutes <= 0)
            throw new InvalidOperationException("Jwt:AccessTokenExpirationMinutes must be positive.");
    }

    public JwtTokenResult Generate(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenExpirationMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("role", user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims,
            notBefore: now, expires: expires, signingCredentials: credentials);
        return new JwtTokenResult(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
