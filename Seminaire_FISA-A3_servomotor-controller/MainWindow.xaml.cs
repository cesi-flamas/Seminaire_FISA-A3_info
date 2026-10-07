using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace ServoMotorControl
{
    public partial class MainWindow : Window
    {
        // Doivent rester alignées sur ANGLE_MIN / ANGLE_MAX du sketch Arduino.
        private const int AngleMin = 15;
        private const int AngleMax = 165;

        // Un Arduino Uno redémarre quand le port série est ouvert (impulsion sur
        // DTR par le driver USB). Le bootloader occupe ensuite la carte pendant
        // ~1,5 s : tout ce qui est envoyé pendant ce temps est perdu. On bloque
        // donc les envois jusqu'à la fin de ce délai.
        private static readonly TimeSpan BootloaderDelay = TimeSpan.FromSeconds(2);

        // Le slider déclenche ValueChanged à chaque pixel de déplacement, soit
        // des centaines d'événements par seconde. À 9600 bauds la liaison ne
        // suit pas et le servo accumule du retard. On ne transmet donc que la
        // dernière position connue, à cadence fixe.
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(50);

        private const int StatusLineCount = 6;

        private SerialPort? _serialPort;
        private readonly DispatcherTimer _sendTimer;
        private readonly StringBuilder _receiveBuffer = new();
        private readonly LinkedList<string> _statusLines = new();

        private int _pendingAngle;   // dernière position demandée par l'utilisateur
        private int _lastSentAngle = -1;
        private bool _readyToSend;   // false tant que le bootloader n'a pas rendu la main

        public MainWindow()
        {
            InitializeComponent();

            _pendingAngle = (int)Math.Round(angleSlider.Value);

            _sendTimer = new DispatcherTimer { Interval = SendInterval };
            _sendTimer.Tick += SendTimer_Tick;

            LoadComPorts();
        }

        private void LoadComPorts()
        {
            string? previous = comPortComboBox.SelectedItem?.ToString();
            string[] ports = SerialPort.GetPortNames();

            comPortComboBox.ItemsSource = ports;

            if (ports.Length == 0)
            {
                AppendStatus("Aucun port COM détecté. Branchez la carte puis cliquez sur « Rafraîchir ».");
                return;
            }

            // On conserve la sélection précédente si le port est toujours là.
            int index = previous is null ? -1 : Array.IndexOf(ports, previous);
            comPortComboBox.SelectedIndex = index >= 0 ? index : 0;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadComPorts();
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                Disconnect();
                AppendStatus("Déconnecté du port série.");
                return;
            }

            string? portName = comPortComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(portName))
            {
                AppendStatus("Veuillez sélectionner un port COM.");
                return;
            }

            var port = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One)
            {
                NewLine = "\n",
                // Sans délai d'écriture, une carte débranchée en cours d'usage
                // peut figer l'interface sur un WriteLine qui n'aboutit jamais.
                WriteTimeout = 500,
                ReadTimeout = 500,
                DtrEnable = false,
                RtsEnable = false
            };

            try
            {
                port.DataReceived += SerialPort_DataReceived;
                port.Open();
            }
            catch (Exception ex)
            {
                port.DataReceived -= SerialPort_DataReceived;
                port.Dispose();
                AppendStatus($"Erreur de connexion : {ex.Message}");
                return;
            }

            _serialPort = port;
            _lastSentAngle = -1;
            _readyToSend = false;
            connectButton.Content = "Déconnecter";
            AppendStatus($"Connecté à {portName}. Initialisation de la carte…");

            // Une fois la carte prête, on lui transmet la position affichée pour
            // que le servo et le slider partent synchronisés.
            var bootTimer = new DispatcherTimer { Interval = BootloaderDelay };
            bootTimer.Tick += (_, _) =>
            {
                bootTimer.Stop();
                if (_serialPort is { IsOpen: true })
                {
                    _readyToSend = true;
                    AppendStatus("Carte prête.");
                }
            };
            bootTimer.Start();

            _sendTimer.Start();
        }

        private void Disconnect()
        {
            _sendTimer.Stop();
            _readyToSend = false;

            SerialPort? port = _serialPort;
            _serialPort = null;
            if (port is null)
            {
                connectButton.Content = "Se connecter";
                return;
            }

            // L'abonnement doit être retiré AVANT Close() : si le gestionnaire
            // DataReceived s'exécute pendant la fermeture, SerialPort.Close()
            // peut se bloquer indéfiniment.
            port.DataReceived -= SerialPort_DataReceived;
            try
            {
                if (port.IsOpen)
                    port.Close();
            }
            catch (Exception ex)
            {
                AppendStatus($"Erreur à la fermeture : {ex.Message}");
            }
            finally
            {
                port.Dispose();
            }

            connectButton.Content = "Se connecter";
        }

        private void AngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Mis à jour en toutes circonstances : auparavant l'affichage était
            // à l'intérieur du test « port ouvert », donc le curseur bougeait
            // sans que rien ne change à l'écran tant qu'on n'était pas connecté.
            _pendingAngle = Math.Clamp((int)Math.Round(e.NewValue), AngleMin, AngleMax);

            if (angleTextBlock != null)
                angleTextBlock.Text = $"Angle actuel: {_pendingAngle}°";
        }

        private void SendTimer_Tick(object? sender, EventArgs e)
        {
            if (!_readyToSend || _serialPort is not { IsOpen: true })
                return;

            if (_pendingAngle == _lastSentAngle)
                return;

            int angle = _pendingAngle;
            try
            {
                _serialPort.WriteLine(angle.ToString());
                _lastSentAngle = angle;
            }
            catch (Exception ex)
            {
                // Câble USB débranché en cours d'usage : l'exception remontait
                // depuis le gestionnaire d'événement du slider et fermait
                // l'application. On coupe proprement la liaison à la place.
                AppendStatus($"Liaison perdue : {ex.Message}");
                Disconnect();
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // Exécuté sur un thread du port série : rien d'autre que la lecture
            // ne doit se faire ici, l'IHM est mise à jour via le Dispatcher.
            string data;
            try
            {
                data = ((SerialPort)sender).ReadExisting();
            }
            catch (Exception)
            {
                return;
            }

            Dispatcher.BeginInvoke(() => ProcessReceived(data));
        }

        // Les réponses de la carte n'étaient jusqu'ici jamais lues : le tampon
        // de réception se remplissait en silence et aucune erreur n'était
        // visible côté IHM.
        private void ProcessReceived(string data)
        {
            _receiveBuffer.Append(data);

            string content = _receiveBuffer.ToString();
            int newline;
            while ((newline = content.IndexOf('\n')) >= 0)
            {
                string line = content[..newline].Trim();
                content = content[(newline + 1)..];

                if (line.Length > 0)
                    AppendStatus($"Carte : {line}");
            }

            _receiveBuffer.Clear();
            _receiveBuffer.Append(content);
        }

        private void AppendStatus(string message)
        {
            _statusLines.AddLast($"{DateTime.Now:HH:mm:ss}  {message}");
            while (_statusLines.Count > StatusLineCount)
                _statusLines.RemoveFirst();

            statusTextBlock.Text = string.Join(Environment.NewLine, _statusLines);
        }

        protected override void OnClosed(EventArgs e)
        {
            _sendTimer.Tick -= SendTimer_Tick;
            Disconnect();
            base.OnClosed(e);
        }
    }
}
