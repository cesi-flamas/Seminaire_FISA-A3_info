using System.IO.Ports;
using System.Windows;

namespace ServoMotorControl
{
    public partial class MainWindow : Window
    {
        public SerialPort _serialPort;

        public MainWindow()
        {
            InitializeComponent();
            LoadComPorts();
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
                MessageBox.Show("Déconnecté du port série.");
            }
            else
            {
                if (comPortComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Veuillez sélectionner un port COM.");
                    return;
                }

                string portName = "COM6";
                
                if (comPortComboBox.SelectedItem != null)
                {
                     portName = comPortComboBox.SelectedItem.ToString();
                }
                _serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
                try
                {
                    _serialPort.Open();
                    connectButton.Content = "Déconnecter";
                    MessageBox.Show($"Connecté à {portName}.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur de connexion: {ex.Message}");
                }
            }
        }

        private void AngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                int angle = (int)angleSlider.Value;
                angleTextBlock.Text = $"Angle actuel: {angle}°";
                _serialPort.WriteLine(angle.ToString());
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
                _serialPort.Close();
            base.OnClosed(e);
        }
    }
}