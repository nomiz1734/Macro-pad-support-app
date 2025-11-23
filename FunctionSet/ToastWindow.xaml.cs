using System;
using System.Windows;
using System.Windows.Threading; // Để dùng Timer

namespace FunctionSet
{
    public partial class ToastWindow : Window
    {
        private DispatcherTimer _timer;

        public ToastWindow()
        {
            InitializeComponent();

            // Khởi tạo Timer để tự tắt sau 1.5 giây
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1.5);
            _timer.Tick += Timer_Tick;
        }

        // Hàm hiển thị thông báo mới
        public void ShowNotification(string message)
        {
            // Cập nhật nội dung
            txtMessage.Text = message;

            // Tính toán vị trí (Góc dưới phải màn hình)
            var desktopWorkingArea = SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width - 10;
            this.Top = desktopWorkingArea.Bottom - this.Height - 10;

            // Hiện cửa sổ
            this.Show();

            // Reset timer (Nếu đang đếm dở thì đếm lại từ đầu) -> Đây là mấu chốt giúp cái mới đè cái cũ
            _timer.Stop();
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _timer.Stop();
            this.Hide(); // Ẩn đi chứ không Close hẳn để tái sử dụng
        }
    }
}