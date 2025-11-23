using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Text;
using System.Windows; // Dùng cho Window, RoutedEventArgs
using System.Windows.Input;
using System.Windows.Navigation;

namespace FunctionSet
{
    public partial class DiscordConfigWindow : Window
    {
        //private string _configFile = "discord_id.txt";
        private string _idFile = "discord_id.txt";
        private string _secretFile = "discord_secret.txt";
        private string _tokenFile = "discord_token.txt";

        public DiscordConfigWindow()
        {
            InitializeComponent();
            LoadCurrentId();
        }

        private void LoadCurrentId()
        {
            //if (File.Exists(_configFile))
            //{
            //    txtClientId.Text = File.ReadAllText(_configFile).Trim();                
            //}
            if (File.Exists(_idFile)) txtClientId.Text = File.ReadAllText(_idFile).Trim();
            if (File.Exists(_secretFile)) txtClientSecret.Password = File.ReadAllText(_secretFile).Trim();
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText("http://127.0.0.1");
                // Gọi đích danh MessageBox WPF để tránh lỗi
                System.Windows.MessageBox.Show("Đã copy 'http://127.0.0.1' vào bộ nhớ tạm!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show("Lỗi copy: " + ex.Message);
            }
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        // Sự kiện bấm vào link -> Mở trình duyệt
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string id = txtClientId.Text.Trim();
            string secret = txtClientSecret.Password.Trim();

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret))
            {
                System.Windows.MessageBox.Show("Vui lòng nhập đủ Client ID và Client Secret!", "Thiếu thông tin");
                return;
            }

            // Lưu cấu hình
            File.WriteAllText(_idFile, id);
            File.WriteAllText(_secretFile, secret);

            try
            {
                // 1. Kết nối IPC để lấy CODE (Hiện popup Authorize)
                var helper = new DiscordHelper();
                // Gọi hàm lấy Code (sẽ viết thêm ở bước sau)
                string code = await helper.GetAuthorizeCode();

                if (string.IsNullOrEmpty(code))
                {
                    System.Windows.MessageBox.Show("Không lấy được Code xác thực từ Discord IPC.");
                    return;
                }

                // 2. Dùng HTTP để đổi Code lấy Token
                string token = await ExchangeCodeForToken(id, secret, code);

                if (!string.IsNullOrEmpty(token))
                {
                    File.WriteAllText(_tokenFile, token);
                    System.Windows.MessageBox.Show("Đã lấy Token thành công!\nBây giờ bạn có thể dùng chức năng Mute/Deafen.", "Thành công");
                    this.DialogResult = true;
                    this.Close();
                }
                else
                {
                    System.Windows.MessageBox.Show("Lỗi đổi Token! Kiểm tra lại Client Secret.");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Lỗi: " + ex.Message);
            }
        }

        private async System.Threading.Tasks.Task<string> ExchangeCodeForToken(string id, string secret, string code)
        {
            using (var client = new HttpClient())
            {
                var values = new Dictionary<string, string>
                {
                    { "client_id", id },
                    { "client_secret", secret },
                    { "grant_type", "authorization_code" },
                    { "code", code },
                    { "redirect_uri", "http://127.0.0.1" }
                };

                var content = new FormUrlEncodedContent(values);
                var response = await client.PostAsync("https://discord.com/api/oauth2/token", content);
                var responseString = await response.Content.ReadAsStringAsync();

                try
                {
                    var json = JObject.Parse(responseString);
                    return (string)json["access_token"];
                }
                catch { return null; }
            }
        }

        // Thay thế hàm BtnTest_Click cũ bằng hàm này
        private async void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            string id = txtClientId.Text.Trim();

            if (string.IsNullOrEmpty(id))
            {
                System.Windows.MessageBox.Show("Chưa nhập Client ID!"); return;
            }

            // Lưu ID tạm
            File.WriteAllText("discord_id.txt", id);

            try
            {
                using (var pipe = new NamedPipeClientStream(".", "discord-ipc-0", PipeDirection.InOut))
                {
                    // 1. Thử kết nối vật lý
                    await pipe.ConnectAsync(1000);
                    if (!pipe.IsConnected)
                    {
                        System.Windows.MessageBox.Show("Lỗi: Không tìm thấy ống dẫn 'discord-ipc-0'.\n\n-> Hãy đảm bảo Discord đang bật.\n-> Hãy thử chạy Visual Studio với quyền Admin.", "Kết nối thất bại");
                        return;
                    }

                    // 2. Gửi gói tin Handshake (Xin chào)
                    var handshake = new
                    {
                        v = 1,
                        client_id = id
                    };
                    string jsonSend = Newtonsoft.Json.JsonConvert.SerializeObject(handshake);
                    byte[] body = Encoding.UTF8.GetBytes(jsonSend);
                    byte[] opCode = BitConverter.GetBytes(0); // OpCode 0 = Handshake
                    byte[] len = BitConverter.GetBytes(body.Length);

                    // Ghi vào ống
                    pipe.Write(opCode, 0, 4);
                    pipe.Write(len, 0, 4);
                    pipe.Write(body, 0, body.Length);

                    // 3. QUAN TRỌNG: ĐỌC PHẢN HỒI TỪ DISCORD
                    // Đọc Header (8 byte đầu: 4 byte OpCode + 4 byte Độ dài)
                    byte[] bufferHeader = new byte[8];
                    int bytesRead = await pipe.ReadAsync(bufferHeader, 0, 8);

                    if (bytesRead < 8)
                    {
                        System.Windows.MessageBox.Show("Lỗi: Discord ngắt kết nối ngay lập tức (Không trả về Header).", "Phản hồi rỗng");
                        return;
                    }

                    // Lấy độ dài nội dung phản hồi
                    int responseLen = BitConverter.ToInt32(bufferHeader, 4);

                    // Đọc nội dung (Body)
                    byte[] bufferBody = new byte[responseLen];
                    await pipe.ReadAsync(bufferBody, 0, responseLen);
                    string jsonResponse = Encoding.UTF8.GetString(bufferBody);

                    // 4. HIỂN THỊ KẾT QUẢ "SOI" ĐƯỢC
                    System.Windows.MessageBox.Show($"Discord trả lời:\n\n{jsonResponse}", "Kết quả Debug");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Lỗi ngoại lệ: " + ex.Message);
            }
        }

        // Thêm vào trong class DiscordConfigWindow

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}