using System.IO.Ports;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ServoMotorControl
{
    public partial class MainWindow : Window
    {
        private SerialPort? _serialPort;

        public MainWindow()
        {
            InitializeComponent();
            LoadComPorts();
        }

        private void Window_Arrow_Key(object? sender, KeyEventArgs e)
        {
            angleSlider.Value = e.Key switch
            {
                Key.Up => Math.Min(angleSlider.Maximum, angleSlider.Value + angleSlider.TickFrequency),
                Key.Down => Math.Max(angleSlider.Minimum, angleSlider.Value - angleSlider.TickFrequency),
                _ => angleSlider.Value
            };
            e.Handled = true;
        }

        private void LoadComPorts()
        {
            comPortComboBox.ItemsSource = SerialPort.GetPortNames();
            if (comPortComboBox.Items.Count > 0)
                comPortComboBox.SelectedIndex = 0;
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
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