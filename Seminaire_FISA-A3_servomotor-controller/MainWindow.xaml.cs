using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace ServoMotorControl
{
    public partial class MainWindow : Window
    {
        // Affectation des voies de la grue. L'ordre de ce tableau fixe le
        // numéro de voie envoyé à la carte — première ligne = voie 1 — et doit
        // correspondre à SERVO_COUNT / SERVO_PINS dans le sketch Arduino.
        //
        // Si les deux servomoteurs sont câblés dans l'autre sens, il suffit
        // d'échanger ces deux lignes : rien d'autre n'est à toucher, ni ici ni
        // dans le XAML.
        private static readonly (int Pin, string Role, string Movement)[] ServoDefinitions =
        {
            (9,  "Orientation", "pivotement de la flèche, gauche ↔ droite"),
            (10, "Levage",      "montée et descente de la charge, haut ↕ bas"),
        };

        // Un Arduino Uno redémarre à l'ouverture du port lorsque la ligne DTR
        // est activée ; le bootloader occupe alors la carte environ 1,5 s et
        // tout ce qui est envoyé pendant ce temps est perdu. DtrEnable reste
        // donc à false, ce qui suffit à éviter le redémarrage sur une Uno
        // officielle (vérifié sur carte). Cette marge est conservée pour les
        // deux cas qu'elle ne couvre pas : les adaptateurs USB-série de cartes
        // clones (CH340) qui redémarrent la carte quoi qu'il arrive, et une
        // carte encore en cours de démarrage au moment de la connexion.
        private static readonly TimeSpan BootloaderDelay = TimeSpan.FromSeconds(2);

        // Les sliders déclenchent un changement à chaque pixel de déplacement,
        // soit des centaines d'envois par seconde. À 9600 bauds la liaison ne
        // suit pas et les servomoteurs accumulent du retard. On n'émet donc
        // que la dernière position connue, à cadence fixe.
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(50);

        private const int StatusLineCount = 6;

        private SerialPort? _serialPort;
        private readonly DispatcherTimer _sendTimer;
        private readonly StringBuilder _receiveBuffer = new();
        private readonly LinkedList<string> _statusLines = new();

        private bool _readyToSend;   // false tant que le bootloader n'a pas rendu la main
        private int _roundRobin;     // voie examinée en premier au prochain tick

        public ObservableCollection<ServoChannel> Channels { get; } = new();

        public MainWindow()
        {
            InitializeComponent();

            for (int i = 0; i < ServoDefinitions.Length; i++)
            {
                (int pin, string role, string movement) = ServoDefinitions[i];
                Channels.Add(new ServoChannel(i + 1, pin, role, movement));
            }

            channelsItemsControl.ItemsSource = Channels;

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
            _readyToSend = false;
            _roundRobin = 0;

            // Toutes les voies sont marquées comme jamais transmises : chacune
            // sera envoyée une fois la carte prête, ce qui synchronise les
            // servomoteurs avec les positions affichées.
            foreach (ServoChannel channel in Channels)
                channel.LastSentAngle = -1;

            connectButton.Content = "Déconnecter";
            AppendStatus($"Connecté à {portName}. Initialisation de la carte…");

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

        private void SendTimer_Tick(object? sender, EventArgs e)
        {
            if (!_readyToSend || _serialPort is not { IsOpen: true } || Channels.Count == 0)
                return;

            // Une seule commande par tick, en tourniquet sur les voies. Émettre
            // les quatre voies à chaque tick saturerait de nouveau la liaison :
            // commandes et accusés dépasseraient les 960 octets/s disponibles à
            // 9600 bauds. Le tourniquet garantit qu'aucune voie n'est affamée
            // par une autre que l'on remuerait en continu.
            for (int step = 0; step < Channels.Count; step++)
            {
                ServoChannel channel = Channels[(_roundRobin + step) % Channels.Count];

                int angle = channel.TargetAngle;
                if (angle == channel.LastSentAngle)
                    continue;

                try
                {
                    _serialPort.WriteLine($"S{channel.Number}:{angle}");
                    channel.LastSentAngle = angle;
                }
                catch (Exception ex)
                {
                    // Câble USB débranché en cours d'usage : l'exception
                    // remontait depuis le gestionnaire du slider et fermait
                    // l'application. On coupe proprement la liaison à la place.
                    AppendStatus($"Liaison perdue : {ex.Message}");
                    Disconnect();
                }

                // On reprendra à la voie suivante au prochain tick.
                _roundRobin = (_roundRobin + step + 1) % Channels.Count;
                return;
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
