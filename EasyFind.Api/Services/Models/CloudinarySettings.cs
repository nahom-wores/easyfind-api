namespace EasyFind.Api.Services.Models
{
    /// <summary>
    /// Cloudinary account settings
    /// Loaded from appsettings.json
    /// </summary>
    public class CloudinarySettings
    {
        public string CloudName { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
    }
}
