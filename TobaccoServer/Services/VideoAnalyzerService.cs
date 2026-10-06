using Microsoft.VisualBasic;
using NAudio.MediaFoundation;
using NAudio.Wave;
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Threading.Tasks;
using TobaccoEntities.Models;
using TobacoServer.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Services;

namespace TobacoServer.Services
{
    class VideoAnalyzerService : IDisposable
    {
        private ConcurrentDictionary<string, (string fileName, Process process, DateTime dateTime)> _procecces = new ConcurrentDictionary<string, (string fileName, Process process, DateTime dateTime)>();
        private string _videosFolder = "";
        private int _iterationsBeforeStop = 6;
        private int _secondsToSkip = 10;
        private int _minimalActionArea = 200;
        private ILogger _logger;
        private WaveInEvent waveSource;
        private List<float> amplitudes;
        private List<bool> _soundDetected = new List<bool>();
        private const int bufferSize = 1024; 
        private static object _audioLock = new object();
        private bool _running = true;
        private DateTime _movementEndTime = DateTime.MaxValue;
        private VideoCacheService _videoCacheService;
        private AppDbContext _db;
        private static bool _isDbRecordingNeeded = true;
        private static object _dbLock = new object();
        public VideoAnalyzerService(string outputFolder, ILogger logger, VideoCacheService videoCacheService, AppDbContext context)
        {
            _logger = logger;
            _videosFolder = outputFolder;
            _minimalActionArea = int.Parse(System.Configuration.ConfigurationManager.AppSettings["MinimalActionArea"]);
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            _db = context;
            _videoCacheService = videoCacheService;
        }

        private void OnProcessExit(object? sender, EventArgs e)
        {
            _running = false;
            Thread.Sleep(1000);
            Dispose();
        }

