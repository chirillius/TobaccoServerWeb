using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;
using OpenCvSharp;
using Quartz.Impl.Triggers;
using System;
using TobaccoEntities.Models;
using TobaccoEntities.Models.DTOs.Vision;
using TobaccoEntities.Models.Neuro;

namespace TobacoServer.Models.Services
{
    public class VideoService
    {
        //TODO: Да, я знаю, это ужас, надо сделать один клиент
        //protected HttpClient _peopleDetectionClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["VideoServiceAddress"]) };
        //protected HttpClient _utilsClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["Utils"]) };
        //protected HttpClient _smokeDetectionClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["SmokeServiceAddress"]) };
        //protected HttpClient _phoneDetectionClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["PhoneServiceAddress"]) };
        //protected HttpClient _foodDetectionClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["FoodServiceAddress"]) };
        //protected HttpClient _poseClassificationClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["PoseServiceAddress"]) };
        //protected HttpClient _moppingClassificationClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["MoppingServiceAddress"]) };
        //protected HttpClient _cashRegisterClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["CashRegisterServiceAddress"]) };
        //protected HttpClient _clothesControlClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["ClothesControlServiceAddress"]) };
        //protected HttpClient _stallSurfaceControlClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["StallSurfaceServiceAddress"]) };
        //protected HttpClient _bottleDetectionClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["BottleDetectionServiceAddress"]) };
        //protected HttpClient _badgeDetectionClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["BadgeDetectionServiceAddress"]) };
        //protected HttpClient _lightDetectionClient = new HttpClient() { BaseAddress = new Uri(System.Configuration.ConfigurationManager.AppSettings["LightDetectionServiceAddress"]) };

        protected string _peopleDetectionAddress = System.Configuration.ConfigurationManager.AppSettings["VideoServiceAddress"];
        protected string _utilsAddress = System.Configuration.ConfigurationManager.AppSettings["Utils"];
        protected string _smokeDetectionAddress = System.Configuration.ConfigurationManager.AppSettings["SmokeServiceAddress"];
        protected string _phoneDetectionAddress = System.Configuration.ConfigurationManager.AppSettings["PhoneServiceAddress"];
        protected string _foodDetectionAddress = System.Configuration.ConfigurationManager.AppSettings["FoodServiceAddress"];
        protected string _poseClassificationAddress = System.Configuration.ConfigurationManager.AppSettings["PoseServiceAddress"];
        protected string _moppingDetectionAddress = System.Configuration.ConfigurationManager.AppSettings["MoppingServiceAddress"];
        protected string _cashRegisterClassificationAddress = System.Configuration.ConfigurationManager.AppSettings["CashRegisterServiceAddress"];
        protected string _clothesClassificationAddress = System.Configuration.ConfigurationManager.AppSettings["VideoServiceAddress"];
        protected string _stallSurfaceClassificationAddress = System.Configuration.ConfigurationManager.AppSettings["VideoServiceAddress"];
        protected string _bottleDetectionAddress = System.Configuration.ConfigurationManager.AppSettings["BottleDetectionServiceAddress"];
        protected string _badgeDetectionAddress = System.Configuration.ConfigurationManager.AppSettings["BadgeDetectionServiceAddress"];
        protected string _lightDetectionAddress = System.Configuration.ConfigurationManager.AppSettings["LightDetectionServiceAddress"];
        protected string _frameCapturerAddress = System.Configuration.ConfigurationManager.AppSettings["FrameCapturer"];

        private static readonly HttpClient _httpClient = new HttpClient();
        protected HttpClient client => _httpClient;

        protected Dictionary<Zone, DateTime> _peopleNumberCache = new Dictionary<Zone, DateTime>();

        protected async Task<Mat> TakeShot(string cameraAddress)
        {
            var response = await _httpClient.GetAsync(_frameCapturerAddress + $"frame?cameraAddress={cameraAddress}");
            var stream = await response.Content.ReadAsStreamAsync();

            var buffer = new byte[stream.Length];
            _ = stream.Read(buffer, 0, buffer.Length);
            var image = Cv2.ImDecode(buffer, ImreadModes.Unchanged);
            return image;
        }

        protected async Task<bool> AreLightsOn(Zone zone)
        {
            var response = await _httpClient.PostAsync(_lightDetectionAddress + "light", JsonContent.Create(zone));
            var result = JsonConvert.DeserializeObject<Dictionary<string, bool>>(await response.Content.ReadAsStringAsync())["light"];
            return result;
        }

        protected async Task<string> FindBottlesAsync(Zone zone)
        {
            var response = await _httpClient.PostAsync(_bottleDetectionAddress + "bottles", JsonContent.Create(zone));
            var result = await response.Content.ReadAsStringAsync();
            return result;
        }

        protected async Task<string> IsBadgeOnPerson(Zone zone)
        {
            var response = await _httpClient.PostAsync(_badgeDetectionAddress + "badge", JsonContent.Create(zone));
            var result = await response.Content.ReadAsStringAsync();
            return result;
        }

        protected async Task<string> IsRegisterOpenAsync(Zone zone)
        {
            var response = await _httpClient.PostAsync(_cashRegisterClassificationAddress + "cash-register", JsonContent.Create(zone));
            var result = await response.Content.ReadAsStringAsync();
            return result;
        }

