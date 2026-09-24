using FinCore.Domain.Entities;

namespace FinCore.Application.Security;

public interface IJwtTokenGenerator
{
    JwtTokenResult Generate(User user);
}
