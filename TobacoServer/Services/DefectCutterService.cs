using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Quartz.Impl;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using TobaccoEntities.Models;
using TobacoServer.Models.ArchiveCutters;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Jobs;
using static Quartz.Logging.OperationName;
using static System.Formats.Asn1.AsnWriter;

namespace TobacoServer.Services
{
    public class DefectCutterService : IDisposable
    {
        private AppDbContext _db;
        private IServiceProvider _serviceProvider;
        public static DateTime _defectsDate;
        public static bool IsBusy { get; set; } = false;
        private IEnumerable<IQueryable<TimestampDefect>> _timestampDefectsDbSets = new List<IQueryable<TimestampDefect>>();
        private IEnumerable<IQueryable<LastingDefect>> _lasingDefectsDbSets = new List<IQueryable<LastingDefect>>();
        private ILogger _logger;
        private IServiceScope _scope;
        private string _defectsCutTime = System.Configuration.ConfigurationManager.AppSettings["DefectsCutTime"];
        private string _videosDirectory = Path.Combine(Directory.GetCurrentDirectory(), System.Configuration.ConfigurationManager.AppSettings["VideosDirectory"]);
        private string _tempDirectory = Path.Combine(Directory.GetCurrentDirectory(), System.Configuration.ConfigurationManager.AppSettings["TempDirectory"]);

        public DefectCutterService(ILogger logger, IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;

            _scope = _serviceProvider.CreateScope();
            _db = _scope.ServiceProvider.GetService<AppDbContext>();
        }

        public async void ConfigureCutting()
        {

            _logger.LogInformation($"{DateTime.Now} | Запущена CutDefectsJob");

            AddLastingDefectCutter(_db.TooManyPeopleAtStallFailures.Select(x => x as LastingDefect));
            AddLastingDefectCutter(_db.NoOneAtStallForTooLongFailures.Select(x => x as LastingDefect));
            AddLastingDefectCutter(_db.Crowds.Select(x => x as LastingDefect));
            AddLastingDefectCutter(_db.HumanDetectionBeforeAndAfterShiftFailures.Select(x => x as LastingDefect));
            AddLastingDefectCutter(_db.ServiceNearCabinetFailures.Select(x => x as LastingDefect));
            AddLastingDefectCutter(_db.PhoneFailures.Select(x => x as LastingDefect));
            AddLastingDefectCutter(_db.CashRegisterFailures.Select(x => x as LastingDefect));
            AddLastingDefectCutter(_db.AbandonedOpenCashRegisterFailures.Select(x => x as LastingDefect));
            AddLastingDefectCutter(_db.ClearStallFailures.Select(x => x as LastingDefect));
            AddLastingDefectCutter(_db.BottleFailures.Select(x => x as LastingDefect));
            AddLastingDefectCutter(_db.LightFailures.Select(x => x as LastingDefect));


            AddTimestampDefectCutter(_db.PoseFailures.Select(x => x as TimestampDefect));
            AddTimestampDefectCutter(_db.Delays.Select(x => x as TimestampDefect));
            AddTimestampDefectCutter(_db.SmokeFailures.Select(x => x as TimestampDefect));
            AddTimestampDefectCutter(_db.ClothesControlFailures.Select(x => x as TimestampDefect));
            AddTimestampDefectCutter(_db.InactiveSalesmanFailures.Select(x => x as TimestampDefect));

            _logger.LogInformation($"{DateTime.Now} | Добавлены дефекты");

        }

        public async Task ScheduleRuns()
        {
            try
            {
                IScheduler scheduler = await StdSchedulerFactory.GetDefaultScheduler();
                await scheduler.Start();

                IJobDetail job = JobBuilder.Create<CutDefectsJob>().Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("dbContext", _db);
                job.JobDataMap.Add("defectCutterService", this);

                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity("DefectCut", "Archive")
                    .WithCronSchedule(_defectsCutTime) 
                    .Build();

                await scheduler.ScheduleJob(job, trigger);
            }
            catch (Exception ex)
            {
                _logger.LogInformation($"Ошибка при вырезании дефектов: {ex}");
            }
        }

        public void AddTimestampDefectCutter(IQueryable<TimestampDefect> timestampDefectDbSet)
        {
            _timestampDefectsDbSets = _timestampDefectsDbSets.Append(timestampDefectDbSet);
        }

        public void AddLastingDefectCutter(IQueryable<LastingDefect> lastingDefectDbSets)
        {
            _lasingDefectsDbSets = _lasingDefectsDbSets.Append(lastingDefectDbSets);
        }

        public async Task CutAllDefects(DateTime date)
        {
            _logger.LogInformation($"Запущено вырезание дефектов");

            ArchiveHelper.RequestPurgeIfNeeded(30L * 1024 * 1024 * 1024);
            IsBusy = true;
            _defectsDate = DateTime.Now.Date.AddDays(-1);

            await CutTimestampDefects(date);
            await CutLastingDefectsAsync(date);


            _logger.LogInformation($"Закончено вырезание дефектов");

            var zipPath = $"{_tempDirectory}/{date.ToString("dd-MM-yyyy")}.zip";
            var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            var currentDate = date.ToString("dd-MM-yyyy");
            try
            {
                var fragmentsPath = Path.Combine(_videosDirectory, currentDate, "fragments");
                ArchiveHelper.AddDirectoryToZip(zip, fragmentsPath, $"{fragmentsPath.Split(@"\").Last()}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании архива дефектов");
                throw;
            }
            finally
            {

                zip.Dispose();
                IsBusy = false;
            }
        }

        public async Task CutTimestampDefects(DateTime date)
        {
            _logger.LogInformation($"Запущено вырезание TimestampDefects");
            var timeStampDefectCutter = new TimestampDefectCutter<TimestampDefect>();
            foreach (var dbSet in _timestampDefectsDbSets)
            {
                await timeStampDefectCutter.Execute(dbSet, date, _logger);
            }
        }

        public async Task CutLastingDefectsAsync(DateTime date)
        {
            var lastingDefectCutter = new LastingDefectCutter<LastingDefect>();

            foreach (var dbSet in _lasingDefectsDbSets)
            {
                await lastingDefectCutter.Execute(dbSet, date, _logger);
            }
        }

        public void Dispose()
        {
            _scope.Dispose();
        }
    }
}
