using System.Diagnostics;
using System.IO;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using TobacoServer.Services;
using System.Configuration;
using System;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Reflection.Metadata;
using Azure;
using TobaccoEntities.Models;
using System.Globalization;
using System.IO.Compression;
using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;

namespace TobacoServer.Controllers
{
    [Route("[controller]")]
    public class ArchiveRetrievalController : Controller
    {
        private static object _zipLock = new object();
        private static List<string> _runningZips = new List<string>();
        private string _videosDirectory = Path.Combine(Directory.GetCurrentDirectory(), System.Configuration.ConfigurationManager.AppSettings["VideosDirectory"]);
        private string _tempDirectory = Path.Combine(Directory.GetCurrentDirectory(), System.Configuration.ConfigurationManager.AppSettings["TempDirectory"]);
        private string _archivesDirectory = Path.Combine(Directory.GetCurrentDirectory(), System.Configuration.ConfigurationManager.AppSettings["ArchivesDirectory"]);

        private ILogger _logger;
        private IServiceScope _scope;

        public ArchiveRetrievalController(ILogger logger, IServiceProvider serviceProvider)
        {
            Directory.CreateDirectory(_videosDirectory);
            Directory.CreateDirectory(_tempDirectory);
            _logger = logger;
            _scope = serviceProvider.CreateScope();
        }


        [HttpGet]
        [Route("live-archive/merge/{cameraName}")]
        public async Task<IActionResult> Merge(string cameraName, [FromQuery] DateTime startDateTime, DateTime endDateTime)
        {
            var paths = ArchiveHelper.GetFilePathsWithinRangeOrderedByDateTime(_videosDirectory,
                startDateTime, endDateTime, cameraName);

            if (paths.Count == 0)
            {
                return NotFound("No such files.");
            }

            List<string> reservedDateFolderNames = paths.Select(x => x.Split("\\origin")[0]).Distinct().ToList();
            long totalFoldersLength = 0;

            foreach (var pathToVideoFolder in reservedDateFolderNames)
            {
                ArchiveHelper.StartSendingHeartbeat(pathToVideoFolder, TrackingType.Folder);
                totalFoldersLength += ArchiveHelper.GetFolderLength(pathToVideoFolder);
            }

            var firstDateTimeString = paths.First().Split("\\origin")[0].Split("\\").Last() + "-"
                + paths.First().Split("origin\\").Last().Split("\\").Last().Split("_")[0];
            var firstDateTime = DateTime.ParseExact(firstDateTimeString, "dd-MM-yyyy-HH-mm-ss.fff", CultureInfo.InvariantCulture);

            var lastDateTimeString = paths.Last().Split("\\origin")[0].Split("\\").Last() + "-"
                + paths.Last().Split("origin\\").Last().Split("\\").Last().Split("_")[1].Replace(".mp4", "");
            var lastDateTime = DateTime.ParseExact(lastDateTimeString, "dd-MM-yyyy-HH-mm-ss.fff", CultureInfo.InvariantCulture);

            var outputPath = $"{cameraName}_{firstDateTime}_{lastDateTime}.mp4".Replace(":", "-");
            outputPath = Path.Combine(_tempDirectory, outputPath);


            if (ArchiveHelper.IsMergeRunning(outputPath))
            {
                return StatusCode(202);
            }




            if (!System.IO.File.Exists(outputPath))
            {

                ArchiveHelper.StartSendingHeartbeat(outputPath, TrackingType.File);


                try
                {
                    ArchiveHelper.RequestPurgeIfNeeded(totalFoldersLength);

                }
                catch (Exception ex)
                {

                    return BadRequest();
                }

                await ArchiveHelper.MergeVideosWithNoAwait(paths, outputPath, Directory.GetCurrentDirectory());
                return StatusCode(202);
            }

            var fileInfo = new FileInfo(outputPath);

            ArchiveHelper.StopSendingHeartbeat(outputPath);

            foreach (var pathToVideoFolder in reservedDateFolderNames)
            {
                ArchiveHelper.StopSendingHeartbeat(pathToVideoFolder);
            }

            return Ok(fileInfo.Length);
        }


