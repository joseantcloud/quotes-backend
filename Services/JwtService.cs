using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AzureQuotes.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace AzureQuotes.Api.Services;

public sealed class JwtService(IConfiguration configuration)
{
    public string CreateToken(AppUser user)
    {
        var secret = configuration["JWT_SECRET_KEY"] ?? "dev-jwt-secret-change-me-please-use-a-long-secret";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email)
        };

        var token = new JwtSecurityToken(
            issuer: "azure-quotes-api",
            audience: "azure-quotes-client",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
