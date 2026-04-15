using Microsoft.EntityFrameworkCore;
using Quartz;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Services;

namespace TobacoServer.Models.Jobs
{
    [DisallowConcurrentExecution]
    public class ConversionRegisterJob : IJob
    {
        private static int _totalPeopleCounter = 0;
        private static DateTime _currentRegisterDate = DateTime.MinValue;

        public async Task Execute(IJobExecutionContext context)
        {
            var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
            var db = dbScope?.ServiceProvider.GetService<AppDbContext>();
            var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService;

            if (db is null || videoService is null)
            {
                return;
            }

            var now = DateTime.Now;
            var today = now.Date;
            await EnsureCounterLoadedAsync(db, today);

            if (_currentRegisterDate != today)
            {
                await UpsertDailyRegisterAsync(db, _currentRegisterDate, _totalPeopleCounter);
                await LoadCounterForDateAsync(db, today);
            }

            var zonesConfigurator = new ZonesConfigurator();
            var zones = zonesConfigurator.GetZones();
            var conversionZoneNamePart = context.MergedJobDataMap["conversionZoneNamePart"]?.ToString()?.ToLower();
            var stallZoneNamePart = context.MergedJobDataMap["stallZoneNamePart"]?.ToString()?.ToLower();

            if (string.IsNullOrWhiteSpace(conversionZoneNamePart) || string.IsNullOrWhiteSpace(stallZoneNamePart))
            {
                return;
            }

            var conversionRegisterZone = zones.FirstOrDefault(x => x.Name.ToLower().Contains(conversionZoneNamePart));
            var nextFireTime = context.Trigger.GetNextFireTimeUtc();
            var thresholdDateTime = now.AddHours(1);

            if (nextFireTime.HasValue && nextFireTime.Value.ToLocalTime().DateTime > thresholdDateTime)
            {
                await UpsertDailyRegisterAsync(db, _currentRegisterDate, _totalPeopleCounter);

            }

            if (conversionRegisterZone?.ConversionCounting?.EntryBand is null)
            {
                return;
            }

            var stallZones = zones.Where(x => x.Name.ToLower().Contains(stallZoneNamePart)).ToList();
            if (stallZones.Count == 0)
            {
                return;
            }

            var stallCounts = await Task.WhenAll(stallZones.Select(x => videoService.GetPeopleNumberAsync(x)));
            if (stallCounts.Sum() <= 0)
            {
                return;
            }

            var directionalCount = await videoService.GetDirectionalEntryCountAsync(conversionRegisterZone);
            if (directionalCount.NewEntries > 0)
            {
                _totalPeopleCounter += directionalCount.NewEntries;
                await UpsertDailyRegisterAsync(db, _currentRegisterDate, _totalPeopleCounter);
            }
        }

        private static async Task EnsureCounterLoadedAsync(AppDbContext db, DateTime targetDate)
        {
            if (_currentRegisterDate == targetDate)
            {
                return;
            }

            if (_currentRegisterDate == DateTime.MinValue)
            {
                await LoadCounterForDateAsync(db, targetDate);
            }
        }

        private static async Task LoadCounterForDateAsync(AppDbContext db, DateTime targetDate)
        {
            var nextDate = targetDate.AddDays(1);
            var existingRecord = await db.ConversionRegister
                .Where(x => x.DateTime >= targetDate && x.DateTime < nextDate)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            _currentRegisterDate = targetDate;
            _totalPeopleCounter = existingRecord?.PeopleNumber ?? 0;
        }

        private static async Task UpsertDailyRegisterAsync(AppDbContext db, DateTime targetDate, int peopleNumber)
        {
            if (targetDate == DateTime.MinValue)
            {
                return;
            }

            var nextDate = targetDate.AddDays(1);
            var existingRecord = await db.ConversionRegister
                .Where(x => x.DateTime >= targetDate && x.DateTime < nextDate)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync();

            if (existingRecord is null)
            {
                _ = db.ConversionRegister.Add(new ConversionRegister
                {
                    DateTime = targetDate,
                    PeopleNumber = peopleNumber,
                    DefectImage = new DefectImage()
                });
            }
            else
            {
                existingRecord.PeopleNumber = peopleNumber;
                existingRecord.DateTime = targetDate;
            }

            await db.SaveChangesAsync();
        }
    }
}
