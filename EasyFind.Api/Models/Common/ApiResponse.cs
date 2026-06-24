using System.Net;

namespace EasyFind.Api.Models.Dto.Common
{
    public class ApiResponse
    {
        public ApiResponse()
        {
            ErrorMessage = new List<string>();
        }
        public HttpStatusCode StatusCode { get; set; } 
        public bool IsSuccess { get; set; }
        public List<string> ErrorMessage { get; set; }
        public object Result { get; set; }
    }
}
