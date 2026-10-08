using System.IO.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Vortice.XInput;

namespace ServoMotorControl
{
    public partial class MainWindow : Window
    {
        private SerialPort? _serialPort;
        private const double GaugeCenterX = 100, GaugeCenterY = 100, GaugeRadius = 90;
        private readonly DispatcherTimer _timer;
        private GamepadButtons _prevButtons;

        public MainWindow()
        {
            InitializeComponent();
            LoadComPorts();
            UpdateGauge();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => Poll();
            _timer.Start();
        }

        private void Poll()
        {
            if (!XInput.GetState(0, out State s)) return;
            var pad = s.Gamepad;
            // ponytail: stock XInput deadzone, raise it if the stick still drifts
            angleSlider.Value += pad.RightThumbX * angleSlider.TickFrequency / 32768.0;
            var newlyPressed = pad.Buttons & ~_prevButtons;
            _prevButtons = pad.Buttons;
            if ((newlyPressed & GamepadButtons.RightShoulder) != 0)
                ToggleConnection();
        }

        private void UpdateGauge()
        {
            double rad = angleSlider.Value * Math.PI / 180;
            var tip = new Point(GaugeCenterX - GaugeRadius * Math.Cos(rad), GaugeCenterY - GaugeRadius * Math.Sin(rad));
            gaugeNeedle.EndPoint = new Point(GaugeCenterX - (GaugeRadius - 8) * Math.Cos(rad),
                GaugeCenterY - (GaugeRadius - 8) * Math.Sin(rad));
            var figure = new PathFigure { StartPoint = new Point(GaugeCenterX - GaugeRadius, GaugeCenterY), IsClosed = false };
            figure.Segments!.Add(new ArcSegment { Point = tip, Size = new Size(GaugeRadius, GaugeRadius),
                SweepDirection = SweepDirection.Clockwise });
            gaugeFill.Data = new PathGeometry { Figures = new PathFigures { figure } };
        }
        private void Gauge_PointerPressed(object? sender, PointerPressedEventArgs e) => SetAngleFromPointer(e);
        private void Gauge_PointerMoved(object? sender, PointerEventArgs e) => SetAngleFromPointer(e);
        private void SetAngleFromPointer(PointerEventArgs e)
        {
            if (!e.GetCurrentPoint(gaugeCanvas).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(gaugeCanvas);
            double deg = Math.Atan2(GaugeCenterY - p.Y, GaugeCenterX - p.X) * 180 / Math.PI;
            deg = Math.Clamp(deg, 0, 180);
            angleSlider.Value = Math.Round(deg / angleSlider.TickFrequency) * angleSlider.TickFrequency;
        }

        private void LoadComPorts()
        {
            comPortComboBox.ItemsSource = SerialPort.GetPortNames();
            if (comPortComboBox.Items.Count > 0)
                comPortComboBox.SelectedIndex = 0;
        }

        private void Window_Arrow_Key(object? sender, KeyEventArgs e)
        {
            angleSlider.Value = e.Key switch
            {
                Key.Right => Math.Min(angleSlider.Maximum, angleSlider.Value + angleSlider.TickFrequency),
                Key.Left => Math.Max(angleSlider.Minimum, angleSlider.Value - angleSlider.TickFrequency),
                _ => angleSlider.Value
            };
            e.Handled = true;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e) => ToggleConnection();
        private void ToggleConnection()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
                connectButton.Content = "Se connecter";
                ShowStatus("Déconnecté du port série.");
            }
            else
            {
                if (comPortComboBox.SelectedItem == null)
                {
                    ShowStatus("Veuillez sélectionner un port COM.");
                    return;
                }

                string portName = "COM6";
                
                if (comPortComboBox.SelectedItem != null)
                {
                     portName = comPortComboBox.SelectedItem.ToString() ?? portName;
                }
                _serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
                try
                {
                    _serialPort.Open();
                    connectButton.Content = "Déconnecter";
                    ShowStatus($"Connecté à {portName}.");
                }
                catch (Exception ex)
                {
                    ShowStatus($"Erreur de connexion: {ex.Message}");
                }
            }
        }

        private void AngleSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            int angle = (int)angleSlider.Value;
            angleTextBlock.Text = $"Angle actuel: {angle}°";
            UpdateGauge();
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.WriteLine(angle.ToString());
            }
        }

        private void ShowStatus(string message) => statusTextBlock.Text = message;

        protected override void OnClosed(EventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
                _serialPort.Close();
            base.OnClosed(e);
        }
    }
}