using System.Net;

namespace EasyFind.Api.Models.Dto.Common
{
    public class ApiResponse
    {
        public bool IsSuccess { get; set; }
        public List<string> Errors { get; set; } = [];
        public object? Result { get; set; }
    }
}
