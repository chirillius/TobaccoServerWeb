using Microsoft.AspNetCore.Server.IISIntegration;
using Newtonsoft.Json;
using Quartz;
using Quartz.Impl;
using Quartz.Simpl;
using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using TobaccoEntities.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Jobs;
using TobacoServer.Models.Services;
using TobacoServer.Services;
using static Quartz.Logging.OperationName;

namespace TobacoServer.Models
{
    internal class PeriodicalTasksConfigurator : IDisposable
    {
        private dynamic _json;
        public dynamic _jsonName;
        private IServiceProvider _serviceProvider;
        private ILogger _logger;
        private List<JobKey> _jobKeysThatMustBeStoppedForNight = new List<JobKey>();
        private List<IServiceScope> _defectScopes = new List<IServiceScope>();

        public PeriodicalTasksConfigurator(IServiceProvider serviceProvider, ILogger logger, string jsonName = "periodal_tasks.json")
        {
            _logger = logger;
            _jsonName = jsonName;
            _serviceProvider = serviceProvider;
        }

        public async Task ConfigurePeriodicalTasksAsync()
        {
            _json = JsonConvert.DeserializeObject(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Configuration", _jsonName), System.Text.Encoding.UTF8));

            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            await StartStaringAtCameraDetection();
            await Task.Delay(10);
            await ConfigureHumanDetectionBeforeAndAfterShift();
            await Task.Delay(10);
            await ConfigureMorningDelays(scheduler);
            await Task.Delay(10);
            await ConfigureEveningLeaving(scheduler);
            await Task.Delay(10);
            await ConfigurePeopleAtStallNumberCheck();
            await Task.Delay(10);
            await ConfigureSmokeDetectionAsync();
            await Task.Delay(10);
            await ConfigurePhoneDetectionAsync();
            await Task.Delay(10);
            await ConfigureFoodDetectionAsync();
            await Task.Delay(10);
            await ConfigurePoseClassificationAsync();
            await Task.Delay(10);
            await ConfigureSpeechToText();
            await Task.Delay(10);
            await ConfigureCrowdControl();
            await Task.Delay(10);
            await ConfigureMoppingDetection();
            await Task.Delay(10);
            await ConfigureServiceNearCabinetDetection();
            await Task.Delay(10);
            await ConfigureCashRegisterDetectionAsync();
            await Task.Delay(10);
            await ConfigureCashRegisterRecountingDetectionAsync();
            await Task.Delay(10);
            await ConfigureClothesControlAsync();
            await Task.Delay(10);
            await ConfigureConversionRegisterAsync();
            await Task.Delay(10);
            await ConfigureClearStallDetectionAsync();
            await Task.Delay(10);
            await ConfigureBottleDetectionAsync();
            await Task.Delay(10);
            await ConfigureBadgeDetectionAsync();
            await Task.Delay(10);
            await ConfigureLightDetectionAsync();
            await Task.Delay(10);
            await ConfigureAbandonedOpenCashRegisterAsync();
            await Task.Delay(10);
            //Должен быть последним!
            await ConfigureNightStoppingAndDayResuming(scheduler);
        }

        private async Task ConfigureAbandonedOpenCashRegisterAsync()
        {
            bool isActive = _json.abandonedOpenCashRegister.isActive;
            if (isActive)
            {
                var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
                int period = _json.abandonedOpenCashRegister.period;
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                IJobDetail job = JobBuilder.Create<AbandonedOpenCashRegisterJob>().Build();
                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("zoneNamePart", _json.abandonedOpenCashRegister.zoneNamePart);
                job.JobDataMap.Add("stallZoneNamePart", _json.abandonedOpenCashRegister.stallZoneNamePart);
                job.JobDataMap.Add("period", _json.abandonedOpenCashRegister.period);
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"abandonedOpenCashRegister", "Default")
                  .StartAt(DateTime.Now)
                  .WithSimpleSchedule(x => x
                  .WithInterval(new TimeSpan(0, 0, period / 1000))
                  .RepeatForever())
                  .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }


        private async Task ConfigureLightDetectionAsync()
        {
            bool isActive = _json.lightDetection.isActive;
            if (isActive)
            {
                var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
                int period = _json.lightDetection.period;
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);

                IJobDetail job = JobBuilder.Create<LightDetectionJob>().Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("zoneNamePart", _json.lightDetection.zoneNamePart);
                job.JobDataMap.Add("period", _json.lightDetection.period);

                ITrigger trigger = TriggerBuilder.Create()
                        .WithIdentity($"ConfigureLightDetectionAsync", "Default")
                        .StartAt(DateTime.Now)
                        .WithSimpleSchedule(x => x
                        .WithInterval(new TimeSpan(0, 0, period / 1000))
                        .RepeatForever())
                        .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigureBadgeDetectionAsync()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.badgeDetection.isActive;
            int periodInMinutes = _json.badgeDetection.periodInMinutes;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                IJobDetail job = JobBuilder.Create<BadgeDetectionJob>().Build();
                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("zoneNamePart", _json.badgeDetection.zoneNamePart);
                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity("badgeDetection", "Default")
                    .StartAt(DateTime.Now)
                    .WithSimpleSchedule(x => x
                        .WithInterval(TimeSpan.FromMinutes(periodInMinutes))
                        .RepeatForever())
                    .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigureBottleDetectionAsync()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.bottleDetection.isActive;
            int period = _json.bottleDetection.period;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                IJobDetail job = JobBuilder.Create<BottleDetectionJob>().Build();
                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("period", _json.bottleDetection.period);
                job.JobDataMap.Put("zoneNamePart", _json.bottleDetection.zoneNamePart);
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"bottleDetection", "Default")
                  .StartAt(DateTime.Now)
                  .WithSimpleSchedule(x => x
                  .WithInterval(TimeSpan.FromMilliseconds(period))
                  .RepeatForever())
                  .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigureConversionRegisterAsync()
        {
            bool isActive = _json.conversionRegister.isActive;
            if (isActive)
            {
                int period = _json.conversionRegister.period;
                var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);

                IJobDetail job = JobBuilder.Create<ConversionRegisterJob>().Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                var conversionZoneNamePart = _json.conversionRegister.conversionZoneNamePart ?? _json.conversionRegister.clientZoneNamePart;
                var stallZoneNamePart = _json.conversionRegister.stallZoneNamePart ?? _json.conversionRegister.zoneNamePart;

                job.JobDataMap.Add("conversionZoneNamePart", conversionZoneNamePart);
                job.JobDataMap.Add("stallZoneNamePart", stallZoneNamePart);
                job.JobDataMap.Add("period", period);

                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity("ConversionRegisterTrigger", "Default")
                    .StartAt(DateTime.Now)
                    .WithSimpleSchedule(x => x
                        .WithInterval(TimeSpan.FromMilliseconds(period))
                        .RepeatForever())
                    .Build();

                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigureClearStallDetectionAsync()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.clearStallDetection.isActive;
            int period = _json.clearStallDetection.period;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);

                IJobDetail job = JobBuilder.Create<ClearStallDetectionJob>().Build();
                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("zoneNamePart", _json.clearStallDetection.zoneNamePart);
                job.JobDataMap.Add("period", _json.clearStallDetection.period);
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"ClearStallDetection", "Default")
                  .StartAt(DateTime.Now)
                  .WithSimpleSchedule(x => x
                  .WithInterval(TimeSpan.FromMilliseconds(period))
                  .RepeatForever())
                  .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigureClothesControlAsync()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.clothesControl.isActive;
            int periodInMinutes = _json.clothesControl.periodInMinutes;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);

