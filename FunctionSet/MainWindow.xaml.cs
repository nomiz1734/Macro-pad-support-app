using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Windows;           // Cho MessageBox, Window của WPF
using System.Windows.Controls;  // Cho Button, TextBox
using System.Windows.Input;
using System.Windows.Media;     // Cho Brushes, Color của WPF

namespace FunctionSet
{
    // Các Class Model
    public class KeyFunction { public string Name { get; set; } public int Code { get; set; } }
    public class PortInfo { public string PortId { get; set; } public string Description { get; set; } }

    public class CustomAction
    {
        public int Code { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Data { get; set; }
    }

    public partial class MainWindow : Window
    {
        private SerialPort _serialPort;
        private int _currentMode = 1;
        private int _selectedKey = 0;
        private Dictionary<int, Dictionary<int, int>> _keyMappings = new Dictionary<int, Dictionary<int, int>>();

        private List<CustomAction> _customActions = new List<CustomAction>();
        private string _customFileDb = "custom_macros.txt";

        // Cửa sổ thông báo tùy chỉnh
        private ToastWindow _toast;
        private DiscordHelper _discordHelper;

        public MainWindow()
        {
            InitializeComponent();

            // Khởi tạo Toast Window
            _toast = new ToastWindow();
            _discordHelper = new DiscordHelper();
            LoadPorts();
            LoadCustomActions();
            LoadFunctions();
        }

        // ============================================================
        // 1. XỬ LÝ THÔNG BÁO (TOAST NOTIFICATION)
        // ============================================================
        private void ShowNotification(string message)
        {
            // Đảm bảo chạy trên luồng giao diện chính
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _toast.ShowNotification(message);
            });
        }

        // Khi tắt App chính thì tắt luôn Toast
        protected override void OnClosed(EventArgs e)
        {
            if (_toast != null) _toast.Close();
            base.OnClosed(e);
        }

