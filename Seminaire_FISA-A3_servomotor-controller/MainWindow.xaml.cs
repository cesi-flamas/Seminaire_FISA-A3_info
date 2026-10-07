using System.IO.Ports;
using System.Windows;
using System.Collections.Generic;
using System.Threading;


namespace ServoMotorControl
{
    public partial class MainWindow : Window
    {
        public SerialPort _serialPort;
        string portName = "COM6";
        List<int> angles = new List<int>();
        int angle2;
        bool state;

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

                if (comPortComboBox.SelectedItem != null)
                {
                    portName = comPortComboBox.SelectedItem.ToString();
                }
                _serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
                try
                {
                    _serialPort.Open();
                    connectButton.Content = "Déconnecter";
                    _serialPort.WriteLine("angle ?"); // Ecrit à la carte arduino pour demander l'angle actuel du servo moteur
                    string test = _serialPort.ReadLine(); // Lit l'angle actuel du servo moteur
                    angleSlider.Value = Convert.ToDouble(test);  // Affiche sur le slider l'angle actuel du servo moteur
                    MessageBox.Show($"Connecté à {portName}");

                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur de connexion: {ex.Message}");
                }
            }
        }

        // Ajout d'une pause entre chaque changement d'angle pour alléger l'effort du servo moteur
        public void FixServoMoteur() 
        {
            if (_serialPort != null && _serialPort.IsOpen) // Vérifie si le port n'est pas null et s'il est ouvert
            {
                while(true) { // Boucle infinie
                if(angles.Count > 0) // Vérifie s'il y a eu un déplacement du Slider
                {
                    angle2 = angles.First(); // Récupére la 1ère valeur récupèrer au cours du déplacement du slider
                    _serialPort.WriteLine(angle2.ToString()); // Envoie l'angle récupèrer
                    Thread.Sleep(15); // Mets en Pause le thread pendant 30ms pour soulager le servo moteur
                    angles.RemoveAt(0); // Enleve la 1ère valeur envoyée
                } else
                {
                    Thread.Sleep(50); // Mets en Pause le Thread pendant 150ms pour éviter de surcharger le système
                }
                state = Thread.CurrentThread.IsAlive; // Récupère l'état du Thread
                }
            }
        }
        

        private void AngleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                int angle = (int)angleSlider.Value;
                angles.Add(angle); // Ajoute la valeur à une liste
                angleTextBlock.Text = $"Angle actuel: {angle}°";
                if (state == false) // Vérifie l'état du thread
                {
                    Thread testcaller = new Thread(new ThreadStart(FixServoMoteur)); // Défini le Thread pour exécuter FixServoMoteur
                    testcaller.IsBackground = true; // Défini le thread pour s'éxécuter en arrière plan
                    testcaller.Start(); // Démarre le thread
                }
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