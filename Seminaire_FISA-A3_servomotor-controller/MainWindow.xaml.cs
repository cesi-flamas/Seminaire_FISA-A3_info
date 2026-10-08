using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ServoMotorControl
{
    public partial class MainWindow : Window
    {
        // Mouvements de la grue. L'ordre de cette méthode fixe le numéro envoyé
        // à la carte — premier bloc = mouvement 1 — et doit correspondre à
        // MOVEMENT_PINS dans le sketch Arduino.
        //
        // Le montage actuel ne comporte qu'un mouvement, le levage, entraîné
        // par DEUX servomoteurs sur les broches 9 et 10. L'orientation de la
        // flèche s'ajoutera ici en second bloc, avec ses propres broches, en
        // portant MOVEMENT_COUNT à 2 dans le sketch.
        //
        // L'IHM n'émet qu'une consigne par mouvement : c'est la carte qui en
        // déduit l'angle miroir du second servomoteur, afin que les deux
        // moitiés d'une paire bougent dans la même instruction plutôt qu'à
        // 50 ms d'intervalle.
        //
        // Les positions remarquables sont nommées en langage métier : c'est
        // ce que lit le technicien, l'angle n'est qu'une précision.
        private static IEnumerable<ServoChannel> BuildChannels()
        {
            yield return new ServoChannel(
                number: 1,
                pins: [9, 10],
                mirrored: true,
                role: "Levage",
                movement: "Montée et descente de la charge, du bas vers le haut",
                positions:
                [
                    new ServoPosition(15,  "Entièrement descendu", "Bas complet"),
                    new ServoPosition(52,  "Charge basse",         "Bas"),
                    new ServoPosition(90,  "Charge à mi-hauteur",  "Mi-hauteur"),
                    new ServoPosition(128, "Charge haute",         "Haut"),
                    new ServoPosition(165, "Entièrement monté",    "Haut complet"),
                ]);
        }

        // Un Arduino Uno redémarre à l'ouverture du port lorsque la ligne DTR
        // est activée ; le bootloader occupe alors la carte environ 1,5 s et
        // tout ce qui est envoyé pendant ce temps est perdu. DtrEnable reste
        // donc à false, ce qui suffit à éviter le redémarrage sur une Uno
        // officielle (vérifié sur carte). Cette marge est conservée pour les
        // deux cas qu'elle ne couvre pas : les adaptateurs USB-série de cartes
        // clones (CH340) qui redémarrent la carte quoi qu'il arrive, et une
        // carte encore en cours de démarrage au moment de la connexion.
        private static readonly TimeSpan BootloaderDelay = TimeSpan.FromSeconds(2);

        // Les curseurs déclenchent un changement à chaque pixel de déplacement,
        // soit des centaines d'envois par seconde. À 9600 bauds la liaison ne
        // suit pas et les servomoteurs accumulent du retard. On n'émet donc
        // que la dernière position connue, à cadence fixe.
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(50);

        private const int StatusLineCount = 10;

        private static readonly Brush LedOffline = new SolidColorBrush(Color.FromRgb(0x98, 0xA2, 0xB3));
        private static readonly Brush LedBusy = new SolidColorBrush(Color.FromRgb(0xF7, 0x90, 0x09));
        private static readonly Brush LedOnline = new SolidColorBrush(Color.FromRgb(0x12, 0xB7, 0x6A));
        private static readonly Brush LedFault = new SolidColorBrush(Color.FromRgb(0xF0, 0x44, 0x38));

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

            foreach (ServoChannel channel in BuildChannels())
                Channels.Add(channel);

            channelsItemsControl.ItemsSource = Channels;

            _sendTimer = new DispatcherTimer { Interval = SendInterval };
            _sendTimer.Tick += SendTimer_Tick;

            SetState(LedOffline, "Hors ligne");
            LoadComPorts();
        }

        private void SetState(Brush colour, string text)
        {
            stateLed.Fill = colour;
            stateTextBlock.Text = text;
        }

        private void LoadComPorts()
        {
            string? previous = comPortComboBox.SelectedItem?.ToString();
            string[] ports = SerialPort.GetPortNames();

            comPortComboBox.ItemsSource = ports;

            if (ports.Length == 0)
            {
                AppendStatus("Aucune carte détectée. Branchez l'Arduino puis cliquez sur « Rechercher ».");
                return;
            }

            // On conserve la sélection précédente si le port est toujours là.
            int index = previous is null ? -1 : Array.IndexOf(ports, previous);
            comPortComboBox.SelectedIndex = index >= 0 ? index : 0;
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadComPorts();
            AppendStatus($"Recherche des cartes : {comPortComboBox.Items.Count} port(s) trouvé(s).");
        }

        /// <summary>Rappel d'une position nommée depuis son bouton.</summary>
        private void PresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: ServoPosition position } && position.Owner is { } channel)
            {
                channel.Angle = position.Angle;
                AppendStatus($"Commande : {channel.Role} → {position.Name}.");
            }
        }

        /// <summary>Ramène tous les mouvements à mi-course.</summary>
        private void NeutralButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (ServoChannel channel in Channels)
                channel.Angle = ServoChannel.AngleInit;

            AppendStatus("Commande : tous les mouvements au point neutre.");
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                Disconnect();
                AppendStatus("Liaison fermée.");
                return;
            }

            string? portName = comPortComboBox.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(portName))
            {
                AppendStatus("Sélectionnez d'abord le port de la carte.");
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
                SetState(LedFault, "Échec");
                AppendStatus($"Connexion impossible sur {portName} : {ex.Message}");
                AppendStatus("Vérifiez que le moniteur série de l'IDE Arduino est bien fermé.");
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
            SetState(LedBusy, "Initialisation…");
            AppendStatus($"Liaison ouverte sur {portName}. Démarrage de la carte en cours…");

            var bootTimer = new DispatcherTimer { Interval = BootloaderDelay };
            bootTimer.Tick += (_, _) =>
            {
                bootTimer.Stop();
                if (_serialPort is { IsOpen: true })
                {
                    _readyToSend = true;
                    SetState(LedOnline, "En ligne");
                    AppendStatus("Carte prête, commandes actives.");
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

            connectButton.Content = "Se connecter";
            SetState(LedOffline, "Hors ligne");

            if (port is null)
                return;

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
        }

        private void SendTimer_Tick(object? sender, EventArgs e)
        {
            if (!_readyToSend || _serialPort is not { IsOpen: true } || Channels.Count == 0)
                return;

            // Une seule commande par tick, en tourniquet sur les voies. Émettre
            // toutes les voies à chaque tick saturerait de nouveau la liaison :
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
                    // remontait depuis le gestionnaire du curseur et fermait
                    // l'application. On coupe proprement la liaison à la place.
                    SetState(LedFault, "Liaison perdue");
                    AppendStatus($"Liaison perdue : {ex.Message}");
                    Disconnect();
                    return;
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
                string line = Sanitize(content[..newline]);
                content = content[(newline + 1)..];

                if (line.Length > 0)
                    AppendStatus(Translate(line));
            }

            _receiveBuffer.Clear();
            _receiveBuffer.Append(content);
        }

        /// <summary>
        /// Ne conserve que les caractères imprimables d'une ligne reçue.
        ///
        /// Au redémarrage de la carte — et surtout dans les secondes qui
        /// suivent un téléversement — la liaison livre des octets parasites :
        /// la ligne série n'est pas encore stable et le récepteur interprète du
        /// bruit comme des caractères, y compris des NUL et d'autres caractères
        /// de contrôle. Recopiés tels quels dans le journal, ils polluent le
        /// TextBlock qui l'affiche. On les écarte avant toute interprétation.
        /// </summary>
        private static string Sanitize(string raw)
        {
            var kept = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (!char.IsControl(c))
                    kept.Append(c);
            }

            return kept.ToString().Trim();
        }

        /// <summary>
        /// Traduit une réponse brute de la carte en langage métier. « OK 2 165 »
        /// ne dit rien à un technicien ; « Levage : Entièrement monté » si.
        /// </summary>
        private string Translate(string line)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 3 && parts[0] == "OK"
                && int.TryParse(parts[1], out int number)
                && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int angle))
            {
                ServoChannel? channel = Channels.FirstOrDefault(c => c.Number == number);
                if (channel is not null)
                    return $"{channel.Role} : {channel.NearestPosition(angle).Name} ({angle}°)";

                return $"Voie {number} inconnue de l'interface, positionnée à {angle}°.";
            }

            // « READY <mouvements> », ou « READY <mouvements> <servomoteurs> »
            // depuis que chaque mouvement est entraîné par une paire.
            if (parts.Length is 2 or 3 && parts[0] == "READY" && int.TryParse(parts[1], out int declared))
            {
                int expectedServos = Channels.Sum(c => c.Pins.Length);

                // Un écart ici signifie que le sketch téléversé n'est pas celui
                // qu'attend l'interface : c'est la cause typique d'un mouvement
                // qui ne répond pas, et elle est invisible autrement.
                if (declared != Channels.Count)
                {
                    SetState(LedFault, "Carte incompatible");
                    return $"ATTENTION : la carte déclare {declared} mouvement(s), l'interface en gère {Channels.Count}. "
                         + "Téléversez le sketch correspondant à cette version.";
                }

                if (parts.Length == 3 && int.TryParse(parts[2], out int servos))
                {
                    if (servos != expectedServos)
                    {
                        SetState(LedFault, "Carte incompatible");
                        return $"ATTENTION : la carte pilote {servos} servomoteur(s), l'interface en attend {expectedServos}. "
                             + "Vérifiez MOVEMENT_PINS dans le sketch.";
                    }

                    return $"Carte démarrée : {declared} mouvement(s), {servos} servomoteurs.";
                }

                return $"Carte démarrée, {declared} mouvement(s) disponible(s).";
            }

            if (parts.Length >= 1 && parts[0] == "ERR")
                return $"Commande refusée par la carte : {line[3..].Trim()}";

            return $"Carte : {line}";
        }

        private void AppendStatus(string message)
        {
            _statusLines.AddLast($"{DateTime.Now:HH:mm:ss}  {message}");
            while (_statusLines.Count > StatusLineCount)
                _statusLines.RemoveFirst();

            statusTextBlock.Text = string.Join(Environment.NewLine, _statusLines);
            logScrollViewer.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            _sendTimer.Tick -= SendTimer_Tick;
            Disconnect();
            base.OnClosed(e);
        }
    }
}