        // ============================================================
        // 2. QUẢN LÝ DỮ LIỆU (LOAD/SAVE FILE)
        // ============================================================
        private void LoadCustomActions()
        {
            try
            {
                if (File.Exists(_customFileDb))
                {
                    _customActions.Clear();
                    var lines = File.ReadAllLines(_customFileDb);
                    foreach (var line in lines)
                    {
                        var p = line.Split('|');
                        if (p.Length == 4)
                        {
                            _customActions.Add(new CustomAction { Code = int.Parse(p[0]), Name = p[1], Type = p[2], Data = p[3] });
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveCustomActions()
        {
            try
            {
                var lines = _customActions.Select(x => $"{x.Code}|{x.Name}|{x.Type}|{x.Data}");
                File.WriteAllLines(_customFileDb, lines);
            }
            catch { }
        }

        // ============================================================
        // 3. THỰC THI LỆNH TỪ ARDUINO (EXECUTE)
        // ============================================================
        private void ExecuteCustomAction(int code)
        {
            var action = _customActions.FirstOrDefault(x => x.Code == code);
            if (action == null) return;

            try
            {
                if (action.Type == "APP")
                {
                    Process.Start(new ProcessStartInfo(action.Data) { UseShellExecute = true });
                }
                else if (action.Type == "KEY")
                {
                    // Dùng SendKeys của WinForms
                    System.Windows.Forms.SendKeys.SendWait(action.Data);
                }
                else if (action.Type == "DISCORD")
                {
                    _discordHelper.LoadClientId();

                    switch (action.Data)
                    {
                        case "MUTE_TOGGLE": _discordHelper.ToggleMute(); break;
                        case "DEAF_TOGGLE": _discordHelper.ToggleDeafen(); break;
                        case "MUTE_ON": _discordHelper.Mute(true); break;
                        case "MUTE_OFF": _discordHelper.Mute(false); break;
                        case "DEAF_ON": _discordHelper.Deafen(true); break;
                        case "DEAF_OFF": _discordHelper.Deafen(false); break;
                    }
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = $"Lỗi thực thi: {ex.Message}";
            }
        }

        // ============================================================
        // 4. CÁC CHỨC NĂNG CRUD (THÊM - SỬA - XÓA)
        // ============================================================

        // --- NÚT THÊM ---
        private void BtnCreateFunc_Click(object sender, RoutedEventArgs e)
        {
            CreateMacroWindow win = new CreateMacroWindow();
            win.Title = "Tạo Chức Năng Mới";
            if (win.ShowDialog() == true)
            {
                int newCode = 241;
                if (_customActions.Count > 0) newCode = _customActions.Max(x => x.Code) + 1;
                if (newCode > 255) { System.Windows.MessageBox.Show("Bộ nhớ đầy (Max 255)!"); return; }

                var action = new CustomAction { Code = newCode, Name = win.ResultName, Type = win.ResultType, Data = win.ResultData };
                _customActions.Add(action);
                SaveCustomActions();
                LoadFunctions();
                cboFunctions.SelectedValue = newCode;
            }
        }

        // --- NÚT SỬA ---
        private void BtnEditFunc_Click(object sender, RoutedEventArgs e)
        {
            if (cboFunctions.SelectedItem == null) return;
            int selectedCode = (int)cboFunctions.SelectedValue;
            var actionToEdit = _customActions.FirstOrDefault(x => x.Code == selectedCode);

            if (actionToEdit == null)
            {
                System.Windows.MessageBox.Show("Chỉ sửa được các chức năng do bạn tự tạo!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            CreateMacroWindow win = new CreateMacroWindow();
            win.Title = "Sửa Chức Năng";
            // Nạp dữ liệu cũ vào form
            win.SetData(actionToEdit.Name, actionToEdit.Type, actionToEdit.Data);

            if (win.ShowDialog() == true)
            {
                actionToEdit.Name = win.ResultName;
                actionToEdit.Type = win.ResultType;
                actionToEdit.Data = win.ResultData;
                SaveCustomActions();
                LoadFunctions();
                cboFunctions.SelectedValue = selectedCode;
                System.Windows.MessageBox.Show("Đã cập nhật thành công!");
            }
        }

        // --- NÚT XÓA ---
        private void BtnDeleteFunc_Click(object sender, RoutedEventArgs e)
        {
            if (cboFunctions.SelectedItem == null) return;
            int selectedCode = (int)cboFunctions.SelectedValue;
            var actionToDelete = _customActions.FirstOrDefault(x => x.Code == selectedCode);

            if (actionToDelete == null)
            {
                System.Windows.MessageBox.Show("Chỉ xóa được các chức năng do bạn tự tạo!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (System.Windows.MessageBox.Show($"Bạn có chắc muốn xóa: '{actionToDelete.Name}'?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _customActions.Remove(actionToDelete);
                SaveCustomActions();
                LoadFunctions();
                cboFunctions.SelectedIndex = 0;
            }
        }

        // ============================================================
        // 5. HỆ THỐNG & KẾT NỐI
        // ============================================================
        private void LoadPorts()
        {
            var portList = new List<PortInfo>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption like '%(COM%'"))
                {
                    var portNames = SerialPort.GetPortNames();
                    var ports = searcher.Get().Cast<ManagementBaseObject>().ToList();
                    foreach (var p in ports)
                    {
                        string caption = p["Caption"]?.ToString();
                        if (!string.IsNullOrEmpty(caption))
                        {
                            foreach (string portName in portNames)
                            {
                                if (caption.Contains($"({portName})")) portList.Add(new PortInfo { PortId = portName, Description = caption });
                            }
                        }
                    }
                }
            }
            catch { }

            var existingIds = portList.Select(p => p.PortId).ToList();
            foreach (string port in SerialPort.GetPortNames())
            {
                if (!existingIds.Contains(port)) portList.Add(new PortInfo { PortId = port, Description = port });
            }

            cboPorts.ItemsSource = portList;
            cboPorts.DisplayMemberPath = "Description";
            cboPorts.SelectedValuePath = "PortId";
            if (portList.Count > 0) cboPorts.SelectedIndex = 0;
        }

        private void LoadFunctions()
        {
            var functions = new List<KeyFunction> {
                new KeyFunction { Name = "--- Không làm gì ---", Code = 0 },
                new KeyFunction { Name = "Phím: Enter", Code = 200 },
                new KeyFunction { Name = "Phím: Esc", Code = 201 },
                new KeyFunction { Name = "Phím: Backspace", Code = 202 },
                new KeyFunction { Name = "Phím: Tab", Code = 203 },
                new KeyFunction { Name = "Phím: Home", Code = 204 },
                new KeyFunction { Name = "Phím: End", Code = 205 },
                new KeyFunction { Name = "Phím: Page Up", Code = 206 },
                new KeyFunction { Name = "Phím: Page Down", Code = 207 },
                new KeyFunction { Name = "Phím: Mũi tên Lên", Code = 208 },
                new KeyFunction { Name = "Phím: Mũi tên Xuống", Code = 209 },
                new KeyFunction { Name = "Phím: Mũi tên Trái", Code = 210 },
                new KeyFunction { Name = "Phím: Mũi tên Phải", Code = 211 },
                new KeyFunction { Name = "MACRO: Copy (Ctrl+C)", Code = 230 },
                new KeyFunction { Name = "MACRO: Paste (Ctrl+V)", Code = 231 },
                new KeyFunction { Name = "MACRO: Undo (Ctrl+Z)", Code = 232 },
                new KeyFunction { Name = "MACRO: Redo (Ctrl+Y)", Code = 233 },
                new KeyFunction { Name = "MACRO: Show Desktop (Win+D)", Code = 234 },
                new KeyFunction { Name = "MEDIA: Play/Pause", Code = 235 },
                new KeyFunction { Name = "MEDIA: Next Song", Code = 236 },
                new KeyFunction { Name = "MEDIA: Prev Song", Code = 237 },
                new KeyFunction { Name = "MEDIA: Volume Up", Code = 238 },
                new KeyFunction { Name = "MEDIA: Volume Down", Code = 239 },
                new KeyFunction { Name = "MEDIA: Mute", Code = 240 }
            };

            // Nạp Custom Actions vào Dropdown
            foreach (var act in _customActions)
            {
                string icon = "❓";
                if (act.Type == "APP") icon = "🚀";
                else if (act.Type == "KEY") icon = "⌨";
                else if (act.Type == "DISCORD") icon = "🎮";
                functions.Add(new KeyFunction { Name = $"{icon} {act.Name}", Code = act.Code });
            }

            for (char c = 'A'; c <= 'Z'; c++) functions.Add(new KeyFunction { Name = "Phím: " + c, Code = (int)c + 32 });
            for (char c = '0'; c <= '9'; c++) functions.Add(new KeyFunction { Name = "Phím: " + c, Code = (int)c });

            cboFunctions.ItemsSource = functions;
            cboFunctions.DisplayMemberPath = "Name";
            cboFunctions.SelectedValuePath = "Code";
            cboFunctions.SelectedIndex = 0;
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_serialPort == null)
                {
                    if (cboPorts.SelectedValue == null) { System.Windows.MessageBox.Show("Vui lòng chọn cổng COM!"); return; }

                    string portName = cboPorts.SelectedValue.ToString();
                    _serialPort = new SerialPort(portName, 9600);
                    _serialPort.DtrEnable = true;
                    _serialPort.DataReceived += SerialPort_DataReceived;
                    _serialPort.Open();

                    btnConnect.Content = "NGẮT KẾT NỐI";
                    btnConnect.Background = System.Windows.Media.Brushes.Crimson;
                    lblStatus.Text = "Đã kết nối " + _serialPort.PortName;
                    lblStatus.Foreground = System.Windows.Media.Brushes.Green;

                    // Bật các nút
                    btnApply.IsEnabled = true;
                    btnFactoryReset.IsEnabled = true;

                    lblStatus.Text += " (Đang đồng bộ...)";
                    _keyMappings.Clear();
                    for (int i = 1; i <= 12; i++) _keyMappings[i] = new Dictionary<int, int>();

                    await System.Threading.Tasks.Task.Delay(2000);
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.WriteLine("GET_MODE");
                        await System.Threading.Tasks.Task.Delay(100);
                        _serialPort.WriteLine("GET_CONFIG");
                    }
                }
                else
                {
                    _serialPort.Close(); _serialPort = null;
                    btnConnect.Content = "KẾT NỐI";
                    btnConnect.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204));
                    lblStatus.Text = "Đã ngắt kết nối";
                    lblStatus.Foreground = System.Windows.Media.Brushes.Red;

                    // Tắt các nút
                    btnApply.IsEnabled = false;
                    btnFactoryReset.IsEnabled = false;

                    lblCurrentMode.Text = "Mode: --";
                }
            }
            catch (Exception ex) { System.Windows.MessageBox.Show("Lỗi: " + ex.Message); }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                while (_serialPort.BytesToRead > 0)
                {
                    string data = _serialPort.ReadLine().Trim();
                    Dispatcher.Invoke(() => {
                        if (data.StartsWith("EXEC:"))
                        {
                            int code = int.Parse(data.Split(':')[1]);
                            ExecuteCustomAction(code);
                        }
                        else if (data.StartsWith("MODE_CHANGED:"))
                        {
                            int newMode = int.Parse(data.Split(':')[1].Trim());
                            if (newMode != _currentMode) ShowNotification($"Đã chuyển sang Mode {newMode}");
                            _currentMode = newMode;
                            lblCurrentMode.Text = "Mode hiện tại: " + _currentMode;
                            if (_selectedKey != 0) UpdateDropdownSelection();
                        }
                        else if (data.StartsWith("DATA:"))
                        {
                            string[] parts = data.Split(':');
                            if (parts.Length == 4)
                            {
                                int m = int.Parse(parts[1]);
                                int k = int.Parse(parts[2]);
                                int c = int.Parse(parts[3]);
                                if (!_keyMappings.ContainsKey(m)) _keyMappings[m] = new Dictionary<int, int>();
                                _keyMappings[m][k] = c;
                            }
                        }
                        else if (data == "CONFIG_LOADED_DONE") lblStatus.Text = "Đã đồng bộ xong!";
                        else if (data.Contains("OK:SAVED")) System.Windows.MessageBox.Show("Đã lưu thành công!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
            }
            catch { }
        }

        private void BtnKey_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button clickedBtn = sender as System.Windows.Controls.Button;
            _selectedKey = int.Parse(clickedBtn.Tag.ToString());
            txtSelectedKey.Text = "Phím số " + _selectedKey;
            txtSelectedKey.Background = System.Windows.Media.Brushes.Yellow;
            UpdateDropdownSelection();
        }

        private void UpdateDropdownSelection()
        {
            if (_keyMappings.ContainsKey(_currentMode) && _keyMappings[_currentMode].ContainsKey(_selectedKey))
                cboFunctions.SelectedValue = _keyMappings[_currentMode][_selectedKey];
            else cboFunctions.SelectedIndex = 0;
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                if (_selectedKey == 0) { System.Windows.MessageBox.Show("Chưa chọn phím!"); return; }
                int code = (int)cboFunctions.SelectedValue;
                _serialPort.Write($"SET:{_currentMode}:{_selectedKey}:{code}\n");
                if (!_keyMappings.ContainsKey(_currentMode)) _keyMappings[_currentMode] = new Dictionary<int, int>();
                _keyMappings[_currentMode][_selectedKey] = code;
            }
        }

        // --- PHẦN MỚI THÊM ---

        // 1. Hàm này giúp bạn kéo di chuyển cửa sổ khi giữ chuột vào thanh tiêu đề
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        // 2. Hàm này xử lý khi bấm nút X để tắt ứng dụng và ngắt kết nối cổng COM an toàn
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }
            this.Close();
        }

        private void BtnFactoryReset_Click(object sender, RoutedEventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                if (System.Windows.MessageBox.Show("Bạn có chắc muốn xóa hết cài đặt và về mặc định?", "Cảnh báo", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    _serialPort.WriteLine("RESET_DEFAULT");
                    _keyMappings.Clear(); _customActions.Clear();
                    if (File.Exists(_customFileDb)) File.Delete(_customFileDb);

                    System.Windows.MessageBox.Show("Đã reset toàn bộ!");

                    // Ngắt kết nối an toàn
                    _serialPort.Close(); _serialPort = null;
                    btnConnect.Content = "KẾT NỐI";
                    btnConnect.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204));
                    lblStatus.Text = "Đã ngắt kết nối";
                    lblStatus.Foreground = System.Windows.Media.Brushes.Red;
                    btnApply.IsEnabled = false; btnFactoryReset.IsEnabled = false;
                }
            }
        }
    }
}