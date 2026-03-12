using System.Diagnostics;
using System.Text;
using TobaccoEntities.Models;

namespace TobacoServer.Services
{
    public class StreamingHandlerService
    {
        private static readonly Dictionary<int, Process> _processes = new();
        private static readonly Dictionary<int, string> _keys = new();
        // Для каждой камеры хранится множество userId активных потребителей
        private static readonly Dictionary<int, HashSet<string>> _consumersByUser = new();
        private static readonly object _lock = new();

        private readonly string _playlistsPath =
            Path.Combine(Directory.GetCurrentDirectory(), "Playlists");

        private string GenerateRandomKey()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var sb = new StringBuilder(10);

            for (int i = 0; i < 10; i++)
                sb.Append(chars[random.Next(chars.Length)]);

            return sb.ToString();
        }

        // ==========================
        // HANDSHAKE
        // ==========================
        public string CreateEmptySlot(int id, CurrentStoreHandlingService currentStoreHandlingService)
        {
            lock (_lock)
            {
                var dir = Path.Combine(_playlistsPath, id.ToString());
                var hasExistingProcess = _processes.TryGetValue(id, out var existingProcess);
                var hasExistingKey = _keys.TryGetValue(id, out var existingToken);

                if (hasExistingProcess && hasExistingKey)
                {
                    var existingPlaylistPath = Path.Combine(dir, $"{existingToken}.m3u8");

                    // Если процесс живой и плейлист существует — переиспользуем слот
                    if (!existingProcess.HasExited && System.IO.File.Exists(existingPlaylistPath))
                    {
                        return existingToken;
                    }

                    // Иначе считаем слот "зависшим" и аккуратно его очищаем
                    try
                    {
                        if (!existingProcess.HasExited)
                        {
                            existingProcess.StandardInput.WriteLine("q");
                            existingProcess.StandardInput.Flush();
                            if (!existingProcess.WaitForExit(3000))
                            {
                                existingProcess.Kill(entireProcessTree: true);
                                existingProcess.WaitForExit();
                            }
                        }
                    }
                    catch
                    {
                        if (!existingProcess.HasExited)
                        {
                            existingProcess.Kill(entireProcessTree: true);
                        }
                    }
                    finally
                    {
                        existingProcess.Dispose();
                    }

                    _processes.Remove(id);
                    _keys.Remove(id);
                    _consumersByUser.Remove(id);

                    try
                    {
                        if (Directory.Exists(dir))
                            Directory.Delete(dir, true);
                    }
                    catch
                    {
                        // Игнорируем ошибки удаления, чтобы не блокировать новый слот
                    }
                }

                Directory.CreateDirectory(dir);

                var camera = currentStoreHandlingService
                    .GetStore()
                    .Cameras
                    .FirstOrDefault(x => x.Id == id);

                if (camera is null)
                    throw new Exception("Camera not found");

                // Генерация токена (ключа) сразу
                var token = GenerateRandomKey();

                // Путь плейлиста теперь совпадает с токеном
                var playlistPath = Path.Combine(dir, $"{token}.m3u8");

                // Сегменты HLS: чтобы ffmpeg создавал отдельные ts
                var args =
                    $"-i {camera.Address} " +
                    "-c:v copy -c:a aac -g 25 " +
                    "-hls_time 2 -hls_list_size 100 " +
                    $"-hls_segment_filename \"{dir}/segment_%03d.ts\" " +
                    $"\"{playlistPath}\"";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = new Process { StartInfo = startInfo };
                process.Start();

                // Читаем stderr, чтобы ffmpeg не блокировался
                process.BeginErrorReadLine();

                _processes[id] = process;
                _keys[id] = token;
                _consumersByUser[id] = new HashSet<string>(StringComparer.Ordinal);

                return token;
            }
        }


        // ==========================
        // CLIENT CONNECT
        // ==========================
        public void TakeSlot(int id, string userId)
        {
            lock (_lock)
            {
                if (!_processes.ContainsKey(id))
                    return;

                if (!_consumersByUser.TryGetValue(id, out var users))
                {
                    users = new HashSet<string>(StringComparer.Ordinal);
                    _consumersByUser[id] = users;
                }

                if (!string.IsNullOrWhiteSpace(userId))
                {
                    users.Add(userId);
                }
            }
        }

        // ==========================
        // CLIENT DISCONNECT
        // ==========================
        public void ReleaseSlot(int id, string userId)
        {
            lock (_lock)
            {
                if (!_processes.ContainsKey(id))
                    return;

                if (_consumersByUser.TryGetValue(id, out var users) && !string.IsNullOrWhiteSpace(userId))
                {
                    users.Remove(userId);
                }

                if (users != null && users.Count > 0)
                    return;

                var process = _processes[id];

                try
                {
                    if (!process.HasExited)
                    {
                        // 🔥 корректно завершаем ffmpeg
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
                        process.Kill(entireProcessTree: true);
                }
                finally
                {
                    process.Dispose();
                }

                _processes.Remove(id);
                _keys.Remove(id);
                _consumersByUser.Remove(id);

                CleanupPlaylist(id);
            }
        }

        private void CleanupPlaylist(int id)
        {
            var dir = Path.Combine(_playlistsPath, id.ToString());

            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // если ОС держит хендлы — просто не падаем
            }
        }

        public string GetKey(int id) => _keys[id];
    }
}
