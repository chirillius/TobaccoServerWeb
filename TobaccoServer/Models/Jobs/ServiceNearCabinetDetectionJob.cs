using OpenCvSharp;
using Quartz;
using TobaccoEntities.Models;
using TobaccoEntities.Models.DTOs.Vision;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.ImageSavers;
using TobacoServer.Models.Services;

namespace TobacoServer.Models.Jobs
{
    public enum ServiceNearCabinetFollowUpDecision
    {
        Ignore,
        Served,
        Failure
    }

    public class ServiceNearCabinetFailureSuppressionState
    {
        private const int RequiredChangedIntervals = 3;
        private int _changedIntervals;

        public bool IsSuppressed { get; private set; }

        public void MarkFailureRecorded()
        {
            IsSuppressed = true;
            _changedIntervals = 0;
        }

        public bool ShouldSkip(ServiceNearCabinetAnalysisResponse analysis)
        {
            if (!IsSuppressed)
            {
                return false;
            }

            if (analysis.ClientPeople == 0 || analysis.ClientPeople > 1)
            {
                _changedIntervals++;
            }
            else
            {
                _changedIntervals = 0;
            }

            if (_changedIntervals >= RequiredChangedIntervals)
            {
                IsSuppressed = false;
                _changedIntervals = 0;
                return false;
            }

            return true;
        }
    }

    [DisallowConcurrentExecution]
    public class ServiceNearCabinetDetectionJob : IJob
    {
        private static readonly TimeSpan FollowUpDelay = TimeSpan.FromSeconds(10);
        private static readonly object SuppressionLock = new();
        private static readonly Dictionary<string, ServiceNearCabinetFailureSuppressionState> SuppressionStates = new();

        public async Task Execute(IJobExecutionContext context)
        {
            var dbScope = context.MergedJobDataMap["appDbContextScope"] as IServiceScope;
            var db = dbScope?.ServiceProvider.GetService<AppDbContext>()
                ?? throw new InvalidOperationException("AppDbContext scope was not provided for ServiceNearCabinetDetectionJob.");
            var videoService = context.MergedJobDataMap["videoCacheService"] as VideoCacheService
                ?? throw new InvalidOperationException("VideoCacheService was not provided for ServiceNearCabinetDetectionJob.");
            var zoneNamePart = context.MergedJobDataMap["zoneNamePart"]?.ToString()
                ?? throw new InvalidOperationException("zoneNamePart was not provided for ServiceNearCabinetDetectionJob.");
            var stallZoneNamePart = context.MergedJobDataMap["stallZoneNamePart"]?.ToString()
                ?? throw new InvalidOperationException("stallZoneNamePart was not provided for ServiceNearCabinetDetectionJob.");
            var clientZoneNamePart = context.MergedJobDataMap["clientZoneNamePart"]?.ToString()
                ?? throw new InvalidOperationException("clientZoneNamePart was not provided for ServiceNearCabinetDetectionJob.");
            var zonesConfigurator = new ZonesConfigurator();
            var zones = zonesConfigurator.GetZones();
            var clientZones = FindZones(zones, clientZoneNamePart);
            var cabinetZones = FindZones(zones, zoneNamePart);
            var stallZones = FindZones(zones, stallZoneNamePart);

            var suppressionState = GetSuppressionState(zoneNamePart, clientZoneNamePart, stallZoneNamePart);
            var initialAnalysis = await AnalyzeAsync(videoService, cabinetZones, clientZones, stallZones);
            if (ShouldSkipBecauseFailureWasAlreadyRecorded(suppressionState, initialAnalysis))
            {
                return;
            }

            if (!IsInitialServiceSituation(initialAnalysis))
            {
                return;
            }

            var startDateTime = DateTime.Now;
            await Task.Delay(FollowUpDelay);
            var followUpAnalysis = await AnalyzeAsync(videoService, cabinetZones, clientZones, stallZones);
            var decision = GetFollowUpDecision(initialAnalysis, followUpAnalysis);

            if (decision == ServiceNearCabinetFollowUpDecision.Failure)
            {
                var saved = await SaveFailureAsync(db, videoService, cabinetZones, startDateTime, DateTime.Now);
                if (saved)
                {
                    MarkFailureRecorded(suppressionState);
                }
            }
        }

