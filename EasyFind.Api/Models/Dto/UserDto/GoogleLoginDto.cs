namespace EasyFind.Api.Models.Dto.UserDto
{
    /// <summary>
    /// Request sent from frontend containing Google ID Token
    /// Frontend gets this token from Google after user signs in
    /// </summary>
    public class GoogleLoginDto
    {
        public string IdToken { get; set; }
    }

    /// <summary>
    /// User information extracted from Google token
    /// </summary>
    public class GoogleUserInfo
    {
        public string Email { get; set; }
        public string Name { get; set; }
        public string GivenName { get; set; }   // First name
        public string FamilyName { get; set; }  // Last name
        public string Picture { get; set; }     // Profile photo URL
        public string GoogleId { get; set; }    // Unique Google user ID
        public bool EmailVerified { get; set; }
    }
}
