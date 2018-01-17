using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Text;
using System.Drawing;

namespace ScreenLapse {
    public partial class MainWindow : Window {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowRect(IntPtr hWnd, ref Rect rect);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowTextLength(IntPtr hWnd);

        // Capture Modes:
        // 0: Active Monitor
        // 1: Single Monitor
        // 2: Active Program
        // 3: Single Program

        private string outputFolder;
        private string outputName;
        private int outputIndex;
        private float interval = 3f;
        private bool capture = false;
        private double resolutionScale = 1f;

        private System.Drawing.Imaging.ImageFormat format = System.Drawing.Imaging.ImageFormat.Jpeg;
        private string ext = ".jpg";

        private int captureMode;
        private int captureItem;

        private Thread captureThread;

        private List<Process> listedProcesses;
        private List<Screen> listedScreens;

        public MainWindow() {
            listedProcesses = new List<Process>();
            listedScreens = new List<Screen>();

            InitializeComponent();

            FormatComboBox.SelectedIndex = 0;

            outputName = OutputNameTextBox.Text;

            outputFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) + @"\Time Lapse";
            OutputFolderTextBox.Text = outputFolder;

            CalculateCaptureIndex();
        }

        private void OutputFolderButton_Click(object sender, RoutedEventArgs e) {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog() { ShowNewFolderButton = true, SelectedPath = outputFolder }) {
                DialogResult result = dialog.ShowDialog();
                outputFolder = dialog.SelectedPath;
                OutputFolderTextBox.Text = outputFolder;
            }