                IJobDetail job = JobBuilder.Create<ClothesControlJob>().Build();
                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("zoneNamePart", _json.clothesControl.zoneNamePart);
                job.JobDataMap.Add("numberOfIterations", _json.clothesControl.numberOfIterations);
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"ClothesControl", "Default")
                  .StartAt(DateTime.Now)
                  .WithSimpleSchedule(x => x
                  .WithInterval(TimeSpan.FromMinutes(periodInMinutes))
                  .RepeatForever())
                  .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigureMoppingDetection()
        {
            bool isActive = _json.moppingDetection.isActive;
            if (isActive)
            {
                int period = _json.moppingDetection.period;
                var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                var job = JobBuilder.Create<MoppingJob>()
                    .WithIdentity("MoppingJob", "Default")
                    .StoreDurably()
                    .Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("zoneNamePart", _json.moppingDetection.zoneNamePart);

                await scheduler.AddJob(job, replace: true);

                for (int i = 0; i < 7; i++)
                {
                    int intervalIndex = 0;
                    foreach (var (start, end) in GetConfiguredIntervals((IEnumerable)_json.moppingDetection.intervals))
                    {
                        await ScheduleRecurringWindowAsync(
                            scheduler,
                            job,
                            $"MoppingDetection_{i}_{intervalIndex}",
                            i,
                            start,
                            end,
                            period);
                        intervalIndex++;
                    }
                }
            }
        }

        private async Task ConfigureCashRegisterRecountingDetectionAsync()
        {
            bool isActive = _json.countingCashRegisterCheck.isActive;

            if (isActive)
            {
                int period = _json.countingCashRegisterCheck.period;
                var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                var job = JobBuilder.Create<CashRegisterRecountingJob>()
                    .WithIdentity("CashRegisterRecountingJob", "Default")
                    .StoreDurably()
                    .Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("period", period);
                job.JobDataMap.Add("zoneNamePart", _json.countingCashRegisterCheck.zoneNamePart);
                job.JobDataMap.Add("clientZoneNamePart", _json.countingCashRegisterCheck.clientZoneNamePart);

                await scheduler.AddJob(job, replace: true);

                for (int i = 0; i < 7; i++)
                {
                    int intervalIndex = 0;
                    foreach (var (start, end) in GetConfiguredIntervals((IEnumerable)_json.countingCashRegisterCheck.intervals))
                    {
                        await ScheduleRecurringWindowAsync(
                            scheduler,
                            job,
                            $"CountingCashRegister_{i}_{intervalIndex}",
                            i,
                            start,
                            end,
                            period,
                            new Dictionary<string, string>
                            {
                                ["intervalKey"] = $"{GetQuartzDayOfWeek(i)}_{intervalIndex}",
                                ["intervalEndTime"] = end.ToString(@"hh\:mm\:ss")
                            });
                        intervalIndex++;
                    }
                }
            }
        }

        private async Task ConfigureCashRegisterDetectionAsync()
        {
            bool isActive = _json.cashRegisterCheck.isActive;
            if (isActive)
            {
                var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
                int period = _json.cashRegisterCheck.period;
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                var job = JobBuilder.Create<CashRegisterCheckJob>().Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("period", _json.cashRegisterCheck.period);
                job.JobDataMap.Add("zoneNamePart", _json.cashRegisterCheck.zoneNamePart);
                job.JobDataMap.Add("clientZoneNamePart", _json.cashRegisterCheck.clientZoneNamePart);

                var trigger = TriggerBuilder.Create()
                    .WithIdentity("CashRegisterTrigger", "Default")
                    .StartAt(DateTime.Now)
                    .WithSimpleSchedule(x => x
                        .WithInterval(TimeSpan.FromMilliseconds(period))
                        .RepeatForever())
                    .Build();

                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigurePoseClassificationAsync()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.poseClassification.isActive;
            int period = _json.poseClassification.period;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                IJobDetail job = JobBuilder.Create<PoseClassificationJob>().Build();
                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("timeOffsetInMilliseconds", _json.poseClassification.timeOffsetInMilliseconds);
                job.JobDataMap.Add("lengthOfSamplesListToAverage", _json.poseClassification.lengthOfSamplesListToAverage);
                job.JobDataMap.Add("timeDelta", _json.poseClassification.timeDelta);
                job.JobDataMap.Add("zoneNamePart", _json.poseClassification.zoneNamePart);
                job.JobDataMap.Add("clientZoneNamePart", _json.poseClassification.clientZoneNamePart);
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"PoseClassification", "Default")
                  .StartAt(DateTime.Now)
                  .WithSimpleSchedule(x => x
                  .WithInterval(TimeSpan.FromMilliseconds(period))
                  .RepeatForever())
                  .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);

            }
        }

        private async Task ConfigureFoodDetectionAsync()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.foodDetection.isActive;
            int period = _json.foodDetection.period;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);

                IJobDetail job = JobBuilder.Create<FoodDetectionJob>().Build();
                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("period", _json.foodDetection.period);
                job.JobDataMap.Add("zoneNamePart", _json.foodDetection.zoneNamePart);
                job.JobDataMap.Add("clientZoneNamePart", _json.foodDetection.clientZoneNamePart);
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"FoodDetection", "Default")
                  .StartAt(DateTime.Now)
                  .WithSimpleSchedule(x => x
                  .WithInterval(TimeSpan.FromMilliseconds(period))
                  .RepeatForever())
                  .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigurePhoneDetectionAsync()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.phoneDetection.isActive;
            int period = _json.phoneDetection.period;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);

                IJobDetail job = JobBuilder.Create<PhoneDetectionJob>().Build();
                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("period", _json.phoneDetection.period);
                job.JobDataMap.Add("zoneNamePart", _json.phoneDetection.zoneNamePart);
                job.JobDataMap.Add("clientZoneNamePart", _json.phoneDetection.clientZoneNamePart);
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"PhoneDetection", "Default")
                  .StartAt(DateTime.Now)
                  .WithSimpleSchedule(x => x
                  .WithInterval(TimeSpan.FromMilliseconds(period))
                  .RepeatForever())
                  .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigureHumanDetectionBeforeAndAfterShift()
        {
            bool isActive = _json.humanDetectionBeforeAndAfterShift.isActive;

            if (!isActive)
                return;

            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            int period = _json.humanDetectionBeforeAndAfterShift.period;
            var scope = _serviceProvider.CreateScope();
            _defectScopes.Add(scope);

            IJobDetail job = JobBuilder.Create<HumanDetectionBeforeAndAfterShiftJob>()
                .WithIdentity("HumanDetectionBeforeAndAfterShiftJob", "Default")
                .StoreDurably()
                .Build();

            job.JobDataMap.Add("cameraAddresses", _json.humanDetectionBeforeAndAfterShift.cameraAddresses);
            job.JobDataMap.Add("appDbContextScope", scope);
            job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
            job.JobDataMap.Add("period", period);

            await scheduler.AddJob(job, replace: true);

            for (int i = 0; i < 7; i++)
            {
                int morningHours, morningMinutes, eveningHours, eveningMinutes;
                GetParsedDate(i, out morningHours, out morningMinutes, out eveningHours, out eveningMinutes);
                var nightStart = new TimeSpan(eveningHours, eveningMinutes, 0).Add(TimeSpan.FromHours(1));
                var nightEnd = new TimeSpan(morningHours, morningMinutes, 0).Subtract(TimeSpan.FromHours(1));

                await ScheduleRecurringWindowAsync(
                    scheduler,
                    job,
                    $"HumanDetectionNight_{i}",
                    i,
                    nightStart,
                    nightEnd,
                    period);
            }
        }

        private async Task ConfigureServiceNearCabinetDetection()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.serviceNearCabinet.isActive;
            int period = _json.serviceNearCabinet.period;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);

                IJobDetail job = JobBuilder.Create<ServiceNearCabinetDetectionJob>().Build();
                var zonesConfigurator = new ZonesConfigurator();
                var zones = zonesConfigurator.GetZones();
                job.JobDataMap.Add("zoneNamePart", _json.serviceNearCabinet.zoneNamePart);
                job.JobDataMap.Add("clientZoneNamePart", _json.serviceNearCabinet.clientZoneNamePart);
                job.JobDataMap.Add("stallZoneNamePart", _json.serviceNearCabinet.stallZoneNamePart);
                job.JobDataMap.Add("period", _json.serviceNearCabinet.period);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"ServiceNearCabinetDetection", "Default")
                    .StartAt(DateTime.Now)
                    .WithSimpleSchedule(x => x
                        .WithInterval(TimeSpan.FromMilliseconds(period))
                        .RepeatForever())
                    .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        public async Task StartStaringAtCameraDetection()
        {
            bool isActive = _json.staringAtCamera.isActive;
            if (isActive)
            {
                var client = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["StaringAtCameraServiceAddress"]) };
                var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
                var json = _json.staringAtCamera.ToString();
                int period = _json.staringAtCamera.period;
                if (isActive)
                {
                    var scope = _serviceProvider.CreateScope();
                    _defectScopes.Add(scope);

                    IJobDetail job = JobBuilder.Create<StaringAtCameraJob>().Build();
                    var zonesConfigurator = new ZonesConfigurator();
                    var zones = zonesConfigurator.GetZones();
                    job.JobDataMap.Add("clientZoneNamePart", _json.staringAtCamera.clientZoneNamePart);
                    job.JobDataMap.Add("period", _json.staringAtCamera.period);
                    job.JobDataMap.Add("appDbContextScope", scope);
                    job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                    ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity($"StaringAtCameraDetection", "Default")
                        .StartAt(DateTime.Now)
                        .WithSimpleSchedule(x => x
                            .WithInterval(TimeSpan.FromMilliseconds(period))
                            .RepeatForever())
                        .Build();
                    _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                    _ = await scheduler.ScheduleJob(job, trigger);
                }
            }
        }

        private async Task ConfigureNightStoppingAndDayResuming(IScheduler scheduler)
        {
            for (int i = 0; i < 7; i++)
            {
                await scheduler.Start();
                int morningHours;
                int morningMinutes;
                int eveningHours;
                int eveningMinutes;
                string identityName = GetParsedDate(i, out morningHours, out morningMinutes, out eveningHours, out eveningMinutes);
                var morningFireTime = DateTime.Today.AddDays(i).AddHours(morningHours).AddMinutes(morningMinutes);
                var eventngFireTime = DateTime.Today.AddDays(i).AddHours(eveningHours).AddMinutes(eveningMinutes);

                IJobDetail nightStoppingJob = JobBuilder.Create<NightStoppingJob>().Build();
                nightStoppingJob.JobDataMap.Add("logger", _logger);
                nightStoppingJob.JobDataMap.Add("jobs", _jobKeysThatMustBeStoppedForNight);
                nightStoppingJob.JobDataMap.Add("scheduler", scheduler);
                nightStoppingJob.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                ITrigger nightStoppingTrigger = TriggerBuilder.Create()
                    .WithIdentity($"NightStoppingTrigger_{identityName}", "Default")
                    .StartAt(new DateTimeOffset(eventngFireTime))
                    .WithSimpleSchedule(x => x
                        .WithIntervalInHours(7 * 24)
                        .RepeatForever())
                    .Build();
                _ = await scheduler.ScheduleJob(nightStoppingJob, nightStoppingTrigger);


                IJobDetail dayResumingJob = JobBuilder.Create<DayResumingJob>().Build();
                dayResumingJob.JobDataMap.Add("logger", _logger);
                dayResumingJob.JobDataMap.Add("jobs", _jobKeysThatMustBeStoppedForNight);
                dayResumingJob.JobDataMap.Add("scheduler", scheduler);
                ITrigger dayResumingTrigger = TriggerBuilder.Create()
                    .WithIdentity($"DayResumingTrigger_{identityName}", "Default")
                    .StartAt(new DateTimeOffset(morningFireTime))
                    .WithSimpleSchedule(x => x
                        .WithIntervalInHours(7 * 24)
                        .RepeatForever())
                    .Build();
                _ = await scheduler.ScheduleJob(dayResumingJob, dayResumingTrigger);

            }
        }
        private async Task StartLightDetection()
        {
            if ((bool)_json.lightDetection.isActive)
            {
                try
                {
                    using var client = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["LightDetectorAddress"]) };
                    var zonesConfigurator = new ZonesConfigurator();
                    var zone = zonesConfigurator.GetZones().Where(x => x.Name == _json.lightDetection.zoneName.ToString()).First();
                    var data = new Dictionary<string, string>()
                    {
                        { "cameraAddress", zone.CameraAddress.ToString() },
                        { "period", _json.lightDetection.period.ToString() },
                        { "time", _json.delays.time.ToString() },
                        { "brightnessThreshold", _json.lightDetection.brightnessThreshold.ToString() },

                    };
                    var response = await client.PostAsJsonAsync(Uri.EscapeDataString("start"), data);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Exception was thrown in StartLightDetection.");
                }
            }
        }
        private async Task ConfigureCrowdControl()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.crowdControl.isActive;
            int period = _json.crowdControl.period;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                var zones = new ZonesConfigurator().GetZones();
                IJobDetail job = JobBuilder.Create<CrowdControlJob>().Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("zoneNamePart", _json.crowdControl.zoneNamePart);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("interval", period);
                ITrigger trigger = TriggerBuilder.Create()
                    .WithIdentity("CrowdControl", "Default")
                    .StartAt(DateTime.Now)
                    .WithSimpleSchedule(x => x
                        .WithInterval(TimeSpan.FromMilliseconds(period))
                        .RepeatForever())
                    .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigureSpeechToText()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.speechToText.isActive;
            int period = _json.speechToText.period;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                var zones = new ZonesConfigurator().GetZones();
                IJobDetail job = JobBuilder.Create<SpeechToTextJob>().Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("clientZoneNamePart", _json.speechToText.clientZoneNamePart);
                job.JobDataMap.Add("zones", zones);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("cameraAddress", _json.speechToText.cameraAddress);
                job.JobDataMap.Add("isRunning", false);
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"SpeechToText", "Default")
                .WithPriority(10)
                  .StartAt(DateTime.Now)
                  .WithSimpleSchedule(x => x
                  .WithInterval(TimeSpan.FromMilliseconds(period))
                  .RepeatForever())
                  .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigureSmokeDetectionAsync()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.smokeDetection.isActive;
            int period = _json.smokeDetection.period;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                IJobDetail job = JobBuilder.Create<SmokeDetectionJob>().Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("zoneNamePart", _json.smokeDetection.zoneNamePart);
                job.JobDataMap.Add("clientZoneNamePart", _json.smokeDetection.clientZoneNamePart);
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"SmokeDetection", "Default")
                  .StartAt(DateTime.Now)
                  .WithSimpleSchedule(x => x
                  .WithInterval(TimeSpan.FromMilliseconds(period))
                  .RepeatForever())
                  .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);

            }
        }
        private async Task ConfigurePeopleAtStallNumberCheck()
        {
            var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
            bool isActive = _json.peopleAtStallNumberCheck.isActive;
            int period = _json.peopleAtStallNumberCheck.period;
            if (isActive)
            {
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                IJobDetail job = JobBuilder.Create<CheckPeopleAtStallNumberJob>().Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("interval", period);
                job.JobDataMap.Add("zoneNamePart", _json.peopleAtStallNumberCheck.zoneNamePart);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("thresholdInMinutes", _json.noOneAtStallForTooLong.thresholdInMinutes);
                ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"PeopleAtStallNumberCheck", "Default")
                  .StartAt(DateTime.Now)
                  .WithSimpleSchedule(x => x
                      .WithInterval(TimeSpan.FromMilliseconds(period))
                      .RepeatForever())
                  .Build();
                _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                _ = await scheduler.ScheduleJob(job, trigger);
            }
        }

        private async Task ConfigureMorningDelays(IScheduler scheduler)
        {
            bool isActive = _json.delays.isMorningDelaysActive;
            if (isActive)
            {
                for (int i = 0; i < 7; i++)
                {
                    await scheduler.Start();
                    int morningHours;
                    int morningMinutes;
                    string identityName = GetParsedDate(i, out morningHours, out morningMinutes, out _, out _) + "утро";

                    var fireAt = DateTime.Today.AddDays(i).AddHours(morningHours).AddMinutes(morningMinutes);
                    await StartDailyScheduleJob(scheduler, identityName, fireAt);
                }

            }
        }

        public async Task ConfigureEveningLeaving(IScheduler scheduler)
        {
            bool isActive = _json.delays.isEveningLeavingActive;
            if (isActive)
            {
                for (int i = 0; i < 7; i++)
                {
                    await scheduler.Start();
                    int eveningHours;
                    int eveningMinutes;
                    string dayOfWeek = GetParsedDate(i, out _, out _, out eveningHours, out eveningMinutes) + "_вечер";

                    var fireAt = DateTime.Today.AddDays(i).AddHours(eveningHours).AddMinutes(eveningMinutes);
                    await StartDailyScheduleJob(scheduler, dayOfWeek, fireAt);
                }
            }
        }

        private async Task StartDailyScheduleJob(IScheduler scheduler, string identityName, DateTime fireAt)
        {
            var scope = _serviceProvider.CreateScope();
            _defectScopes.Add(scope);
            IJobDetail job = JobBuilder.Create<DelaysJob>().Build();
            job.JobDataMap.Add("logger", _logger);
            job.JobDataMap.Add("ZoneNamePart", _json.delays.zoneNamePart);
            job.JobDataMap.Add("appDbContextScope", scope);
            job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity($"DelayTrigger_{identityName}", "EveningDelays")
                .StartAt(new DateTimeOffset(fireAt))
                .WithSimpleSchedule(x => x
                    .WithIntervalInHours(7 * 24)
                    .RepeatForever())
                .Build();
            _ = await scheduler.ScheduleJob(job, trigger);
        }

        private IEnumerable<(TimeSpan Start, TimeSpan End)> GetConfiguredIntervals(IEnumerable intervals)
        {
            foreach (var interval in intervals)
            {
                var parts = interval.ToString().Split('-', StringSplitOptions.TrimEntries);
                yield return (TimeSpan.Parse(parts[0]), TimeSpan.Parse(parts[1]));
            }
        }

        private async Task ScheduleRecurringWindowAsync(
            IScheduler scheduler,
            IJobDetail job,
            string identityPrefix,
            int daysOffset,
            TimeSpan start,
            TimeSpan end,
            int periodInMilliseconds,
            IDictionary<string, string>? triggerData = null)
        {
            int dayOfWeek = GetQuartzDayOfWeek(daysOffset);

            if (end <= start)
            {
                await ScheduleWindowSegmentsAsync(
                    scheduler,
                    job,
                    $"{identityPrefix}_late",
                    dayOfWeek,
                    start,
                    TimeSpan.FromHours(24),
                    periodInMilliseconds,
                    triggerData);

                int nextDayOfWeek = (dayOfWeek % 7) + 1;
                await ScheduleWindowSegmentsAsync(
                    scheduler,
                    job,
                    $"{identityPrefix}_early",
                    nextDayOfWeek,
                    TimeSpan.Zero,
                    end,
                    periodInMilliseconds,
                    triggerData);
                return;
            }

            await ScheduleWindowSegmentsAsync(
                scheduler,
                job,
                identityPrefix,
                dayOfWeek,
                start,
                end,
                periodInMilliseconds,
                triggerData);
        }

        private async Task ScheduleWindowSegmentsAsync(
            IScheduler scheduler,
            IJobDetail job,
            string identityPrefix,
            int dayOfWeek,
            TimeSpan start,
            TimeSpan end,
            int periodInMilliseconds,
            IDictionary<string, string>? triggerData = null)
        {
            if (start >= end)
                return;

            int periodInSeconds = Math.Max(1, periodInMilliseconds / 1000);
            var current = start;
            int segmentIndex = 0;

            while (current < end)
            {
                var nextHour = new TimeSpan(current.Hours, 0, 0).Add(TimeSpan.FromHours(1));
                var segmentEnd = nextHour < end ? nextHour : end;
                var minuteField = BuildMinuteField(current, segmentEnd);
                var triggerBuilder = TriggerBuilder.Create()
                    .WithIdentity($"{identityPrefix}_{segmentIndex}", "Default")
                    .ForJob(job)
                    .WithCronSchedule(
                        $"0/{periodInSeconds} {minuteField} {current.Hours} ? * {dayOfWeek} *",
                        x => x.InTimeZone(TimeZoneInfo.Local));

                if (triggerData != null)
                {
                    foreach (var item in triggerData)
                    {
                        triggerBuilder = triggerBuilder.UsingJobData(item.Key, item.Value);
                    }
                }

                await scheduler.ScheduleJob(triggerBuilder.Build());
                current = segmentEnd;
                segmentIndex++;
            }
        }

        private static string BuildMinuteField(TimeSpan start, TimeSpan end)
        {
            int startMinute = start.Minutes;
            int endMinuteInclusive = end.Minutes == 0 ? 59 : end.Minutes - 1;

            if (startMinute == 0 && endMinuteInclusive == 59)
                return "*";

            if (startMinute == endMinuteInclusive)
                return startMinute.ToString();

            return $"{startMinute}-{endMinuteInclusive}";
        }

        private static int GetQuartzDayOfWeek(int daysOffset)
        {
            var dayOfWeek = DateTime.Now.Date.AddDays(daysOffset).DayOfWeek;
            return dayOfWeek == DayOfWeek.Sunday ? 1 : (int)dayOfWeek + 1;
        }

        private string GetParsedDate(int daysOffset, out int morningHours, out int morningMinutes, out int eveningHours, out int eveningMinutes)
        {
            var dayOfWeek = DateTime.Now.AddDays(daysOffset).ToString("ddd", new CultureInfo("ru-RU"));
            var time = _json.delays.time[dayOfWeek].ToString().Split("-");
            var morningTime = time[0];
            var eveningTime = time[1];
            morningHours = int.Parse(morningTime.Split(":")[0]);
            eveningHours = int.Parse(eveningTime.Split(":")[0]);
            morningMinutes = int.Parse(morningTime.Split(":")[1]);
            eveningMinutes = int.Parse(eveningTime.Split(":")[1]);
            return dayOfWeek;
        }

        public void Dispose()
        {
            _defectScopes.ForEach(x => x.Dispose());
        }
    }
}
