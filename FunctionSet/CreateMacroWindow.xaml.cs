using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input; // Cần thiết cho các sự kiện chuột

namespace FunctionSet
{
    public partial class CreateMacroWindow : Window
    {
        public string ResultName { get; private set; }
        public string ResultType { get; private set; } // "APP", "KEY", "DISCORD"
        public string ResultData { get; private set; }

        public CreateMacroWindow()
        {
            InitializeComponent();
            LoadKeys();
        }

        private void LoadKeys()
        {
            cboKeys.Items.Clear();
            // Nạp danh sách phím
            for (char c = 'A'; c <= 'Z'; c++) cboKeys.Items.Add(c.ToString());
            for (int i = 0; i <= 9; i++) cboKeys.Items.Add("D" + i);
            for (int i = 1; i <= 12; i++) cboKeys.Items.Add("F" + i);

            string[] specialKeys = { "Enter", "Esc", "Tab", "Space", "Backspace", "Delete", "Up", "Down", "Left", "Right" };
            foreach (var k in specialKeys) cboKeys.Items.Add(k);

            cboKeys.SelectedIndex = 0;
            cboDiscordAction.SelectedIndex = 0;
        }

        // --- HÀM NẠP DỮ LIỆU CŨ KHI SỬA ---
        public void SetData(string name, string type, string data)
        {
            txtName.Text = name;

            if (type == "APP")
            {
                MyTabControl.SelectedIndex = 0;
                txtPath.Text = data;
            }
            else if (type == "KEY")
            {
                MyTabControl.SelectedIndex = 1;
                string keyRaw = data;

                // Tách Modifier
                if (keyRaw.Contains("^")) { chkCtrl.IsChecked = true; keyRaw = keyRaw.Replace("^", ""); }
                if (keyRaw.Contains("+")) { chkShift.IsChecked = true; keyRaw = keyRaw.Replace("+", ""); }
                if (keyRaw.Contains("%")) { chkAlt.IsChecked = true; keyRaw = keyRaw.Replace("%", ""); }

                // Làm sạch chuỗi để khớp với ComboBox
                keyRaw = keyRaw.Replace("{", "").Replace("}", "").ToUpper();

                foreach (var item in cboKeys.Items)
                {
                    if (item.ToString().ToUpper() == keyRaw) { cboKeys.SelectedItem = item; break; }
                }
            }
            else if (type == "DISCORD")
            {
                MyTabControl.SelectedIndex = 2;
                foreach (ComboBoxItem item in cboDiscordAction.Items)
                {
                    if (item.Tag != null && item.Tag.ToString() == data)
                    {
                        cboDiscordAction.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        // --- CÁC SỰ KIỆN CLICK ---

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*";
            if (dlg.ShowDialog() == true)
            {
                txtPath.Text = dlg.FileName;
                if (string.IsNullOrEmpty(txtName.Text))
                    txtName.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }

        // [QUAN TRỌNG] Hàm mở cửa sổ cấu hình Discord
        private void BtnConfigDiscord_Click(object sender, RoutedEventArgs e)
        {
            DiscordConfigWindow configWin = new DiscordConfigWindow();
            configWin.ShowDialog();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                System.Windows.MessageBox.Show("Vui lòng nhập tên chức năng!");
                return;
            }

            int tabIndex = MyTabControl.SelectedIndex;

            // 1. TAB APP
            if (tabIndex == 0)
            {
                if (string.IsNullOrWhiteSpace(txtPath.Text))
                {
                    System.Windows.MessageBox.Show("Chưa chọn file!");
                    return;
                }
                ResultType = "APP";
                ResultData = txtPath.Text;
            }
            // 2. TAB PHÍM TẮT
            else if (tabIndex == 1)
            {
                string modifiers = "";
                if (chkCtrl.IsChecked == true) modifiers += "^";
                if (chkShift.IsChecked == true) modifiers += "+";
                if (chkAlt.IsChecked == true) modifiers += "%";

                if (cboKeys.SelectedItem == null) { System.Windows.MessageBox.Show("Chưa chọn phím!"); return; }

                string key = cboKeys.SelectedItem.ToString();
                if (key.Length > 1) key = "{" + key.ToUpper() + "}";
                else key = key.ToLower();

                ResultData = modifiers + key;
                ResultType = "KEY";
            }
            // 3. TAB DISCORD
            else if (tabIndex == 2)
            {
                if (cboDiscordAction.SelectedItem == null)
                {
                    System.Windows.MessageBox.Show("Vui lòng chọn hành động Discord!");
                    return;
                }

                var item = cboDiscordAction.SelectedItem as ComboBoxItem;
                ResultData = item.Tag.ToString();
                ResultType = "DISCORD";
            }

            ResultName = txtName.Text;
            this.DialogResult = true;
            this.Close();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}