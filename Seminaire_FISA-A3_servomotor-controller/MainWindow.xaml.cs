using System.IO.Ports;
using System.Windows;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;

namespace ServoMotorControl
{
    public partial class MainWindow : Window
    {
        public SerialPort _serialPort;
        string portName = "COM6";
        List<int> angles = new List<int>();
        int angle2;
        bool state;
        bool rightPressed = false;
        bool leftPressed = false;
        bool upPressed = false;
        bool downPressed = false;
        private DispatcherTimer holdTimer;


        public MainWindow()
        {
            InitializeComponent();
            LoadComPorts();

            // Timer qui répète le changement d'angle tant qu'une flèche est maintenue
            holdTimer = new DispatcherTimer();
            holdTimer.Interval = TimeSpan.FromMilliseconds(100); // 1 pas toutes les 100 ms (10°/s)
            holdTimer.Tick += (s, e) => StepAngle();

            // Si la fenêtre perd le focus pendant qu'une flèche est maintenue, le KeyUp n'arrive jamais :
            // on remet tout à zéro pour que le servo ne continue pas de tourner tout seul
            Deactivated += (s, e) =>
            {
                upPressed = downPressed = rightPressed = leftPressed = false;
                holdTimer.Stop();
            };
        }

        private void LoadComPorts()
        {
            comPortComboBox.ItemsSource = SerialPort.GetPortNames();
            if (comPortComboBox.Items.Count == 0) {
                comPortComboBox.ItemsSource = null;
                comPortComboBox.Items.Add("Aucun Port COM");
            }
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
                if (comPortComboBox.SelectedItem == null || (string)comPortComboBox.SelectedValue == "Aucun Port COM")
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

        // Calcule le sens à partir des flèches maintenues et déplace le slider d'1°
        // (+1 avec Haut/Droite, -1 avec Bas/Gauche, 0 si les deux sens sont appuyés)
        private void StepAngle()
        {
            int direction = 0;
            if (upPressed || rightPressed) direction += 1 * (int)angleSlider.TickFrequency;
            if (downPressed || leftPressed) direction -= 1 * (int)angleSlider.TickFrequency;

            // Math.Clamp empêche de sortir des bornes du slider (0 à 120)
            angleSlider.Value = Math.Clamp(angleSlider.Value + direction, angleSlider.Minimum, angleSlider.Maximum);
        }

        // Appui sur une flèche : on mémorise la touche, on fait un pas tout de suite,
        // puis le timer prend le relais tant que la touche reste enfoncée
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:    upPressed = true;    break;
                case Key.Down:  downPressed = true;  break;
                case Key.Right: rightPressed = true; break;
                case Key.Left:  leftPressed = true;  break;
                default: return; // autre touche : on ne fait rien
            }

            // Empêche la ComboBox (changement de port COM) ou le slider de réagir aussi à la flèche
            e.Handled = true;

            // Windows renvoie KeyDown en boucle quand on maintient une touche (IsRepeat = true) :
            // on l'ignore, c'est le timer qui gère la répétition à vitesse régulière
            if (e.IsRepeat) return;

            StepAngle();        // un appui court = 1 pas
            holdTimer.Start();  // maintien = répétition
        }

        // Relâchement d'une flèche : on l'oublie, et on arrête le timer si plus aucune n'est appuyée
        private void MainWindow_KeyUp(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:    upPressed = false;    break;
                case Key.Down:  downPressed = false;  break;
                case Key.Right: rightPressed = false; break;
                case Key.Left:  leftPressed = false;  break;
                default: return;
            }
            e.Handled = true;

            if (!upPressed && !downPressed && !rightPressed && !leftPressed)
                holdTimer.Stop();
        }
    }
}