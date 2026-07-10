using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ClinicQueue.Domain.Identity;
using ClinicQueue.Infrastructure.Authorization;
using ClinicQueue.Infrastructure.Persistence;
using ClinicQueue.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ClinicQueue.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "ClinicQueue";
    public string Audience { get; set; } = "ClinicQueue";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public string SigningKey { get; set; } = string.Empty;
}

public sealed class TokenPair
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTime AccessExpiresAt { get; init; }
    public required DateTime RefreshExpiresAt { get; init; }
}

public interface ITokenService
{
    Task<TokenPair> IssueAsync(User user, Guid? clinicId, IReadOnlyList<string> roles, CancellationToken cancellationToken = default);
    Task<TokenPair?> RotateRefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task RevokeRefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    void WriteAuthCookies(HttpResponse response, TokenPair tokens);
    void ClearAuthCookies(HttpResponse response);
}

public sealed class TokenService : ITokenService
{
    public const string AccessCookieName = "cq_access";
    public const string RefreshCookieName = "cq_refresh";
    public const string CsrfCookieName = "cq_csrf";

    private readonly AppDbContext _db;
    private readonly JwtOptions _options;

    public TokenService(AppDbContext db, IOptions<JwtOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<TokenPair> IssueAsync(
        User user,
        Guid? clinicId,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken = default)
    {
        var accessExpires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpires = DateTime.UtcNow.AddDays(_options.RefreshTokenDays);

        var accessToken = CreateAccessToken(user, clinicId, roles, accessExpires);
        var rawRefresh = GenerateSecureToken();
        var refreshEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawRefresh),
            ExpiresAt = refreshExpires,
            ClinicId = clinicId
        };

        _db.RefreshTokens.Add(refreshEntity);
        await _db.SaveChangesAsync(cancellationToken);

        return new TokenPair
        {
            AccessToken = accessToken,
            RefreshToken = rawRefresh,
            AccessExpiresAt = accessExpires,
            RefreshExpiresAt = refreshExpires
        };
    }

    public async Task<TokenPair?> RotateRefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(refreshToken);
        var existing = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (existing is null || !existing.IsActive(DateTime.UtcNow))
        {
            return null;
        }

        existing.RevokedAt = DateTime.UtcNow;

        var roles = await _db.ClinicMemberships
            .AsNoTracking()
            .Where(m => m.UserId == existing.UserId && m.Status == MembershipStatus.Active)
            .Select(m => m.Role.ToString())
            .Distinct()
            .ToListAsync(cancellationToken);

        if (existing.User.IsSuperAdmin)
        {
            roles.Add(Roles.SuperAdmin);
        }

        var accessExpires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpires = DateTime.UtcNow.AddDays(_options.RefreshTokenDays);
        var rawRefresh = GenerateSecureToken();
        var replacement = new RefreshToken
        {
            UserId = existing.UserId,
            TokenHash = HashToken(rawRefresh),
            ExpiresAt = refreshExpires,
            ClinicId = existing.ClinicId
        };
        _db.RefreshTokens.Add(replacement);
        await _db.SaveChangesAsync(cancellationToken);

        existing.ReplacedByTokenId = replacement.Id;
        await _db.SaveChangesAsync(cancellationToken);

        return new TokenPair
        {
            AccessToken = CreateAccessToken(existing.User, existing.ClinicId, roles, accessExpires),
            RefreshToken = rawRefresh,
            AccessExpiresAt = accessExpires,
            RefreshExpiresAt = refreshExpires
        };
    }

    public async Task RevokeRefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(refreshToken);
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (existing is null)
        {
            return;
        }

        existing.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public void WriteAuthCookies(HttpResponse response, TokenPair tokens)
    {
        const bool secure = true;
        response.Cookies.Append(AccessCookieName, tokens.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Expires = tokens.AccessExpiresAt,
            Path = "/"
        });
        response.Cookies.Append(RefreshCookieName, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Expires = tokens.RefreshExpiresAt,
            Path = "/api/v1/auth"
        });

        var csrf = GenerateSecureToken()[..32];
        response.Cookies.Append(CsrfCookieName, csrf, new CookieOptions
        {
            HttpOnly = false,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Expires = tokens.AccessExpiresAt,
            Path = "/"
        });
    }

    public void ClearAuthCookies(HttpResponse response)
    {
        response.Cookies.Delete(AccessCookieName, new CookieOptions { Path = "/" });
        response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = "/api/v1/auth" });
        response.Cookies.Delete(CsrfCookieName, new CookieOptions { Path = "/" });
    }

    private string CreateAccessToken(User user, Guid? clinicId, IReadOnlyList<string> roles, DateTime expires)
    {
        if (string.IsNullOrWhiteSpace(_options.SigningKey) || _options.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be configured (env/Docker secret, ≥32 chars).");
        }

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString())
        };

        if (clinicId.HasValue)
        {
            claims.Add(new Claim(JwtTenantProvider.ClinicIdClaimType, clinicId.Value.ToString()));
        }

        foreach (var role in roles.Distinct())
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private static string HashToken(string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}
