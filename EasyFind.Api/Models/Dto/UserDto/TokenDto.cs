namespace EasyFind.Api.Models.Dto.UserDto
{
    public class TokenDto
    {
        // Defaults to FALSE deliberately. It used to default to true, which meant
        // every `return new TokenDto()` on a failure path handed the client a
        // response claiming success with a null AccessToken — the refresh
        // endpoint reported failures as successes for exactly that reason. A
        // default that lies is worse than no default; success is now something a
        // caller has to state.
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
    }
}
