namespace EasyFind.Api.Services.IServices
{
    /// <summary>
    /// Service for handling image uploads to Cloudinary
    /// Manages uploading, deleting, and organizing property images
    /// </summary>
    public interface IImageService
    {
        /// <summary>
        /// Upload a single image to Cloudinary
        /// Returns the public URL of uploaded image
        /// </summary>
        Task<string> UploadImageAsync(IFormFile file, string folder = "properties");

        /// <summary>
        /// Upload multiple images for a property
        /// Returns list of uploaded image URLs
        /// </summary>
        Task<List<string>> UploadMultipleImagesAsync(List<IFormFile> files, string folder = "properties");

        /// <summary>
        /// Delete an image from Cloudinary using its URL
        /// </summary>
        Task<bool> DeleteImageAsync(string imageUrl);

        Task<bool> DeleteMultipleImagesAsync(List<string> imageUrls);
    }
}
