using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Site.Services
{
    public class ImageUploadService
    {
        private readonly Cloudinary? _cloudinary;
        private readonly bool _useCloudinary;

        public ImageUploadService(IConfiguration config)
        {
            var cloudName = config["CloudinarySettings:CloudName"];
            var apiKey = config["CloudinarySettings:ApiKey"];
            var apiSecret = config["CloudinarySettings:ApiSecret"];

            if (!string.IsNullOrEmpty(cloudName) && !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
            {
                var account = new Account(cloudName, apiKey, apiSecret);
                _cloudinary = new Cloudinary(account);
                _useCloudinary = true;
                Console.WriteLine("[IMAGE UPLOAD] Cloudinary initialized successfully.");
            }
            else
            {
                _useCloudinary = false;
                Console.WriteLine("[IMAGE UPLOAD] Cloudinary credentials not fully configured. Falling back to local file upload.");
            }
        }

        public async Task<string> UploadFileAsync(IFormFile file, string subFolder = "")
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File is empty or null.");
            }

            if (_useCloudinary && _cloudinary != null)
            {
                try
                {
                    using (var stream = file.OpenReadStream())
                    {
                        var uploadParams = new ImageUploadParams()
                        {
                            File = new FileDescription(file.FileName, stream),
                            Folder = string.IsNullOrEmpty(subFolder) ? "site_uploads" : $"site_uploads/{subFolder}",
                            // Keep original format or optimize
                            Transformation = new Transformation().Quality("auto").FetchFormat("auto")
                        };

                        var uploadResult = await _cloudinary.UploadAsync(uploadParams);
                        if (uploadResult.Error != null)
                        {
                            Console.WriteLine($"[CLOUDINARY ERROR] {uploadResult.Error.Message}. Falling back to local upload.");
                        }
                        else
                        {
                            return uploadResult.SecureUrl.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CLOUDINARY EXCEPTION] {ex.Message}. Falling back to local upload.");
                }
            }

            // Fallback: Local upload in wwwroot/uploads
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", subFolder);
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = Guid.NewGuid().ToString() + "_" + Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            // Return relative path for web serving
            var relativePath = string.IsNullOrEmpty(subFolder) 
                ? $"/uploads/{uniqueFileName}" 
                : $"/uploads/{subFolder}/{uniqueFileName}";

            // Normalize slashes for web urls
            return relativePath.Replace("\\", "/");
        }
    }
}
