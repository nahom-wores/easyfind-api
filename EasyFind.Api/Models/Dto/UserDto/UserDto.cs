namespace EasyFind.Api.Models.Dto.UserDto
{
    public class UserDto
    {      
        public string Id { get; set; } 
        public string UserName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string RegisterationErrors { get; set; }
        public bool IsSuccess { get; set; } = true;

    }
}