        public static ServiceNearCabinetFollowUpDecision GetFollowUpDecision(
            ServiceNearCabinetAnalysisResponse initialAnalysis,
            ServiceNearCabinetAnalysisResponse followUpAnalysis)
        {
            if (!IsInitialServiceSituation(initialAnalysis))
            {
                return ServiceNearCabinetFollowUpDecision.Ignore;
            }

            if (followUpAnalysis.StallPeople == 0 && followUpAnalysis.ClientPeople > 1)
            {
                return ServiceNearCabinetFollowUpDecision.Served;
            }

            if (followUpAnalysis.CabinetPeople == 1
                && followUpAnalysis.ClientPeople == 1
                && followUpAnalysis.StallPeople == 1)
            {
                return ServiceNearCabinetFollowUpDecision.Failure;
            }

            return ServiceNearCabinetFollowUpDecision.Ignore;
        }

        private static bool IsInitialServiceSituation(ServiceNearCabinetAnalysisResponse analysis)
        {
            return analysis.CabinetPeople == 1
                && analysis.ClientPeople == 1
                && analysis.StallPeople == 1;
        }

        private static ServiceNearCabinetFailureSuppressionState GetSuppressionState(
            string zoneNamePart,
            string clientZoneNamePart,
            string stallZoneNamePart)
        {
            var key = $"{zoneNamePart}|{clientZoneNamePart}|{stallZoneNamePart}";
            lock (SuppressionLock)
            {
                if (!SuppressionStates.TryGetValue(key, out var state))
                {
                    state = new ServiceNearCabinetFailureSuppressionState();
                    SuppressionStates[key] = state;
                }

                return state;
            }
        }

        private static bool ShouldSkipBecauseFailureWasAlreadyRecorded(
            ServiceNearCabinetFailureSuppressionState suppressionState,
            ServiceNearCabinetAnalysisResponse analysis)
        {
            lock (SuppressionLock)
            {
                return suppressionState.ShouldSkip(analysis);
            }
        }

        private static void MarkFailureRecorded(ServiceNearCabinetFailureSuppressionState suppressionState)
        {
            lock (SuppressionLock)
            {
                suppressionState.MarkFailureRecorded();
            }
        }

        private static List<Zone> FindZones(IEnumerable<Zone> zones, string namePart)
        {
            return zones
                .Where(zone => !string.IsNullOrWhiteSpace(zone.Name)
                    && zone.Name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static async Task<ServiceNearCabinetAnalysisResponse> AnalyzeAsync(
            VideoCacheService videoService,
            List<Zone> cabinetZones,
            List<Zone> clientZones,
            List<Zone> stallZones)
        {
            return await videoService.AnalyzeServiceNearCabinetAsync(new ServiceNearCabinetAnalysisRequest
            {
                CabinetZones = cabinetZones,
                ClientZones = clientZones,
                StallZones = stallZones,
            });
        }

        private static async Task<bool> SaveFailureAsync(
            AppDbContext db,
            VideoCacheService videoService,
            IEnumerable<Zone> cabinetZones,
            DateTime startDateTime,
            DateTime endDateTime)
        {
            var images = new List<Mat>();
            try
            {
                foreach (var cameraAddress in cabinetZones.Select(zone => zone.CameraAddress).Distinct())
                {
                    images.Add(await videoService.TakeShotAsync(cameraAddress));
                }

                using var grid = DefectImagesSaver.CreateImageGrid(images);
                if (grid.Empty())
                {
                    return false;
                }

                var path = DefectImagesSaver.Save("Grid", grid, "serviceNearCabinet");
                db.ServiceNearCabinetFailures.Add(new ServiceNearCabinetFailure()
                {
                    StartDateTime = startDateTime,
                    EndDateTime = endDateTime,
                    DefectImage = new DefectImage() { Path = path }
                });
                db.SaveChanges();
                return true;
            }
            finally
            {
                images.ForEach(image => image.Dispose());
            }
        }
    }
}
