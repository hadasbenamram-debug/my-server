using DAL.Classes;
using System;

namespace BLL.Interfaces
{
    /// <summary>JWT string together with its absolute UTC expiry.</summary>
    public sealed record TokenResult(string Token, DateTime ExpiresAt);

    /// <summary>
    /// The single place where login (session) JWTs are created.
    /// Links in e-mails no longer carry a JWT: they carry a one-time, single-purpose token
    /// stored (hashed) in the ActionTokens table - see IActionTokenService.
    /// </summary>
    public interface ITokenService
    {
        TokenResult CreateToken(User user);
    }
}
