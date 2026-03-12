using OpenCvSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using TobaccoEntities.Models;

namespace TobacoServer.Models.Services
{
    public class VideoCacheService : VideoService
    {
        private readonly int _cacheTime = 300;
        private ConcurrentDictionary<Zone, (int Number, DateTime Time)> _peopleNumbers = new ConcurrentDictionary<Zone, (int Number, DateTime Time)>();

        public ConcurrentDictionary<Zone, (CashRegisterStates State, DateTime Time)> _cashRegisterStates = new ConcurrentDictionary<Zone, (CashRegisterStates State, DateTime Time)>();
        private ConcurrentDictionary<Zone, (Mat Mat, DateTime Time)> _shots = new ConcurrentDictionary<Zone, (Mat Mat, DateTime Time)>();

        public async Task<Mat> TakeShotAsync(string cameraAddress)
        {
            return await base.TakeShot(cameraAddress);
        }


        public async Task<int> GetPeopleNumberAsync(Zone zone)
        {
            if (_peopleNumbers.TryGetValue(zone, out var cachedValue))
            {
                var now = DateTime.Now;
                var delta = now - cachedValue.Time;
                if (delta > TimeSpan.FromMilliseconds(_cacheTime))
                {
                    var peopleNumber = await base.GetPeopleNumberAsync(zone);
                    Debug.WriteLine($"cache updated ({delta})");
                    _peopleNumbers[zone] = (peopleNumber, now);
                    return peopleNumber;

                }
                else
                {
                    Debug.WriteLine("used cache");
                    return cachedValue.Number;
                }
            }
            else
            {
                var peopleNumber = await base.GetPeopleNumberAsync(zone);
                _ = _peopleNumbers.TryAdd(zone, (peopleNumber, DateTime.Now));
                return peopleNumber;
            }
        }

        public async Task<bool> GetClothesColorAsync(Zone zone)
        {
            try
            {
                return await base.GetClothesColorAsync(zone);

            }
            catch (Exception)
            {

                throw;
            }
        }
        public async Task<string> FindBottlesAsync(Zone zone)
        {
            return await base.FindBottlesAsync(zone);
        }
        public async Task<bool> AreLightsOn(Zone zone)
        {
            return await base.AreLightsOn(zone);
        }
        public async Task<string> IsBadgeOnPerson(Zone zone)
        {
            return await base.IsBadgeOnPerson(zone);
        }
        public async Task<bool> IsStallSurfaceClearAsync(Zone zone)
        {
            return await base.IsSurfaceClearAsync(zone);
        }
        public async Task<string> IsCashRegisterOpenAsync(Zone zone)
        {
            var result = await base.IsRegisterOpenAsync(zone);
            return result;
        }

        public async Task<string> IsPoseDetectedAsync(Zone zone)
        {
            return await base.GetPoseDetectedAsync(zone);
        }


        public async Task<string> IsMoppingDetectedAsync(Zone zone)
        {
            return await base.IsMoppingDetectedAsync(zone);
        }

        public async Task<bool> IsFoodDetectedAsync(Zone zone)
        {
            return await base.IsFoodDetectedAsync(zone);
        }

        public async Task<string> FindPhonesAsync(Zone zone)
        {
            return await base.FindPhonesAsync(zone);
        }
        public async Task<string> FindSmokeAsync(Zone zone)
        {
            return await base.IsSmokeOrFireAsync(zone);
        }
    }
}