        [HttpGet]
        [Route("live-archive/{cameraName}")]
        public async Task<IActionResult> GetLongVideo(string cameraName, [FromQuery] DateTime startDateTime, DateTime endDateTime)
        {
            var paths = ArchiveHelper.GetFilePathsWithinRangeOrderedByDateTime(_videosDirectory,
                startDateTime, endDateTime, cameraName);
            if (paths.Count == 0)
            {
                return NotFound("No such files.");
            }
            var firstDateTimeString = paths.First().Split("\\origin")[0].Split("\\").Last() + "-"
                + paths.First().Split("origin\\").Last().Split("\\").Last().Split("_")[0];
            var firstDateTime = DateTime.ParseExact(firstDateTimeString, "dd-MM-yyyy-HH-mm-ss.fff", CultureInfo.InvariantCulture);

            var lastDateTimeString = paths.Last().Split("\\origin")[0].Split("\\").Last() + "-"
                + paths.Last().Split("origin\\").Last().Split("\\").Last().Split("_")[1].Replace(".mp4", "");
            var lastDateTime = DateTime.ParseExact(lastDateTimeString, "dd-MM-yyyy-HH-mm-ss.fff", CultureInfo.InvariantCulture);

            var filename = $"{cameraName}_{firstDateTime}_{lastDateTime}.mp4".Replace(":", "-");
            filename = Path.Combine(_tempDirectory, filename);

            ArchiveHelper.SendHeartbeat(filename, TrackingType.File);

            if (!System.IO.File.Exists(filename))
            {
                return BadRequest($"No such File {filename}");
            }
            var fileInfo = new FileInfo(filename);
            var fileLength = fileInfo.Length;

            var range = Request.Headers["Range"].ToString();

            if (string.IsNullOrEmpty(range))
            {
                Response.Headers.Append("Accept-Ranges", "bytes");
                return PhysicalFile(filename, "video/mp4");
            }

            var bytes = range.Replace("bytes=", "").Split('-');
            var start = long.Parse(bytes[0]);
            var end = bytes[1].Length > 0 ? long.Parse(bytes[1]) : fileLength - 1;

            Response.StatusCode = 206;
            Response.Headers.Append("Accept-Ranges", "bytes");
            Response.Headers.Append("Content-Range", $"bytes {start}-{end}/{fileLength}");
            Response.Headers.Append("Content-Length", (end - start + 1).ToString());


            return new FileStreamResult(new FileStream(filename, FileMode.Open, FileAccess.Read), "video/mp4")
            {
                EnableRangeProcessing = true
            };
        }

        [HttpPost]
        [Route("archive/checkZip")]
        public async Task<IActionResult> CheckZip([FromBody] List<DateTime> listStartDateTime)
        {
            var zipPath = $"{_archivesDirectory}/" +
            $"{listStartDateTime.First().ToString("dd-MM-yyyy")}_{listStartDateTime.Last().ToString("dd-MM-yyyy")}.zip";

            if (System.IO.File.Exists(zipPath))
            {
                return Ok(zipPath);
            }

            return StatusCode(404);
        }

        [HttpPost]
        [Route("archive/deleteZip")]
        public async Task<IActionResult> DeleteZip([FromBody] List<DateTime> listStartDateTime)
        {
            var zipPath = $"{_archivesDirectory}/" +
            $"{listStartDateTime.First().ToString("dd-MM-yyyy")}_{listStartDateTime.Last().ToString("dd-MM-yyyy")}.zip";

            if (!System.IO.File.Exists(zipPath))
            {
                return StatusCode(404);
            }

            try
            {
                System.IO.File.Delete(zipPath);
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500);
            }
        }

