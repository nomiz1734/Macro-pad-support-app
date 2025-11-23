using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace FunctionSet
{
    public class DiscordHelper
    {
        private NamedPipeClientStream _pipe;
        private string _clientId = "";
        private string _configFile = "discord_id.txt";
        private string _tokenFile = "discord_token.txt";
        private string _logFile = "discord_debug_log.txt";
        private int _nonce = 0;

        private static bool _localMuteState = false;
        private static bool _localDeafState = false;

        public DiscordHelper() { LoadClientId(); }

        private void Log(string msg) { try { File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { } }
        public void LoadClientId() { if (File.Exists(_configFile)) _clientId = File.ReadAllText(_configFile).Trim(); }

        // --- HÀM LẤY CODE (Dùng cho nút Save Config) ---
        public async Task<string> GetAuthorizeCode()
        {
            LoadClientId();
            // Reset pipe cũ
            if (_pipe != null) { _pipe.Dispose(); _pipe = null; }

            if (!await ConnectPipeOnly()) return "";

            // 1. Handshake
            var handshake = new { v = 1, client_id = _clientId };
            await SendFrameAsync(0, handshake);
            await ReadFrameAsync();

            // 2. Mở cửa sổ Discord (để hiện popup)
            try { Process.Start(new ProcessStartInfo($"discord://") { UseShellExecute = true }); } catch { }
            await Task.Delay(500);

            // 3. Xin Code (response_type = code)
            var authPayload = new
            {
                cmd = "AUTHORIZE",
                args = new { client_id = _clientId, scopes = new[] { "rpc", "rpc.voice.read", "rpc.voice.write" }, response_type = "code" },
                nonce = Guid.NewGuid().ToString()
            };
            await SendFrameAsync(1, authPayload);

            // 4. Đọc phản hồi lấy Code
            string res = await ReadFrameAsync();
            try { return (string)JObject.Parse(res)["data"]["code"]; } catch { return ""; }
        }

        // --- LOGIC ĐIỀU KHIỂN CHÍNH ---
        private async Task SendCommand(string cmdType, object args)
        {
            // Đảm bảo đã kết nối và ĐĂNG NHẬP
            if (!await EnsureAuthenticated()) return;

            try
            {
                var payload = new { cmd = cmdType, args = args, nonce = (++_nonce).ToString() };
                Log($"Gửi lệnh: {cmdType}");
                await SendFrameAsync(1, payload);
                await ReadFrameAsync(); // Đọc kết quả
            }
            catch { if (_pipe != null) { _pipe.Dispose(); _pipe = null; } }
        }

        private async Task<bool> ConnectPipeOnly()
        {
            if (_pipe != null && _pipe.IsConnected) return true;
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var p = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut);
                    await p.ConnectAsync(300);
                    if (p.IsConnected) { _pipe = p; return true; }
                }
                catch { }
            }
            return false;
        }

        private async Task<bool> EnsureAuthenticated()
        {
            if (_pipe != null && _pipe.IsConnected) return true; // Giả sử kết nối còn sống thì vẫn auth ok

            if (!await ConnectPipeOnly()) return false;

            // 1. Handshake
            var handshake = new { v = 1, client_id = _clientId };
            await SendFrameAsync(0, handshake);
            await ReadFrameAsync();

            // 2. Authenticate bằng Token đã lưu
            if (!File.Exists(_tokenFile)) return false;
            string token = File.ReadAllText(_tokenFile).Trim();

            var authPayload = new { cmd = "AUTHENTICATE", args = new { access_token = token }, nonce = Guid.NewGuid().ToString() };
            await SendFrameAsync(1, authPayload);
            string res = await ReadFrameAsync();

            if (res.Contains("ERROR"))
            {
                Log("Token hết hạn hoặc sai!");
                return false;
            }

            Log("Đăng nhập lại thành công!");
            return true;
        }

        // --- IO CORE ---
        private async Task SendFrameAsync(int opCode, object data)
        {
            if (_pipe == null || !_pipe.IsConnected) return;
            string json = JsonConvert.SerializeObject(data);
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] op = BitConverter.GetBytes(opCode);
            byte[] len = BitConverter.GetBytes(body.Length);
            using (MemoryStream ms = new MemoryStream())
            {
                ms.Write(op, 0, 4); ms.Write(len, 0, 4); ms.Write(body, 0, body.Length);
                await _pipe.WriteAsync(ms.ToArray(), 0, (int)ms.Length);
            }
        }

        private async Task<string> ReadFrameAsync()
        {
            if (_pipe == null || !_pipe.IsConnected) return "";
            byte[] h = new byte[8];
            if (await _pipe.ReadAsync(h, 0, 8) < 8) return "";
            int len = BitConverter.ToInt32(h, 4);
            byte[] b = new byte[len];
            await _pipe.ReadAsync(b, 0, len);
            return Encoding.UTF8.GetString(b);
        }

        // Public Helpers
        public async void ToggleMute() { _localMuteState = !_localMuteState; await SendCommand("SET_VOICE_SETTINGS", new { mute = _localMuteState }); }
        public async void ToggleDeafen() { _localDeafState = !_localDeafState; await SendCommand("SET_VOICE_SETTINGS", new { deaf = _localDeafState }); }
        public async void Mute(bool s) { _localMuteState = s; await SendCommand("SET_VOICE_SETTINGS", new { mute = s }); }
        public async void Deafen(bool s) { _localDeafState = s; await SendCommand("SET_VOICE_SETTINGS", new { deaf = s }); }
    }
}