        public void AnalyzeStream(string cameraName, string rtspUrl)
        {
            Task.Run(async () =>
            {
                var currentFolderName = Path.Combine(_videosFolder, DateTime.Now.ToString("dd-MM-yyyy"));
                var isStoppingNeeded = 0;
                var capture = new VideoCapture(rtspUrl);
                using var MOG2 = OpenCvSharp.BackgroundSubtractorMOG2.Create(250, 39);
                var i = 0;
                var isSoundDetected = false;
                Point[][] contours;
                HierarchyIndex[] hierarchy;
                using var blurredFrame = new Mat();
                using var motionFrame = new Mat();
                using var dilatedFrame = new Mat();
                using var thresholdedFrame = new Mat();
                var resizeSize = new OpenCvSharp.Size(640, 360);
                using var result = new Mat(360, 640, MatType.CV_8UC3, new Scalar(0, 0, 0));
                var zonesConfigurator = new ZonesConfigurator();
                var gaussianBlurKernelSize = new Size(15, 15);
                var dilationStructuringElement = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
                var zones = zonesConfigurator.GetZones().Where(x => x.Name.ToLower().Contains("прилавок")).ToList();
                using Mat resizedFrame = new Mat();



                while (_running)
                {
                    if (!capture.IsOpened())
                    {
                        capture = new VideoCapture(rtspUrl);
                    }

                    capture.Grab();
                    i++;
                    if (i % 100 != 0)
                    {
                        continue;
                    }

                    using var frame = capture.RetrieveMat();
                    i = 0;

                    if (frame.Empty())
                    {
                        var time = DateTime.Now;
                        capture.Release();
                        capture.Dispose();
                        capture = new VideoCapture(rtspUrl);
                        Console.WriteLine($"Created new videocapture: {DateTime.Now - time}");
                        continue;
                    }

                    Cv2.Resize(frame, resizedFrame, resizeSize);
                    Cv2.GaussianBlur(resizedFrame, blurredFrame, gaussianBlurKernelSize, 1);
                    MOG2.Apply(blurredFrame, motionFrame);
                    Cv2.Threshold(motionFrame, thresholdedFrame, 128, 255, ThresholdTypes.Binary);
                    Cv2.Dilate(thresholdedFrame, dilatedFrame, dilationStructuringElement);
                    Cv2.FindContours(dilatedFrame, out contours, out hierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                    if (contours is not null && contours.Length > 0)
                    {
                        contours = contours.Where(x => Cv2.ContourArea(x) >= _minimalActionArea).ToArray();
                        if (contours.Length > 0 || isSoundDetected)
                        {
                            lock (_dbLock)
                            {
                                _isDbRecordingNeeded = true;
                            }

                            if (_movementEndTime != DateTime.MaxValue)
                            {
                                _movementEndTime = DateTime.MaxValue;
                            }
                            if (!_procecces.ContainsKey(rtspUrl))
                            {
                                currentFolderName = Path.Combine(_videosFolder, DateTime.Now.ToString("dd-MM-yyyy"));
                                var ffmpegProcess = StartRecording(cameraName, rtspUrl);
                                Console.WriteLine($"Started {rtspUrl}");
                            }
                            isStoppingNeeded = 0;
                        }
                        else
                        {
                            if (_movementEndTime == DateTime.MaxValue)
                            {
                                _movementEndTime = DateTime.Now;
                            }
                            else
                            {
                                if (DateTime.Now - _movementEndTime > new TimeSpan(0, 10, 0))
                                {
                                    var stallZonesPeopleNumber = zones.Select(async x => await _videoCacheService.GetPeopleNumberAsync(x)).Sum(x => x.Result);
                                    if (stallZonesPeopleNumber > 0)
                                    {
                                        lock (_dbLock)
                                        {
                                            if (_isDbRecordingNeeded)
                                            {
                                                _db.InactiveSalesmanFailures.Add(new InactiveSalesmanFailure() { DateTime = DateTime.Now, DefectImage = new DefectImage() });
                                                _db.SaveChanges();
                                                _isDbRecordingNeeded = false;
                                            }
                                        }
                                    }

                                }
                            }
                            if (_procecces.ContainsKey(rtspUrl))
                            {
                                if (isStoppingNeeded >= _iterationsBeforeStop)
                                {
                                    await StopRecording(rtspUrl);
                                }
                                else
                                {
                                    isStoppingNeeded++;
                                    SkipSeconds(capture, _secondsToSkip);
                                }
                            }
                        }
                    }
                    else
                    {
                        if (_procecces.ContainsKey(rtspUrl))
                        {
                            if (isStoppingNeeded >= _iterationsBeforeStop)
                            {
                                await StopRecording(rtspUrl);
                            }
                            else
                            {
                                isStoppingNeeded++;
                                SkipSeconds(capture, _secondsToSkip);
                            }
                        }
                    }
                    frame.Release();
                    frame.Dispose();
                }
                _logger.LogInformation($"CAP.ISOPEN() IS FALSE, QUITING THE CYCLE");
                capture.Dispose();
            });
        }

        private static bool IsError(string errorLine)
        {
            var isIgnoringError = errorLine.Contains("too many bits")
                || errorLine.Contains("input #0, rtsp, from")
                || errorLine.Contains("timestamps are unset in a packet");
            return !isIgnoringError && (errorLine.Contains("error") || errorLine.Contains("@"));
        }

        

        private Process StartRecording(string cameraName, string rtspUrl)
        {
            ArchiveHelper.RequestPurgeIfNeeded(50);
            Directory.CreateDirectory(Path.Combine(_videosFolder, DateTime.Now.ToString("dd-MM-yyyy"), "origin", cameraName));

            var fileName = $"{Path.Combine(_videosFolder, DateTime.Now.ToString("dd-MM-yyyy"), "origin", cameraName, Path.GetRandomFileName())}.mp4";
            var ffmpegProcess = new Process();
            ffmpegProcess.StartInfo.FileName = "ffmpeg.exe";
            ffmpegProcess.StartInfo.Arguments = $" -rtsp_transport tcp -threads 1 -i {rtspUrl} " +

            // -abort_on empty_output
            //// использовать системные временные метки вместо тех, что могут быть в самом потоке данных
            //$" -use_wallclock_as_timestamps 1 " +
            //// сбросить и начать заново временные метки (PTS/DTS) внутри обработанных сегментов или выходных файлов
            //" -reset_timestamps 1 " +
            //" -vsync 0 -enc_time_base demux " +


            // фрагментировать MP4 файл, индекс всех фрагментов оставляет пустым и помещает в начало файла
            //$" -movflags +frag_keyframe+empty_moov+faststart " +
            //$" -movflags +frag_keyframe+separate_moof+omit_tfhd_offset+empty_moov+faststart " +
            //$" -movflags +frag_keyframe+separate_moof+faststart " +
            $" -c:v copy {fileName} ";
            //$" -c:v h264_nvenc -preset fast {fileName} ";

            //ffmpegProcess.StartInfo.FileName = "C:\\Program Files\\VideoLAN\\VLC\\vlc.exe";
            //ffmpegProcess.StartInfo.Arguments = $" {rtspUrl} --sout \"#transcode{{vcodec=h264,acodec=mp4a}}:standard{{access=file,mux=mp4,dst='{fileName}'}}\" --rtsp-tcp --network-caching=1000";
            try
            {
                ffmpegProcess.StartInfo.UseShellExecute = false;
                ffmpegProcess.StartInfo.RedirectStandardInput = true;
                ffmpegProcess.StartInfo.RedirectStandardOutput = false;
                ffmpegProcess.StartInfo.RedirectStandardError = true;
                ffmpegProcess.StartInfo.CreateNoWindow = true;

                ffmpegProcess.ErrorDataReceived += OnErrorMessageRecieved;

                ffmpegProcess.Start();

                //ffmpegProcess.PriorityClass = ProcessPriorityClass.RealTime;
                ffmpegProcess.BeginErrorReadLine();

                ArchiveHelper.StartSendingHeartbeat(Path.Combine(_videosFolder, DateTime.Now.ToString("dd-MM-yyyy")), TrackingType.Folder);
                _logger.LogInformation($"Начата запись | {rtspUrl}");
                _procecces.TryAdd(rtspUrl, (fileName, ffmpegProcess, DateTime.Now));

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при запуске записи для {rtspUrl}");
                throw;
            }

            return ffmpegProcess;
        }

        private void OnErrorMessageRecieved(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is not null && IsError(e.Data.ToLower()))
            {
                _logger.LogError($"{sender} {e.Data}");
            }
        }

