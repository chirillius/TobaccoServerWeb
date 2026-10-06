using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Newtonsoft.Json;
using OpenCvSharp;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using System.Runtime.InteropServices;
using TobaccoEntities.Models;
using Rectangle = TobaccoEntities.Models.Rectangle;


namespace TobacoServer.Services
{
    public class DefectImageService
    {
        protected string _defectImageAddress = System.Configuration.ConfigurationManager.AppSettings["DefectImageServiceAddress"];
        HttpClient client = new HttpClient();
        public async Task<(Mat Image, Zone Zone, IEnumerable<Rectangle> Bboxes)> GetImageWithBboxAsync(string imagePath)
        {
            var response = await client.PostAsJsonAsync(Path.Combine(_defectImageAddress, "image"), new Dictionary<string, string> { { "relativePath", imagePath } });

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            ms.Position = 0;
            using var imageData = Image.Load(ms);

            imageData.Metadata.ExifProfile.TryGetValue(ExifTag.UserComment, out var profile);
            if (profile == null)
                throw new Exception("Exif UserComment not found");

            var data = profile.Value.ToString();

            var metadata = new
            {
                Zone = new Zone(),
                Bboxes = new List<Rectangle>()
            };

            var result = JsonConvert.DeserializeAnonymousType(data, metadata);

            ms.Position = 0;
            var image = Mat.FromStream(ms, ImreadModes.Unchanged);

            return (image, result.Zone, result.Bboxes);

        }

        public async Task<List<(Mat Image, Zone Zone, IEnumerable<Rectangle> Bboxes)>> GetImageWithBboxAsync(List<string> imagePaths)
        {
            var result = new List<(Mat, Zone, IEnumerable<Rectangle>)>();

            foreach (var imagePath in imagePaths)
            {
                var image = await GetImageWithBboxAsync(imagePath);
                result.Add(image);
            }

            return result;
        }

        public async Task<Mat> GetImageWithResultAsync(string imagePath)
        {
            var response = await client.PostAsJsonAsync(Path.Combine(_defectImageAddress, "image"), new Dictionary<string, string> { { "relativePath", imagePath } });

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);

            ms.Position = 0;
            var imageData = Image.Load(ms);

            ms.Position = 0;
            var image = Mat.FromStream(ms, ImreadModes.Unchanged);

            return image;
        }

        public async Task<List<Mat>> GetImageWithResultAsync(List<string> imagePaths)
        {
            var result = new List<Mat>();

            foreach (var imagePath in imagePaths)
            {
                var image = await GetImageWithResultAsync(imagePath);
                result.Add(image);
            }

            return result;
        }



        public async Task MoveDefectImagesWithDateAsync(string defect, string date, List<string> imagePaths)
        {
            var response = await client.PostAsJsonAsync(Path.Combine(_defectImageAddress, $"images/move-date/{defect}-{date}"), imagePaths);
        }

        public async Task MoveImagesToFalsePositiveWithDateAsync(string defect, string date, List<string> imagePaths)
        {
            var response = await client.PostAsJsonAsync(
                Path.Combine(_defectImageAddress, $"images/move-false-positive-date/{defect}-{date}"),
                imagePaths);
        }

        public async Task MoveDefectImagesAsync(string defect, long id, List<string> imagePaths)
        {
            var response = await client.PostAsJsonAsync(Path.Combine(_defectImageAddress, $"images/move/{defect}-{id}"), imagePaths);
        }
    }
}