        protected async Task<bool> GetClothesColorAsync(Zone zone)
        {
            var response = await _httpClient.PostAsync(_clothesClassificationAddress + "are-clothes-black", JsonContent.Create(new ClothesColorRequest
            {
                Rectangle = zone.Rectangle,
                Polygon = zone.Polygon,
                ReferenceWidth = zone.ReferenceWidth,
                ReferenceHeight = zone.ReferenceHeight,
                CameraAddress = zone.CameraAddress
            }));
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                throw new Exception(await response.Content.ReadAsStringAsync());
            }

            var payload = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<ClothesColorResponse>(payload)
                ?? throw new Exception("Не удалось получить результат анализа цвета одежды.");

            return result.AreClothesBlack;
        }

        protected async Task<bool> IsSurfaceClearAsync(Zone zone)
        {
            var response = await _httpClient.PostAsync(_stallSurfaceClassificationAddress + "is-surface-clear", JsonContent.Create(new ClearStallRequest
            {
                Rectangle = zone.Rectangle,
                Polygon = zone.Polygon,
                ReferenceWidth = zone.ReferenceWidth,
                ReferenceHeight = zone.ReferenceHeight,
                CameraAddress = zone.CameraAddress
            }));
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(await response.Content.ReadAsStringAsync());
            }

            var payload = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<ClearStallResponse>(payload)
                ?? throw new Exception("Не удалось получить результат анализа поверхности.");

            return result.Clear;
        }

        protected async Task<string> IsMoppingDetectedAsync(Zone zone)
        {

            var response = await _httpClient.PostAsync(_moppingDetectionAddress + "mopping", JsonContent.Create(zone));
            var result = await response.Content.ReadAsStringAsync();
            return result;
        }
        protected async Task<string> GetPoseDetectedAsync(Zone zone)
        {
            var response = await _httpClient.PostAsync(_poseClassificationAddress + "pose", JsonContent.Create(zone));
            var result = await response.Content.ReadAsStringAsync();
            return result;
        }

        protected async Task<bool> IsFoodDetectedAsync(Zone zone)
        {
            var response = await _httpClient.PostAsync(_foodDetectionAddress + "food", JsonContent.Create(new Dictionary<string, object>()
                {
                    { "rectangle", zone.Rectangle },
                    { "polygon", zone.Polygon },
                    { "referenceWidth", zone.ReferenceWidth },
                    { "referenceHeight", zone.ReferenceHeight },
                    { "cameraAddress", zone.CameraAddress }
            }));
            var result = JsonConvert.DeserializeObject<Dictionary<string, bool>>(await response.Content.ReadAsStringAsync())["food"];
            return result;
        }

        protected async Task<ServiceNearCabinetAnalysisResponse> AnalyzeServiceNearCabinetAsync(ServiceNearCabinetAnalysisRequest request)
        {
            var response = await _httpClient.PostAsync(_peopleDetectionAddress + "service-near-cabinet/analyze", JsonContent.Create(request));
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(await response.Content.ReadAsStringAsync());
            }

            var payload = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ServiceNearCabinetAnalysisResponse>(payload)
                ?? new ServiceNearCabinetAnalysisResponse();
        }

        protected async Task<string> FindPhonesAsync(Zone zone)
        {
            var response = await _httpClient.PostAsync(_phoneDetectionAddress + "phones", JsonContent.Create(zone));
            var result = await response.Content.ReadAsStringAsync();
            return result;
        }

        protected async Task<string> IsSmokeOrFireAsync(Zone zone)
        {
            try
            {
                var response = await _httpClient.PostAsync(_smokeDetectionAddress + "smoke", JsonContent.Create(zone));
                var result = await response.Content.ReadAsStringAsync();
                return result;

            }
            catch (Exception ex)
            {

                throw;
            }
        }

        protected async Task<int> GetPeopleNumberAsync(Zone zone)
        {
            try
            {
                var response = await _httpClient.PostAsync(_peopleDetectionAddress + "get-people-number", JsonContent.Create(zone));
                var str = await response.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<Dictionary<string, int>>(str);
                return data["peopleNumber"];
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        protected async Task<DirectionalEntryCountResponse> GetDirectionalEntryCountAsync(Zone zone)
        {
            var response = await _httpClient.PostAsync(_peopleDetectionAddress + "directional-entry-count", JsonContent.Create(zone));
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(await response.Content.ReadAsStringAsync());
            }

            var payload = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<DirectionalEntryCountResponse>(payload)
                ?? new DirectionalEntryCountResponse();
        }

        protected async Task<CashRegisterStates> GetCashRegisterStateAsync(Zone zone)
        {
            var response = await _httpClient.PostAsync(_cashRegisterClassificationAddress + "cash-register-state", JsonContent.Create(new Dictionary<string, object>()
            {
                {
                    "rectangle", zone.Rectangle
                },
                {
                    "cameraAddress", zone.CameraAddress
                },
                { "polygon", zone.Polygon },
                { "referenceWidth", zone.ReferenceWidth },
                { "referenceHeight", zone.ReferenceHeight }
            }));

            var state = await response.Content.ReadAsStringAsync();
            return state == "open"
                ? CashRegisterStates.Open
                : state == "closed" ? CashRegisterStates.Closed : throw new Exception("Unknown cash register state: " + state);
        }
    }
}
