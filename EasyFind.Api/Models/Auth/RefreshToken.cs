using System.ComponentModel.DataAnnotations;

namespace EasyFind.Api.Models.Auth
{
    public class RefreshToken
    {
        [Key]
        public int RefreshTokenId { get; set; }
        public string UserId { get; set; }
        public string JwtTokenId { get; set; }
        public string Refresh_Token { get; set; }
        //makes sure the refresh token is valid for one use
        public bool IsValid { get; set; }
        public bool IsUsed { get; set; }
        public DateTimeOffset ExpireDate { get; set; }
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpireDate;
        public bool IsActive => IsValid && !IsUsed && !IsExpired;
    }
}