        async Task StopRecording(string rtspUrl)
        {
            try
            {
                var start = DateTime.Now;
                _logger.LogInformation($"Вызов StopRecording | {rtspUrl}");
                var ffmpegProcess = _procecces[rtspUrl].process;
                if (ffmpegProcess != null)
                {
                    (string fileName, Process process, DateTime dateTime) process;
                    _procecces.Remove(rtspUrl, out process);
                    ffmpegProcess.StandardInput.WriteLine("q");
                    _logger.LogInformation($"Начато ожидание завершения процесса. Удаление из индекса заняло {(start - DateTime.Now).TotalMilliseconds} | {rtspUrl}");
                    start = DateTime.Now;
                    await ffmpegProcess.WaitForExitAsync();
                    _logger.LogInformation($"Начато переименование файла. Ожидание завершения процесса {(start - DateTime.Now).TotalMilliseconds}| {rtspUrl}");

                    Console.WriteLine($"Stopped {rtspUrl}");
                    start = DateTime.Now;
                    var finalName = process.fileName.Replace(Path.GetFileName(process.fileName), $"{process.dateTime.ToString($"HH-mm-ss.fff")}_{DateTime.Now.ToString($"HH-mm-ss.fff")}.mp4");
                    File.Move(process.fileName, finalName);
                    _logger.LogInformation($"Запись переименована в {finalName}. Переименование заняло {(start - DateTime.Now).TotalMilliseconds}  | {rtspUrl} | {DateTime.Now}");

                    ArchiveHelper.StopSendingHeartbeat(Path.Combine(_videosFolder, DateTime.Now.ToString("dd-MM-yyyy")));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
            }
        }

        private void SkipSeconds(VideoCapture videoCapture, int seconds)
        {
            for (int i = 0; i < seconds * videoCapture.Fps; i++)
            {
                using var mat = videoCapture.RetrieveMat();
            }

        }

        public void Dispose()
        {
            foreach (var item in _procecces)
            {
                try
                {
                    StopRecording(item.Key).Wait();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при остановке записи для {item.Key}");
                }
            }
        }
    }
}