        [HttpPost]
        [Route("archive/merge/{cameraName}")]
        public async Task<IActionResult> MergeArchive(string cameraName, [FromBody] List<DateTime> neededDates)
        {
            try
            {

                var dates = neededDates.Where(x => x < DateTime.Now.Date).ToList();
                var skippedDates = new List<string>();
                if (!dates.Any())
                {
                    return NotFound("The videos list was empty.");
                }

                var foldersForBlocking = dates.Select(x =>
                {
                    if (Path.Exists(Path.Combine(Directory.GetCurrentDirectory(), _videosDirectory, x.ToString("dd-MM-yyyy"), "origin")))
                    {
                        return Path.Combine(Directory.GetCurrentDirectory(), _videosDirectory, x.ToString("dd-MM-yyyy"), "origin");
                    }
                    return "";
                }).Where(x => !string.IsNullOrEmpty(x)).ToList();

                if (foldersForBlocking.IsNullOrEmpty())
                {
                    return StatusCode(404);
                }

                long totalFoldersLength = 0;

                foreach (var pathToVideoFolder in foldersForBlocking)
                {
                    ArchiveHelper.StartSendingHeartbeat(pathToVideoFolder, TrackingType.Folder);
                    totalFoldersLength += ArchiveHelper.GetFolderLength(pathToVideoFolder);
                }

                foreach (var date in dates.Select(x => x.ToString("dd-MM-yyyy")))
                {

                    var cameraArchiveFolder = Path.Combine(_archivesDirectory, date, "origin", cameraName);
                    var reservedDirectoryPath = Path.Combine(_archivesDirectory, date);
                    var outputPath = Path.Combine(cameraArchiveFolder, $"{cameraName}_{date}.mp4");
                    if (ArchiveHelper.IsMergeRunning(outputPath))
                    {
                        return StatusCode(202, "Merge operation is already running.");
                    }

                    var currentDate = DateTime.ParseExact(date, "dd-MM-yyyy", CultureInfo.InvariantCulture);
                    var paths = ArchiveHelper.GetFilePathsWithinRangeOrderedByDateTime(
                        _videosDirectory,
                        currentDate,
                        currentDate.AddDays(1).AddSeconds(-1),
                        cameraName);

                    if (!Directory.Exists(cameraArchiveFolder) && paths.Count != 0)
                    {
                        Directory.CreateDirectory(cameraArchiveFolder);
                    }


                    if (paths.Count == 0)
                    {
                        skippedDates.Add(date);
                        continue;
                    }


                    if (!System.IO.File.Exists(outputPath))
                    {

                        ArchiveHelper.StartSendingHeartbeat(reservedDirectoryPath, TrackingType.Folder);
                        ArchiveHelper.RequestPurgeIfNeeded(totalFoldersLength);

                        _ = ArchiveHelper.MergeVideosAsync(paths, outputPath, Directory.GetCurrentDirectory());
                        return StatusCode(202, "Merge operation started.");
                    }

                    ArchiveHelper.StopSendingHeartbeat(reservedDirectoryPath);

                    foreach (var pathToVideoFolder in foldersForBlocking)
                    {
                        ArchiveHelper.StopSendingHeartbeat(pathToVideoFolder);
                    }
                }

                return Ok();
            }
            catch (Exception ex)
            {

                return BadRequest();
            }
        }

