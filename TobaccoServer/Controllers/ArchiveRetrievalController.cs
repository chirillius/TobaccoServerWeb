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
        private static ConcurrentDictionary<string, int> _activeZipDownloads = new ConcurrentDictionary<string, int>();
        private static ConcurrentDictionary<string, string> _mergeOwners = new ConcurrentDictionary<string, string>();
        private static ConcurrentDictionary<string, string> _zipOwners = new ConcurrentDictionary<string, string>();
        private static ConcurrentDictionary<string, CancellationTokenSource> _zipCancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();
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

        private static bool IsZipBuilding(string zipPath)
        {
            lock (_zipLock)
            {
                return _runningZips.Contains(zipPath);
            }
        }

        private static void MarkZipBuilding(string zipPath)
        {
            lock (_zipLock)
            {
                if (!_runningZips.Contains(zipPath))
                {
                    _runningZips.Add(zipPath);
                }
            }
        }

        private static void UnmarkZipBuilding(string zipPath)
        {
            lock (_zipLock)
            {
                _runningZips.Remove(zipPath);
            }
        }

        private static bool IsZipDownloading(string zipPath)
        {
            return _activeZipDownloads.TryGetValue(zipPath, out var count) && count > 0;
        }

        private IActionResult? GetZipBusyResult(string zipPath)
        {
            if (IsZipBuilding(zipPath))
            {
                return Conflict("Архив в данный момент собирается другим клиентом.");
            }

            if (IsZipDownloading(zipPath))
            {
                return Conflict("Архив в данный момент скачивается другим клиентом.");
            }

            return null;
        }

        private static string? NormalizeClientRequestId(string? clientRequestId)
        {
            var normalized = clientRequestId?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static bool IsOwnedByCurrentClient(ConcurrentDictionary<string, string> owners, string key, string? clientRequestId)
        {
            var normalizedClientRequestId = NormalizeClientRequestId(clientRequestId);
            if (normalizedClientRequestId is null)
            {
                return false;
            }

            return owners.TryGetValue(key, out var owner) &&
                   string.Equals(owner, normalizedClientRequestId, StringComparison.Ordinal);
        }

        private static void ClearMergeOwnerIfCompleted(string outputPath)
        {
            if (!ArchiveHelper.IsMergeRunning(outputPath))
            {
                _mergeOwners.TryRemove(outputPath, out _);
            }
        }

        private static void ClearZipOwnerIfCompleted(string zipPath)
        {
            if (!IsZipBuilding(zipPath))
            {
                _zipOwners.TryRemove(zipPath, out _);
                if (_zipCancellationTokens.TryRemove(zipPath, out var cancellationTokenSource))
                {
                    cancellationTokenSource.Dispose();
                }
            }
        }


        [HttpGet]
        [Route("live-archive/available-dates")]
        public IActionResult GetLiveArchiveAvailableDates()
        {
            if (!Directory.Exists(_videosDirectory))
            {
                return Ok(new
                {
                    dates = Array.Empty<string>(),
                    availability = new Dictionary<string, string[]>()
                });
            }

            var availability = new Dictionary<string, string[]>();

            foreach (var dateDirectory in Directory.GetDirectories(_videosDirectory))
            {
                var directoryName = Path.GetFileName(dateDirectory);
                if (!DateTime.TryParseExact(directoryName, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
                {
                    continue;
                }

                var originDirectory = Path.Combine(dateDirectory, "origin");
                if (!Directory.Exists(originDirectory))
                {
                    continue;
                }

                var cameras = Directory.GetDirectories(originDirectory)
                    .Select(Path.GetFileName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray();

                if (cameras.Length == 0)
                {
                    continue;
                }

                availability[parsedDate.ToString("yyyy-MM-dd")] = cameras!;
            }

            var dates = availability.Keys
                .OrderByDescending(x => x)
                .ToArray();

            return Ok(new
            {
                dates,
                availability
            });
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
            var zipPath = Path.Combine(
                _archivesDirectory,
                $"{listStartDateTime.First():dd-MM-yyyy}_{listStartDateTime.Last():dd-MM-yyyy}.zip");

            var busyResult = GetZipBusyResult(zipPath);
            if (busyResult is not null)
            {
                return busyResult;
            }

            if (System.IO.File.Exists(zipPath))
            {
                return Ok(zipPath);
            }

            return StatusCode(404);
        }

        [HttpGet]
        [Route("archive/info")]
        public IActionResult GetZipInfo([FromQuery] string zipPath)
        {
            if (string.IsNullOrWhiteSpace(zipPath))
            {
                return BadRequest("Путь к архиву не указан.");
            }

            if (IsZipBuilding(zipPath))
            {
                return Conflict("Архив в данный момент ещё собирается.");
            }

            if (!System.IO.File.Exists(zipPath))
            {
                return NotFound("Архив не найден на сервере.");
            }

            var fileInfo = new FileInfo(zipPath);
            return Ok(new
            {
                size = fileInfo.Length
            });
        }

        [HttpPost]
        [Route("archive/deleteZip")]
        public async Task<IActionResult> DeleteZip([FromBody] List<DateTime> listStartDateTime)
        {
            var zipPath = Path.Combine(
                _archivesDirectory,
                $"{listStartDateTime.First():dd-MM-yyyy}_{listStartDateTime.Last():dd-MM-yyyy}.zip");

            var busyResult = GetZipBusyResult(zipPath);
            if (busyResult is not null)
            {
                return busyResult;
            }

            if (!System.IO.File.Exists(zipPath))
            {
                return NotFound("Архив не найден на сервере.");
            }

            try
            {
                System.IO.File.Delete(zipPath);
                return Ok("Архив удалён.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Не удалось удалить архив: {ex.Message}");
            }
        }

        [HttpPost]
        [Route("archive/merge/{cameraName}")]
        public async Task<IActionResult> MergeArchive(string cameraName, [FromBody] List<DateTime> neededDates, [FromQuery] string? clientRequestId = null, [FromQuery] bool waitForExisting = false)
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
                    ClearMergeOwnerIfCompleted(outputPath);
                    if (ArchiveHelper.IsMergeRunning(outputPath))
                    {
                        if (IsOwnedByCurrentClient(_mergeOwners, outputPath, clientRequestId) || waitForExisting)
                        {
                            return StatusCode(202, "Архив за выбранную дату ещё собирается.");
                        }

                        return Conflict("Архив за выбранную дату уже собирается другим клиентом.");
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
                        var normalizedClientRequestId = NormalizeClientRequestId(clientRequestId);
                        if (normalizedClientRequestId is not null)
                        {
                            _mergeOwners[outputPath] = normalizedClientRequestId;
                        }

                        ArchiveHelper.StartSendingHeartbeat(reservedDirectoryPath, TrackingType.Folder);
                        ArchiveHelper.RequestPurgeIfNeeded(totalFoldersLength);

                        _ = ArchiveHelper.MergeVideosAsync(paths, outputPath, Directory.GetCurrentDirectory());
                        return StatusCode(202, "Склейка архива запущена.");
                    }

                    ArchiveHelper.StopSendingHeartbeat(reservedDirectoryPath);
                    _mergeOwners.TryRemove(outputPath, out _);

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
        public async Task<IActionResult> MergeArchive(string cameraName, [FromQuery] DateTime date, [FromQuery] DateTime startTime, [FromQuery] DateTime endTime, [FromQuery] string? clientRequestId = null, [FromQuery] bool waitForExisting = false)
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
                ClearMergeOwnerIfCompleted(outputPath);
                if (ArchiveHelper.IsMergeRunning(outputPath))
                {
                    if (IsOwnedByCurrentClient(_mergeOwners, outputPath, clientRequestId) || waitForExisting)
                    {
                        return StatusCode(202, "Архив за выбранный интервал ещё собирается.");
                    }

                    return Conflict("Архив за выбранный интервал уже собирается другим клиентом.");
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
                    var normalizedClientRequestId = NormalizeClientRequestId(clientRequestId);
                    if (normalizedClientRequestId is not null)
                    {
                        _mergeOwners[outputPath] = normalizedClientRequestId;
                    }

                    ArchiveHelper.StartSendingHeartbeat(reservedDirectoryPath, TrackingType.Folder);
                    ArchiveHelper.RequestPurgeIfNeeded(totalFoldersLength);

                    _ = ArchiveHelper.MergeVideosAsync(paths, outputPath, Directory.GetCurrentDirectory());
                    return StatusCode(202, "Склейка архива запущена.");
                }

                ArchiveHelper.StopSendingHeartbeat(reservedDirectoryPath);
                ArchiveHelper.StopSendingHeartbeat(folderForBlocking);
                _mergeOwners.TryRemove(outputPath, out _);

                return Ok();
            }
            catch (Exception ex)
            {

                return BadRequest();
            }
        }




        [HttpPost]
        [Route("archive/add-to-zip")]
        public async Task<IActionResult> AddToZipArchiveAsync([FromBody] List<DateTime> neededDates, [FromQuery] string? clientRequestId = null, [FromQuery] bool waitForExisting = false)
        {
            var zipPath = Path.Combine(
                _archivesDirectory,
                $"{neededDates.First():dd-MM-yyyy}_{neededDates.Last():dd-MM-yyyy}.zip");

            ClearZipOwnerIfCompleted(zipPath);
            if (IsZipBuilding(zipPath))
            {
                if (IsOwnedByCurrentClient(_zipOwners, zipPath, clientRequestId) || waitForExisting)
                {
                    return StatusCode(202, "Архив уже собирается.");
                }

                return Conflict("Архив за выбранный период уже собирается другим клиентом.");
            }

            if (IsZipDownloading(zipPath))
            {
                if (waitForExisting)
                {
                    return StatusCode(202, "Архив в данный момент скачивается другим клиентом.");
                }
                return Conflict("Архив в данный момент скачивается другим клиентом.");
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
                    MarkZipBuilding(zipPath);
                    var normalizedClientRequestId = NormalizeClientRequestId(clientRequestId);
                    if (normalizedClientRequestId is not null)
                    {
                        _zipOwners[zipPath] = normalizedClientRequestId;
                    }
                    var cancellationTokenSource = new CancellationTokenSource();
                    _zipCancellationTokens[zipPath] = cancellationTokenSource;
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                            ArchiveHelper.StartSendingHeartbeat(zipPath, TrackingType.File);
                            foreach (var directory in directoriesToZip)
                            {
                                cancellationTokenSource.Token.ThrowIfCancellationRequested();
                                ArchiveHelper.AddDirectoryToZip(zip, directory, $"{directory.Split(@"\").Last()}");
                            }
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        finally
                        {
                            ArchiveHelper.StopSendingHeartbeat(zipPath);
                            UnmarkZipBuilding(zipPath);
                            _zipOwners.TryRemove(zipPath, out _);
                            if (_zipCancellationTokens.TryRemove(zipPath, out var existingTokenSource))
                            {
                                existingTokenSource.Dispose();
                            }

                            if (cancellationTokenSource.IsCancellationRequested)
                            {
                                try
                                {
                                    if (System.IO.File.Exists(zipPath))
                                    {
                                        System.IO.File.Delete(zipPath);
                                    }
                                }
                                catch
                                {
                                }
                            }

                            foreach (var directory in directoriesToZip)
                            {
                                ArchiveHelper.StopSendingHeartbeat(directory);
                            }
                        }
                    });
                    return StatusCode(202);

                }
            }

            if (IsZipBuilding(zipPath))
            {
                return StatusCode(202, "Архив ещё собирается.");
            }

            if (IsZipDownloading(zipPath))
            {
                return Conflict("Архив в данный момент скачивается другим клиентом.");
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

        [HttpPost]
        [Route("archive/cancel-add-to-zip")]
        public IActionResult CancelAddToZipArchive([FromBody] List<DateTime> neededDates, [FromQuery] string? clientRequestId = null)
        {
            var zipPath = Path.Combine(
                _archivesDirectory,
                $"{neededDates.First():dd-MM-yyyy}_{neededDates.Last():dd-MM-yyyy}.zip");

            ClearZipOwnerIfCompleted(zipPath);

            if (IsZipBuilding(zipPath))
            {
                if (!IsOwnedByCurrentClient(_zipOwners, zipPath, clientRequestId))
                {
                    return Conflict("Архив собирается другим клиентом и не может быть отменён.");
                }

                if (_zipCancellationTokens.TryGetValue(zipPath, out var cancellationTokenSource))
                {
                    cancellationTokenSource.Cancel();
                }
            }

            _zipOwners.TryRemove(zipPath, out _);

            try
            {
                if (System.IO.File.Exists(zipPath))
                {
                    System.IO.File.Delete(zipPath);
                }
            }
            catch
            {
            }

            return Ok("Сборка ZIP-архива отменена.");
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
            if (IsZipBuilding(zipPath))
            {
                return Task.FromResult<IActionResult>(Conflict("Архив в данный момент ещё собирается."));
            }

            if (System.IO.File.Exists(zipPath))
            {
                _activeZipDownloads.AddOrUpdate(zipPath, 1, (_, count) => count + 1);
                ArchiveHelper.StartSendingHeartbeat(zipPath, TrackingType.File);
                HttpContext.Response.OnCompleted(() =>
                {
                    ArchiveHelper.StopSendingHeartbeat(zipPath);
                    _activeZipDownloads.AddOrUpdate(
                        zipPath,
                        0,
                        (_, count) => Math.Max(0, count - 1));
                    if (_activeZipDownloads.TryGetValue(zipPath, out var count) && count == 0)
                    {
                        _activeZipDownloads.TryRemove(zipPath, out _);
                    }
                    return Task.CompletedTask;
                });
                return Task.FromResult<IActionResult>(
                    PhysicalFile(zipPath, "application/zip", Path.GetFileName(zipPath), enableRangeProcessing: true));

            }

            return Task.FromResult<IActionResult>(NotFound("Архив не найден на сервере."));
        }


        [HttpPost]
        [Route("archive/cancel-merge/{cameraName}")]
        public IActionResult CancelArchiveMerge(string cameraName, [FromBody] List<DateTime> neededDates, [FromQuery] string? clientRequestId = null)
        {
            foreach (var date in neededDates.Where(x => x < DateTime.Now.Date).Select(x => x.ToString("dd-MM-yyyy")))
            {
                var cameraArchiveFolder = Path.Combine(_archivesDirectory, date, "origin", cameraName);
                var outputPath = Path.Combine(cameraArchiveFolder, $"{cameraName}_{date}.mp4");
                ClearMergeOwnerIfCompleted(outputPath);

                if (!ArchiveHelper.IsMergeRunning(outputPath))
                {
                    continue;
                }

                if (!IsOwnedByCurrentClient(_mergeOwners, outputPath, clientRequestId))
                {
                    return Conflict("Архив за выбранную дату собирается другим клиентом и не может быть отменён.");
                }

                ArchiveHelper.CancelMerge(outputPath);
                _mergeOwners.TryRemove(outputPath, out _);

                try
                {
                    if (Directory.Exists(cameraArchiveFolder))
                    {
                        Directory.Delete(cameraArchiveFolder, true);
                    }
                }
                catch
                {
                }
            }

            return Ok("Сборка архива отменена.");
        }

        [HttpPost]
        [Route("time-archive/cancel-merge/{cameraName}")]
        public IActionResult CancelTimeArchiveMerge(string cameraName, [FromQuery] DateTime date, [FromQuery] DateTime startTime, [FromQuery] DateTime endTime, [FromQuery] string? clientRequestId = null)
        {
            var dateToString = date.ToString("dd-MM-yyyy");
            var intervalFolder = $"{startTime:HH-mm-ss}_{endTime:HH-mm-ss}";
            var cameraArchiveFolder = Path.Combine(_archivesDirectory, dateToString, intervalFolder, cameraName);
            var outputPath = Path.Combine(cameraArchiveFolder, $"{intervalFolder}.mp4");
            ClearMergeOwnerIfCompleted(outputPath);

            if (ArchiveHelper.IsMergeRunning(outputPath))
            {
                if (!IsOwnedByCurrentClient(_mergeOwners, outputPath, clientRequestId))
                {
                    return Conflict("Архив за выбранный интервал собирается другим клиентом и не может быть отменён.");
                }

                ArchiveHelper.CancelMerge(outputPath);
            }

            _mergeOwners.TryRemove(outputPath, out _);

            try
            {
                if (Directory.Exists(cameraArchiveFolder))
                {
                    Directory.Delete(cameraArchiveFolder, true);
                }
            }
            catch
            {
            }

            return Ok("Сборка архива отменена.");
        }
    }
}