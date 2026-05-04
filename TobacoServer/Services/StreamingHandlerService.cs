using System.Diagnostics;
using System.Text;

namespace TobacoServer.Services
{
    public class StreamingHandlerService : IDisposable
    {
        private const string MainQuality = "main";
        private const string SecondaryQuality = "secondary";

        private static readonly Dictionary<string, Process> _processes = new();
        private static readonly Dictionary<string, string> _keys = new();
        private static readonly Dictionary<string, HashSet<string>> _consumersByUser = new();
        private static readonly Dictionary<string, DateTime> _lastActivityUtc = new();
        private static readonly object _lock = new();
        private static readonly TimeSpan _streamInactivityTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(5);

        private readonly string _playlistsPath = Path.Combine(Directory.GetCurrentDirectory(), "Playlists");
        private readonly Timer _cleanupTimer;

        public StreamingHandlerService()
        {
            _cleanupTimer = new Timer(_ => CleanupInactiveSlots(), null, _cleanupInterval, _cleanupInterval);
        }

        private string GenerateRandomKey()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var sb = new StringBuilder(10);

            for (int i = 0; i < 10; i++)
            {
                sb.Append(chars[random.Next(chars.Length)]);
            }

            return sb.ToString();
        }

        private static string NormalizeQuality(string? quality)
        {
            return string.Equals(quality, MainQuality, StringComparison.OrdinalIgnoreCase)
                ? MainQuality
                : SecondaryQuality;
        }

        private static string BuildSlotKey(int id, string? quality)
        {
            return $"{id}:{NormalizeQuality(quality)}";
        }

        public string CreateEmptySlot(int id, CurrentStoreHandlingService currentStoreHandlingService, string? quality = null)
        {
            lock (_lock)
            {
                var normalizedQuality = NormalizeQuality(quality);
                var slotKey = BuildSlotKey(id, normalizedQuality);
                var dir = Path.Combine(_playlistsPath, id.ToString(), normalizedQuality);

                if (_processes.TryGetValue(slotKey, out var existingProcess) && _keys.TryGetValue(slotKey, out var existingToken))
                {
                    var existingPlaylistPath = Path.Combine(dir, $"{existingToken}.m3u8");
                    if (!existingProcess.HasExited && File.Exists(existingPlaylistPath))
                    {
                        _lastActivityUtc[slotKey] = DateTime.UtcNow;
                        return existingToken;
                    }

                    StopSlotCore(slotKey);
                }

                Directory.CreateDirectory(dir);

                var camera = currentStoreHandlingService
                    .GetStore()
                    .Cameras
                    .FirstOrDefault(x => x.Id == id);

                if (camera is null)
                {
                    throw new Exception("Camera not found");
                }

                var token = GenerateRandomKey();
                var playlistPath = Path.Combine(dir, $"{token}.m3u8");
                var streamAddress = normalizedQuality == MainQuality || string.IsNullOrWhiteSpace(camera.StreamAddress)
                    ? camera.Address
                    : camera.StreamAddress;

                var segmentPath = Path.Combine(dir, "segment_%05d.ts");
                var args =
                    "-rtsp_transport tcp " +
                    "-fflags +genpts " +
                    $"-i \"{streamAddress}\" " +
                    "-c:v copy -an " +
                    "-hls_time 2 -hls_list_size 12 -hls_delete_threshold 8 " +
                    "-hls_flags delete_segments+temp_file " +
                    $"-hls_segment_filename \"{segmentPath}\" " +
                    $"\"{playlistPath}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                var process = new Process { StartInfo = startInfo };
                process.Start();
                process.BeginErrorReadLine();

                _processes[slotKey] = process;
                _keys[slotKey] = token;
                _consumersByUser[slotKey] = new HashSet<string>(StringComparer.Ordinal);
                _lastActivityUtc[slotKey] = DateTime.UtcNow;

                return token;
            }
        }

        public void TakeSlot(int id, string userId, string? quality = null)
        {
            lock (_lock)
            {
                var slotKey = BuildSlotKey(id, quality);

                if (!_processes.ContainsKey(slotKey))
                {
                    return;
                }

                if (!_consumersByUser.TryGetValue(slotKey, out var users))
                {
                    users = new HashSet<string>(StringComparer.Ordinal);
                    _consumersByUser[slotKey] = users;
                }

                if (!string.IsNullOrWhiteSpace(userId))
                {
                    users.Add(userId);
                }

                _lastActivityUtc[slotKey] = DateTime.UtcNow;
            }
        }

        public void TouchSlotActivity(int id, string? quality = null)
        {
            lock (_lock)
            {
                var slotKey = BuildSlotKey(id, quality);

                if (_processes.ContainsKey(slotKey))
                {
                    _lastActivityUtc[slotKey] = DateTime.UtcNow;
                }
            }
        }

        public void ReleaseSlot(int id, string userId, string? quality = null)
        {
            lock (_lock)
            {
                var slotKey = BuildSlotKey(id, quality);

                if (!_processes.ContainsKey(slotKey))
                {
                    return;
                }

                if (_consumersByUser.TryGetValue(slotKey, out var users) && !string.IsNullOrWhiteSpace(userId))
                {
                    users.Remove(userId);
                    if (users.Count > 0)
                    {
                        _lastActivityUtc[slotKey] = DateTime.UtcNow;
                        return;
                    }
                }

                StopSlotCore(slotKey);
            }
        }

        private void CleanupInactiveSlots()
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var staleSlotKeys = _lastActivityUtc
                    .Where(x => now - x.Value >= _streamInactivityTimeout)
                    .Select(x => x.Key)
                    .ToList();

                foreach (var slotKey in staleSlotKeys)
                {
                    StopSlotCore(slotKey);
                }
            }
        }

        private void StopSlotCore(string slotKey)
        {
            if (!_processes.TryGetValue(slotKey, out var process))
            {
                _keys.Remove(slotKey);
                _consumersByUser.Remove(slotKey);
                _lastActivityUtc.Remove(slotKey);
                CleanupPlaylist(slotKey);
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.WriteLine("q");
                    process.StandardInput.Flush();

                    if (!process.WaitForExit(3000))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                    }
                }
            }
            catch
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            finally
            {
                process.Dispose();
            }

            _processes.Remove(slotKey);
            _keys.Remove(slotKey);
            _consumersByUser.Remove(slotKey);
            _lastActivityUtc.Remove(slotKey);

            CleanupPlaylist(slotKey);
        }

        private void CleanupPlaylist(string slotKey)
        {
            var parts = slotKey.Split(':', 2);
            var id = parts[0];
            var quality = parts.Length > 1 ? parts[1] : SecondaryQuality;
            var dir = Path.Combine(_playlistsPath, id, quality);

            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch
            {
                // Ignore cleanup failures for files still being released by the OS.
            }
        }

        public string GetKey(int id, string? quality = null) => _keys[BuildSlotKey(id, quality)];

        public void Dispose()
        {
            _cleanupTimer.Dispose();
        }
    }
}