        [HttpPost]
        [Route("time-archive/merge/{cameraName}")]
        public async Task<IActionResult> MergeArchive(string cameraName, [FromQuery] DateTime date, [FromQuery] DateTime startTime, [FromQuery] DateTime endTime)
        {
            try
            {

                var dateToString = date.ToString("dd-MM-yyyy");

                var folderForBlocking = Path.Combine(Directory.GetCurrentDirectory(), _videosDirectory, dateToString, "origin");
                if (!Directory.Exists(folderForBlocking))
                {
                    return StatusCode(404);
                }


                long totalFoldersLength = 0;
                ArchiveHelper.StartSendingHeartbeat(folderForBlocking, TrackingType.Folder);
                totalFoldersLength += ArchiveHelper.GetFolderLength(folderForBlocking);


                var cameraArchiveFolder = Path.Combine(_archivesDirectory, dateToString, $"{startTime.ToString("HH-mm-ss")}_{endTime.ToString("HH-mm-ss")}", cameraName);

                var reservedDirectoryPath = Path.Combine(_archivesDirectory, dateToString);
                var outputPath = Path.Combine(cameraArchiveFolder, $"{startTime.ToString("HH-mm-ss")}_{endTime.ToString("HH-mm-ss")}.mp4");
                if (ArchiveHelper.IsMergeRunning(outputPath))
                {
                    return StatusCode(202, "Merge operation is already running.");
                }

                if (Directory.Exists(cameraArchiveFolder))
                {
                    return Ok();
                }

                var paths = ArchiveHelper.GetFilePathsWithinRangeOrderedByDateTime(_videosDirectory,
               startTime, endTime, cameraName);

                if (paths.Count == 0)
                {
                    return StatusCode(404);
                }

                if (!Directory.Exists(cameraArchiveFolder))
                {
                    Directory.CreateDirectory(cameraArchiveFolder);
                }

                if (!System.IO.File.Exists(outputPath))
                {

                    ArchiveHelper.StartSendingHeartbeat(reservedDirectoryPath, TrackingType.Folder);
                    ArchiveHelper.RequestPurgeIfNeeded(totalFoldersLength);

                    _ = ArchiveHelper.MergeVideosAsync(paths, outputPath, Directory.GetCurrentDirectory());
                    return StatusCode(202, "Merge operation started.");
                }

                ArchiveHelper.StopSendingHeartbeat(reservedDirectoryPath);
                ArchiveHelper.StopSendingHeartbeat(folderForBlocking);

                return Ok();
            }
            catch (Exception ex)
            {

                return BadRequest();
            }
        }




