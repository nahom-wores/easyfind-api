
namespace EasyFind.Api.Models.Dto.Common
{
    public class ApiRequest
    {
        public SD.ApiType ApiType { get; set; } = SD.ApiType.GET;
        public string Url { get; set; }
        public Object Data { get; set; }
    }
}