            CalculateCaptureIndex();
        }

        private void IntervalTextBox_TextChanged(object sender, TextChangedEventArgs e) {
            interval = Convert.ToSingle(IntervalTextBox.Text);
        }

        private void StartButton_Click(object sender, RoutedEventArgs e) {
            capture = !capture;
            StartButton.Content = capture ? "Stop" : "Start";
            StatusLabel.Content = "";

            if (capture) {
                captureThread = new Thread(CaptureLoop);
                captureThread.Start();
                captureMode = ModeComboBox.SelectedIndex;
                captureItem = ModePickerComboBox.SelectedIndex;
            } else {
                captureThread.Join();
            }
            ControlGrid.IsEnabled = !capture;
        }

        private void CalculateCaptureIndex() {
            outputIndex = 0;
            if (!Directory.Exists(outputFolder)) return;
            string name = outputName.ToLower();
            foreach (string file in Directory.GetFiles(outputFolder).Select(f=>Path.GetFileName(f).ToLower())
                .Where((string f) => {
                    string[] split = f.Split('_');
                    return split.Length == 2 && split[0] == name && int.TryParse(split[1].Substring(0, split[1].IndexOf('.')), out var n);
                    } )) {

                string f = file.Split('_')[1];
                outputIndex = Math.Max(outputIndex, int.Parse(f.Substring(0, f.IndexOf('.'))));
            }
        }

        private void CaptureLoop() {
            while (capture) {
                try {
                    Capture();
                } catch (Exception e) {
                    Console.WriteLine(e);
                }
                Thread.Sleep((int)(interval * 1000));
            }
        }

        private void Capture() {
            Rect rect = new Rect();
            switch (captureMode) {
                case 0:
                    Screen currentScreen = Screen.FromPoint(System.Windows.Forms.Cursor.Position);
                    rect = new Rect() {
                        Left = currentScreen.Bounds.Left,
                        Right = currentScreen.Bounds.Right,
                        Top = currentScreen.Bounds.Top,
                        Bottom = currentScreen.Bounds.Bottom
                    };
                    StatusLabel.Dispatcher.Invoke(() => StatusLabel.Content = "Captured " + currentScreen.DeviceName + (currentScreen.Primary ? " (Primary)" : ""));
                    break;
                case 1:
                    rect = new Rect() {
                        Left = listedScreens[captureItem].Bounds.Left,
                        Right = listedScreens[captureItem].Bounds.Right,
                        Top = listedScreens[captureItem].Bounds.Top,
                        Bottom = listedScreens[captureItem].Bounds.Bottom
                    };
                    break;
                case 2:
                    IntPtr currentWindow = GetForegroundWindow();
                    GetWindowRect(currentWindow, ref rect);
                    string strTitle = string.Empty;
                    int intLength = GetWindowTextLength(currentWindow) + 1;
                    StringBuilder stringBuilder = new StringBuilder(intLength);
                    if (GetWindowText(currentWindow, stringBuilder, intLength) > 0)
                        strTitle = stringBuilder.ToString();
                    StatusLabel.Dispatcher.Invoke(() => StatusLabel.Content = "Captured " + strTitle);
                    break;
                case 3:
                    GetWindowRect(listedProcesses[captureItem].MainWindowHandle, ref rect);
                    break;
                default:
                    goto case 0;
            }

            int width = (int)((rect.Right - rect.Left) * resolutionScale + .5f);
            int height = (int)((rect.Bottom - rect.Top) * resolutionScale + .5f);
            using (Bitmap img = new Bitmap(width, height)) {
                using (Bitmap cap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top)) {
                    using (Graphics g = Graphics.FromImage(cap))
                        g.CopyFromScreen(new System.Drawing.Point(rect.Left, rect.Top), System.Drawing.Point.Empty, new System.Drawing.Size(rect.Right - rect.Left, rect.Bottom - rect.Top));

                    using (Graphics g = Graphics.FromImage(img))
                        g.DrawImage(cap, 0, 0, width, height);
                }
                if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);
                string path = outputFolder + @"\" + outputName + "_" + outputIndex + ext;
                img.Save(path, format);
            }
            outputIndex++;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            if (capture) {
                capture = false;
                captureThread.Join();
            }
        }

        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            listedProcesses.Clear();
            listedScreens.Clear();
            ModePickerComboBox.Items.Clear();

            if (ModeComboBox.SelectedIndex == 1) {
                ModePickerLabel.Content = "Screen:";
                foreach (Screen screen in Screen.AllScreens.OrderBy(s => s.Bounds.X)) {
                    ModePickerComboBox.Items.Add(new ComboBoxItem() {
                        Content = screen.DeviceName + (screen.Primary ? " (Primary)" : "")
                    });
                    listedScreens.Add(screen);
                }
            } else if (ModeComboBox.SelectedIndex == 3) {
                ModePickerLabel.Content = "Window:";
                foreach (Process process in Process.GetProcesses()
                    .Where(p => p.MainWindowHandle != IntPtr.Zero && !String.IsNullOrEmpty(p.MainWindowTitle))) {

                    ModePickerComboBox.Items.Add(new ComboBoxItem() {
                        Content = process.MainWindowTitle
                    });
                    listedProcesses.Add(process);
                }
            }
        }

        private void OutputNameTextBox_TextChanged(object sender, TextChangedEventArgs e) {
            outputName = OutputNameTextBox.Text;
        }

        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            switch (FormatComboBox.SelectedIndex) {
                case 0:
                    format = System.Drawing.Imaging.ImageFormat.Jpeg;
                    ext = ".jpg";
                    break;
                case 1:
                    format = System.Drawing.Imaging.ImageFormat.Png;
                    ext = ".png";
                    break;
                case 2:
                    format = System.Drawing.Imaging.ImageFormat.Gif;
                    ext = ".gif";
                    break;
                case 3:
                    format = System.Drawing.Imaging.ImageFormat.Bmp;
                    ext = ".bmp";
                    break;
            }
        }
        
        private void ResolutionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (ResolutionSlider != null && ResolutionTextBox != null) {
                ResolutionSlider.Value = (int)(ResolutionSlider.Value + .5);
                ResolutionTextBox.Text = ResolutionSlider.Value + "%";
                resolutionScale = ResolutionSlider.Value / 100.0;
            }
        }

        void OutputFolderTextBoxChanged() {
            try {
                Path.GetFullPath(OutputFolderTextBox.Text);
                if (Path.IsPathRooted(OutputFolderTextBox.Text))
                    outputFolder = OutputFolderTextBox.Text;
                else
                    OutputFolderTextBox.Text = outputFolder;
            } catch {
                OutputFolderTextBox.Text = outputFolder;
            }
        }
        void ResolutionTextBoxChanged() {
            string txt = ResolutionTextBox.Text;
            if (txt.EndsWith("%"))
                txt = txt.Substring(0, txt.Length - 1);
            if (int.TryParse(txt, out int i) && i >= 10 && i <= 100) {
                resolutionScale = i / 100f;
                ResolutionSlider.Value = i;
            }
            ResolutionTextBox.Text = (int)(resolutionScale * 100 + .5f) + "%";
        }

        private void OutputFolderTextBox_LostFocus(object sender, RoutedEventArgs e) {
            OutputFolderTextBoxChanged();
        }
        private void OutputFolderTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
            if (e.Key == System.Windows.Input.Key.Enter)
                OutputFolderTextBoxChanged();
        }

        private void ResolutionTextBox_LostFocus(object sender, RoutedEventArgs e) {
            ResolutionTextBoxChanged();
        }
        private void ResolutionTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) {
            if (e.Key == System.Windows.Input.Key.Enter)
                ResolutionTextBoxChanged();
        }
    }
}
