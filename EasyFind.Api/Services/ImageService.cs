using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.AccessControl;
using EasyFind.Api.Services.IServices;
using EasyFind.Api.Services.Models;
using ResourceType = CloudinaryDotNet.Actions.ResourceType;

namespace EasyFind.Api.Services
{
    /// <summary>
    /// Handles all image operations with Cloudinary
    /// KEY CONCEPTS:
    /// - Cloudinary organizes images in "folders" (like property1/, property2/)
    /// - Each image gets a unique "public_id" (identifier)
    /// - Returns secure HTTPS URLs for direct use in frontend
    /// </summary>
    public class ImageService : IImageService
    {
        private readonly Cloudinary _cloudinary;
        private readonly ILogger<ImageService> _logger;

        // Maximum file size: 5MB (adjust as needed)
        private const long MaxFileSize = 10 * 1024 * 1024;

        // Allowed file types
        private readonly string[] _allowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

        public ImageService(IOptions<CloudinarySettings> config, ILogger<ImageService> logger)
        {
            _logger = logger;

            // Initialize Cloudinary with credentials from appsettings.json
            var account = new Account(
                config.Value.CloudName,
                config.Value.ApiKey,
                config.Value.ApiSecret
            );

            _cloudinary = new Cloudinary(account);
            _cloudinary.Api.Secure = true; // Always use HTTPS URLs
        }

        /// <summary>
        /// Upload a single image to Cloudinary
        /// PROCESS: Validate → Upload → Return URL
        /// </summary>
        public async Task<string> UploadImageAsync(IFormFile file, string folder = "field")
         {
            // STEP 1: Validation
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File is empty or null");
            }

            // Check file size
            if (file.Length > MaxFileSize)
            {
                throw new ArgumentException($"File size exceeds maximum allowed size of {MaxFileSize / 1024 / 1024}MB");
            }

            // Check file extension
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!_allowedExtensions.Contains(extension))
            {
                throw new ArgumentException($"File type {extension} is not allowed. Allowed types: {string.Join(", ", _allowedExtensions)}");
            }

            try
            {
                // STEP 2: Prepare upload parameters
                // Generate unique filename to avoid conflicts
                var fileName = $"{Guid.NewGuid()}{extension}";

                // Open file stream
                using var stream = file.OpenReadStream();

                var uploadParams = new ImageUploadParams
                {
                    // File to upload
                    File = new FileDescription(fileName, stream),

                    // Where to store in Cloudinary (e.g., "field/abc-123-def")
                    Folder = folder,

                    // Unique identifier for this image
                    PublicId = Path.GetFileNameWithoutExtension(fileName),

                    // Automatic transformations
                    Transformation = new Transformation()
                        .Quality("auto") // Auto-optimize quality
                        .FetchFormat("auto"), // Auto-choose best format (WebP if supported)

                    // Overwrite if exists (safety)
                    Overwrite = false
                };

                // STEP 3: Upload to Cloudinary
                var uploadResult = await _cloudinary.UploadAsync(uploadParams);

                // STEP 4: Check if upload was successful
                if (uploadResult.StatusCode == HttpStatusCode.OK)
                {
                    _logger.LogInformation("Image uploaded successfully: {Url}", uploadResult.SecureUrl);

                    // Return the HTTPS URL
                    return uploadResult.SecureUrl.ToString();
                }
                else
                {
                    _logger.LogError("Image upload failed: {Error}", uploadResult.Error?.Message);
                    throw new Exception($"Image upload failed: {uploadResult.Error?.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading image to Cloudinary");
                throw;
            }
        }

        /// <summary>
        /// Upload multiple images at once
        /// Useful for property listings with multiple photos
        /// </summary>
        public async Task<List<string>> UploadMultipleImagesAsync(List<IFormFile> files, string folder = "properties")
        {
            if (files == null || files.Count == 0)
            {
                throw new ArgumentException("No files provided");
            }

            var uploadedUrls = new List<string>();

            // Upload each file
            foreach (var file in files)
            {
                try
                {
                    var url = await UploadImageAsync(file, folder);
                    uploadedUrls.Add(url);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload image: {FileName}", file.FileName);

                    // If one fails, continue with others
                    // You could also rollback all uploads if one fails
                    // For MVP, we'll continue
                }
            }

            if (uploadedUrls.Count == 0)
            {
                throw new Exception("All image uploads failed");
            }

            _logger.LogInformation("Uploaded {Count} out of {Total} images", uploadedUrls.Count, files.Count);

            return uploadedUrls;
        }

        /// <summary>
        /// Delete an image from Cloudinary
        /// IMPORTANT: Extract public_id from URL first
        /// </summary>
        public async Task<bool> DeleteImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return false;
            }

            try
            {
                // STEP 1: Extract public_id from Cloudinary URL
                // Example URL: https://res.cloudinary.com/demo/image/upload/v1234/properties/abc-123.jpg
                // Public ID: properties/abc-123
                var publicId = ExtractPublicIdFromUrl(imageUrl);

                if (string.IsNullOrEmpty(publicId))
                {
                    _logger.LogWarning("Could not extract public ID from URL: {Url}", imageUrl);
                    return false;
                }

                // STEP 2: Delete from Cloudinary
                var deleteParams = new DeletionParams(publicId)
                {
                    ResourceType = ResourceType.Image
                };

                var result = await _cloudinary.DestroyAsync(deleteParams);

                if (result.StatusCode == System.Net.HttpStatusCode.OK && result.Result == "ok")
                {
                    _logger.LogInformation("Image deleted successfully: {PublicId}", publicId);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Image deletion failed: {Error}", result.Error?.Message);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting image from Cloudinary: {Url}", imageUrl);
                return false;
            }
        }

        /// <summary>
        /// Delete multiple images at once
        /// Used when deleting a property or removing multiple photos
        /// </summary>
        public async Task<bool> DeleteMultipleImagesAsync(List<string> imageUrls)
        {
            if (imageUrls == null || imageUrls.Count == 0)
            {
                return true; // Nothing to delete
            }

            var successCount = 0;

            foreach (var url in imageUrls)
            {
                var deleted = await DeleteImageAsync(url);
                if (deleted)
                {
                    successCount++;
                }
            }

            _logger.LogInformation("Deleted {Success} out of {Total} images", successCount, imageUrls.Count);

            // Return true if at least one deletion succeeded
            return successCount > 0;
        }

        /// <summary>
        /// Helper: Extract Cloudinary public_id from image URL
        /// Example: https://res.cloudinary.com/demo/image/upload/v1234/properties/abc-123.jpg
        /// Returns: properties/abc-123
        /// </summary>
        private string ExtractPublicIdFromUrl(string imageUrl)
        {
            try
            {
                // Cloudinary URL structure:
                // https://res.cloudinary.com/{cloud_name}/image/upload/v{version}/{public_id}.{format}

                var uri = new Uri(imageUrl);
                var segments = uri.AbsolutePath.Split('/');

                // Find "upload" segment
                var uploadIndex = Array.IndexOf(segments, "upload");
                if (uploadIndex == -1 || uploadIndex + 2 >= segments.Length)
                {
                    return null;
                }

                // Get everything after version (v1234)
                var publicIdParts = segments.Skip(uploadIndex + 2).ToList();

                // Remove file extension from last part
                var lastPart = publicIdParts.Last();
                var lastPartWithoutExtension = Path.GetFileNameWithoutExtension(lastPart);
                publicIdParts[publicIdParts.Count - 1] = lastPartWithoutExtension;

                // Join back together
                return string.Join("/", publicIdParts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting public ID from URL: {Url}", imageUrl);
                return null;
            }
        }
    }
}
