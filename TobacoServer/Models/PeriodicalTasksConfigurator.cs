using Microsoft.AspNetCore.Server.IISIntegration;
using Newtonsoft.Json;
using Quartz;
using Quartz.Impl;
using Quartz.Simpl;
using System;
using System.Diagnostics;
using System.Globalization;
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
                for (int i = 0; i < 7; i++)
                {
                    await scheduler.Start();
                    int morningHours;
                    int morningMinutes;
                    int eveningHours;
                    int eveningMinutes;
                    string identityName = GetParsedDate(i, out morningHours, out morningMinutes, out eveningHours, out eveningMinutes) + "бейдж";
                    var today = ((char)DateTime.Now.DayOfWeek);
                    int dayOfWeek = ((i + today) % 7) + 1;
                    var scope = _serviceProvider.CreateScope();
                    _defectScopes.Add(scope);
                    IJobDetail job = JobBuilder.Create<BadgeDetectionJob>().Build();
                    job.JobDataMap.Add("logger", _logger);
                    job.JobDataMap.Add("appDbContextScope", scope);
                    job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                    job.JobDataMap.Add("zoneNamePart", _json.badgeDetection.zoneNamePart);
                    ITrigger trigger = TriggerBuilder.Create()  
                    .WithIdentity($"badge_{i}", "Default")    
                      .WithCronSchedule($"0 0/{periodInMinutes} {morningHours}-{eveningHours - 1} ? * {dayOfWeek} *")
                      .Build();
                    _jobKeysThatMustBeStoppedForNight.Add(job.Key);
                    _ = await scheduler.ScheduleJob(job, trigger);
                }

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

                IJobDetail job = JobBuilder.Create<ConversionRegisterJob>()
                    .WithIdentity("ConversionRegisterJob", "Default")
                    .StoreDurably()
                    .Build();

                job.JobDataMap.Add("logger", _logger);
                job.JobDataMap.Add("appDbContextScope", scope);
                job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                job.JobDataMap.Add("zoneNamePart", _json.conversionRegister.zoneNamePart);
                job.JobDataMap.Add("clientZoneNamePart", _json.conversionRegister.clientZoneNamePart);
                job.JobDataMap.Add("period", period);

                await scheduler.AddJob(job, replace: true);

                for (int i = 0; i < 7; i++)
                {
                    var today = ((char)DateTime.Now.DayOfWeek);
                    int dayOfWeek = ((i + today) % 7) + 1;
                    int morningHours, eveningHours;
                    string identityName = GetParsedDate(i, out morningHours, out _, out eveningHours, out _);

                    ITrigger trigger = TriggerBuilder.Create()
                        .WithIdentity($"ConversionRegisterTrigger_{dayOfWeek}", "Default")
                        .ForJob(job)
                        .WithCronSchedule($"0/{period / 1000} * {morningHours}-{eveningHours - 1} ? * {dayOfWeek} *")
                        .Build();

                    await scheduler.ScheduleJob(trigger);
                }
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
                var period = _json.moppingDetection.period;
                var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);

                for (int i = 0; i < 7; i++)
                {
                    await scheduler.Start();
                    var job = JobBuilder.Create<MoppingJob>().StoreDurably().Build();

                    int morningHours;
                    int morningMinutes;
                    int eveningHours;
                    int eveningMinutes;
                    string identityName = GetParsedDate(i, out morningHours, out morningMinutes, out eveningHours, out eveningMinutes);
                    DateTime date = DateTime.Today.AddDays(i).AddHours(morningHours).AddMinutes(morningMinutes);
                    identityName = date.ToString("dd.MM.yyyy");

                    var today = ((char)DateTime.Now.DayOfWeek);
                    int dayOfWeek = ((i + today) % 7) + 1;

                    var morningTrigger = TriggerBuilder.Create().WithIdentity(identityName, "Morning")
                        .WithCronSchedule($"0/{period / 1000} * {morningHours}-{morningHours + 1} ? * {dayOfWeek} *",
                        x => x.InTimeZone(TimeZoneInfo.Local)).ForJob(job).Build();

                    var eveningTrigger = TriggerBuilder.Create().WithIdentity(identityName, "Evening")
                       .WithCronSchedule($"0/{period / 1000} * {eveningHours - 1}-{eveningHours} ? * {dayOfWeek} *", x => x.InTimeZone(TimeZoneInfo.Local)).ForJob(job).Build();

                    job.JobDataMap.Add("logger", _logger);
                    job.JobDataMap.Add("appDbContextScope", scope);
                    job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                    job.JobDataMap.Add("zoneNamePart", _json.moppingDetection.zoneNamePart);

                    await scheduler.AddJob(job, false);

                    await scheduler.ScheduleJob(morningTrigger);
                    await scheduler.ScheduleJob(eveningTrigger);
                }
            }
        }

        private async Task ConfigureCashRegisterRecountingDetectionAsync()
        {
            bool isActive = _json.countingCashRegisterCheck.isActive;

            if (isActive)
            {
                var period = _json.countingCashRegisterCheck.period;
                var rangeBeforeInMinutes = (int)_json.countingCashRegisterCheck.rangeBeforeInMinutes;
                var rangeAfterInMinutes = (int)_json.countingCashRegisterCheck.rangeAfterInMinutes;
                var scheduler = await StdSchedulerFactory.GetDefaultScheduler();
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);

                for (int i = 0; i < 7; i++)
                {
                    await scheduler.Start();
                    var today = ((char)DateTime.Now.DayOfWeek);
                    int dayOfWeek = ((i + today) % 7) + 1;

                    int morningHours;
                    int morningMinutes;
                    int eveningHours;
                    int eveningMinutes;
                    string identityName = GetParsedDate(i, out morningHours, out morningMinutes, out eveningHours, out eveningMinutes) + "HumanDetectionBeforeAndAfterShift";

                    var morningTime = new TimeSpan(morningHours, morningMinutes, 0);
                    var eveningTime = new TimeSpan(eveningHours, eveningMinutes, 0);
                    var morningTimeToEnd = morningTime + TimeSpan.FromMinutes(rangeAfterInMinutes);

                    var job = JobBuilder.Create<CashRegisterRecountingJob>().StoreDurably().Build();
                    var morningTrigger = TriggerBuilder.Create()
                        .WithIdentity($"CountingCashRegisterMorningTrigger_{identityName}")
                        .WithCronSchedule($"0/{period / 1000}" +
                        $" * {(morningTime - TimeSpan.FromMinutes(rangeBeforeInMinutes)).Hours}" +
                        $"-{(morningTime + TimeSpan.FromMinutes(rangeAfterInMinutes)).Hours} ? * {dayOfWeek} * ",
                        x => x.InTimeZone(TimeZoneInfo.Local)).ForJob(job).Build();

                    var eveningTrigger = TriggerBuilder.Create()
                        .WithIdentity($"CountingCashRegisterEveningTrigger_{identityName}")
                        .WithCronSchedule($"0/{period / 1000} " +
                        $"* {(eveningTime - TimeSpan.FromMinutes(rangeAfterInMinutes)).Hours}-{eveningHours} ? * {dayOfWeek} * ",
                        x => x.InTimeZone(TimeZoneInfo.Local)).ForJob(job).Build();

                    job.JobDataMap.Add("logger", _logger);
                    job.JobDataMap.Add("appDbContextScope", scope);
                    job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                    job.JobDataMap.Add("period", period);
                    job.JobDataMap.Add("zoneNamePart", _json.countingCashRegisterCheck.zoneNamePart);
                    job.JobDataMap.Add("clientZoneNamePart", _json.countingCashRegisterCheck.clientZoneNamePart);
                    job.JobDataMap.Add("morningTimeToEnd", morningTimeToEnd);
                    job.JobDataMap.Add("eveningTimeToEnd", eveningTime);

                    await scheduler.AddJob(job, false);

                    await scheduler.ScheduleJob(morningTrigger);
                    await scheduler.ScheduleJob(eveningTrigger);
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
                var rangeBeforeInMinutes = (int)_json.countingCashRegisterCheck.rangeBeforeInMinutes;
                var rangeAfterInMinutes = (int)_json.countingCashRegisterCheck.rangeAfterInMinutes;
                var scope = _serviceProvider.CreateScope();
                _defectScopes.Add(scope);
                for (int i = 0; i < 7; i++)
                {
                    await scheduler.Start();
                    var today = ((char)DateTime.Now.DayOfWeek);
                    int dayOfWeek = ((i + today) % 7) + 1;

                    int morningHours;
                    int morningMinutes;
                    int eveningHours;
                    int eveningMinutes;
                    string identityName = GetParsedDate(i, out morningHours, out morningMinutes, out eveningHours, out eveningMinutes) + "HumanDetectionBeforeAndAfterShift";

                    var morningStartTime = new TimeSpan(morningHours, morningMinutes, 0) + TimeSpan.FromMinutes(rangeAfterInMinutes);
                    var eveningEndTime = new TimeSpan(eveningHours, eveningMinutes, 0) - TimeSpan.FromMinutes(rangeAfterInMinutes);

                    var job = JobBuilder.Create<CashRegisterCheckJob>().StoreDurably().Build();

                    var trigger = TriggerBuilder.Create()
                        .WithIdentity($"CashRegisterTrigger_{identityName}")
                        .WithCronSchedule($"0/{period / 1000} {morningStartTime.Hours}-{eveningEndTime.Hours} * ? * {dayOfWeek} *",
                        x => x.InTimeZone(TimeZoneInfo.Local)).ForJob(job).Build();

                    job.JobDataMap.Add("logger", _logger);
                    job.JobDataMap.Add("appDbContextScope", scope);
                    job.JobDataMap.Add("videoCacheService", _serviceProvider.GetService<VideoCacheService>());
                    job.JobDataMap.Add("period", _json.cashRegisterCheck.period);
                    job.JobDataMap.Add("zoneNamePart", _json.cashRegisterCheck.zoneNamePart);
                    job.JobDataMap.Add("clientZoneNamePart", _json.cashRegisterCheck.clientZoneNamePart);
                    job.JobDataMap.Add("isRecountingInProgress", CashRegisterRecountingStatus.IsRecountingInProgress);
                    _ = await scheduler.ScheduleJob(job, trigger);
                }
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
                string dayName = GetParsedDate(i, out morningHours, out morningMinutes, out eveningHours, out eveningMinutes);
                var today = ((char)DateTime.Now.DayOfWeek);
                int dayOfWeek = ((i + today) % 7) + 1;
                int eveningStartHour = eveningHours + 1;
                int eveningEndHour = 23;

                int morningStartHour = 0;
                int morningEndHour = morningHours - 1;

                if (eveningStartHour <= eveningEndHour)
                {
                    string cronEvening = $"0/{period / 1000} * {eveningStartHour}-{eveningEndHour} ? * {dayOfWeek} *";

                    ITrigger eveningTrigger = TriggerBuilder.Create()
                        .WithIdentity($"EveningTrigger_{dayName}", "Default")
                        .ForJob(job)
                        .WithCronSchedule(cronEvening)
                        .Build();

                    await scheduler.ScheduleJob(eveningTrigger);
                }

                int nextDay = (dayOfWeek % 7) + 1;

                if (morningStartHour <= morningEndHour)
                {
                    string cronMorning = $"0/{period / 1000} * {morningStartHour}-{morningEndHour} ? * {nextDay} *";

                    ITrigger morningTrigger = TriggerBuilder.Create()
                        .WithIdentity($"MorningTrigger_{dayName}", "Default")
                        .ForJob(job)
                        .WithCronSchedule(cronMorning)
                        .Build();

                    await scheduler.ScheduleJob(morningTrigger);
                }
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