        [HttpPost]
        [Route("archive/add-to-zip")]
        public async Task<IActionResult> AddToZipArchiveAsync([FromBody] List<DateTime> neededDates)
        {
            var zipPath = $"{_archivesDirectory}/" +
            $"{neededDates.First().ToString("dd-MM-yyyy")}_{neededDates.Last().ToString("dd-MM-yyyy")}.zip";

            if (_runningZips.Contains(zipPath))
            {
                return StatusCode(202);
            }

            if (!System.IO.File.Exists(zipPath))
            {
                var directories = Directory.GetDirectories(_archivesDirectory);

                var directoriesToZip = new List<string>();

                var neededSpace = 0L;
                foreach (var directory in directories)
                {
                    if (neededDates.Select(x => x.ToString("dd-MM-yyyy")).Contains(DateTime.Parse(directory.Split(@"\").Last()).ToString("dd-MM-yyyy")))
                    {
                        neededSpace += ArchiveHelper.GetFolderLength(directory);
                        directoriesToZip.Add(directory);
                        ArchiveHelper.StartSendingHeartbeat(directory, TrackingType.Folder);
                    }
                }
                try
                {
                    ArchiveHelper.RequestPurgeIfNeeded(neededSpace);

                }
                catch (Exception ex)
                {
                    foreach (var directory in directories)
                    {
                        ArchiveHelper.StopSendingHeartbeat(directory);
                    }
                    return BadRequest(ex.Message);
                }

                if (directoriesToZip.Count > 0)
                {
                    _ = Task.Run(() =>
                    {
                        var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                        ArchiveHelper.StartSendingHeartbeat(zipPath, TrackingType.File);
                        foreach (var directory in directoriesToZip)
                        {
                            lock (_zipLock)
                            {
                                _runningZips.Add(zipPath);
                            }
                            ArchiveHelper.AddDirectoryToZip(zip, directory, $"{directory.Split(@"\").Last()}");
                            lock (_zipLock)
                            {
                                _runningZips.Remove(zipPath);
                            }
                        }
                        zip.Dispose();
                    });
                    return StatusCode(202);

                }
            }
            var dates = neededDates.Select(x => x.ToString("dd-MM-yyyy")).ToList();

            var archiveDirectories = Directory
                .GetDirectories(_archivesDirectory)
                .Where(x => dates.Any(date => Path.GetFileName(x).Contains(date)))
                .ToList();

            foreach (var directory in archiveDirectories)
            {
                System.IO.Directory.Delete(directory, true);
            }
            return Ok(zipPath);

        }

        private async Task SendZipAsync(string zipPath)
        {
            ulong bufferSize = 64 * 1024;
            var buffer = new byte[bufferSize];

            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read);
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "application/zip";
            Response.ContentLength = fs.Length;

            int count;
            while ((count = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await Response.Body.WriteAsync(buffer.AsMemory(0, count));
            }

            await Response.Body.FlushAsync();
            Debug.WriteLine($"DONE ---- {DateTime.Now}");
        }

        [HttpGet]
        [Route("are-fragments-ready")]
        public IActionResult GetFragmentsStatus()
        {
            var neededDate = DateTime.Now.AddDays(-1);
            var zipPath = $"{_tempDirectory}/" + $"{neededDate.ToString("dd-MM-yyyy")}.zip";
            if (!DefectCutterService.IsBusy && System.IO.File.Exists(zipPath))
            {
                _logger.LogInformation("Началось скачивание фрагментов");
                return (StatusCode((int)HttpStatusCode.Created));
            }
            else if (!DefectCutterService.IsBusy && !System.IO.File.Exists(zipPath))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using (var defectCutterService = _scope.ServiceProvider.GetService<DefectCutterService>())
                        {
                            defectCutterService.ConfigureCutting();
                            await defectCutterService.CutAllDefects(neededDate);

                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при вырезании дефектов");
                    }
                });

                return Ok();
            }

            else if (DefectCutterService._defectsDate != DateTime.Now.Date.AddDays(-1))
            {
                DefectCutterService.IsBusy = false;
                _logger.LogInformation("Пришел запрос на скачивание фрагментов, вырезание зависло вчера, перезапуск");
                return StatusCode(425);
            }
            else
            {
                _logger.LogInformation("Пришел запрос на скачивание фрагментов, фрагменты не готовы");

                return StatusCode(425);
            }
        }

        [HttpGet]
        [Route("download")]
        public async Task<IActionResult> GetArchiveZipAsync()
        {
            var neededDate = DateTime.Now.AddDays(-1);
            var zipPath = $"{_tempDirectory}/" + $"{neededDate.ToString("dd-MM-yyyy")}.zip";
            if (System.IO.File.Exists(zipPath))
            {
                await SendZipAsync(zipPath);
                System.IO.File.Delete(zipPath);
            }
            return NoContent();

        }


        [HttpGet]
        [Route("download-archive")]
        public async Task<IActionResult> GetZipFileAsync([FromQuery] string zipPath)
        {
            return await ReturnZipFileAsync(zipPath);
        }

        [HttpPost]
        [Route("download-archive")]
        public async Task<IActionResult> PostZipFileAsync([FromBody] string zipPath)
        {
            return await ReturnZipFileAsync(zipPath);
        }

        private Task<IActionResult> ReturnZipFileAsync(string zipPath)
        {
            if (System.IO.File.Exists(zipPath))
            {
                ArchiveHelper.StartSendingHeartbeat(zipPath, TrackingType.File);
                HttpContext.Response.OnCompleted(() =>
                {
                    ArchiveHelper.StopSendingHeartbeat(zipPath);
                    return Task.CompletedTask;
                });
                return Task.FromResult<IActionResult>(
                    PhysicalFile(zipPath, "application/zip", Path.GetFileName(zipPath), enableRangeProcessing: false));

            }

            return Task.FromResult<IActionResult>(BadRequest());
        }

    }
}
