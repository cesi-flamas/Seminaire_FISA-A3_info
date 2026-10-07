using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ServoMotorControl
{
    /// <summary>
    /// Une voie de commande : un servomoteur, donc une seringue de la grue.
    /// L'IHM construit un curseur par voie à partir de cette classe, ce qui
    /// évite d'avoir à retoucher le XAML quand le nombre de vérins change.
    /// </summary>
    public class ServoChannel : INotifyPropertyChanged
    {
        // Doivent rester alignées sur ANGLE_MIN / ANGLE_MAX du sketch Arduino.
        public const int AngleMin = 15;
        public const int AngleMax = 165;
        public const int AngleInit = 90;

        private double _angle = AngleInit;

        public ServoChannel(int number, int pin)
        {
            Number = number;
            Pin = pin;
        }

        /// <summary>Numéro de voie tel qu'attendu par la carte, à partir de 1.</summary>
        public int Number { get; }

        /// <summary>Broche de la carte, pour information à l'écran.</summary>
        public int Pin { get; }

        public string Label => $"Servomoteur {Number} (broche {Pin})";

        // Exposées en propriétés d'instance : le XAML ne sait pas se lier
        // directement à des constantes.
        public int Minimum => AngleMin;
        public int Maximum => AngleMax;

        /// <summary>
        /// Dernier angle réellement transmis à la carte. -1 tant que rien n'a
        /// été envoyé sur cette voie, ce qui force un premier envoi à la
        /// connexion et synchronise le servomoteur avec le curseur affiché.
        /// </summary>
        public int LastSentAngle { get; set; } = -1;

        /// <summary>Position du curseur, liée au slider.</summary>
        public double Angle
        {
            get => _angle;
            set
            {
                if (Math.Abs(_angle - value) < double.Epsilon)
                    return;

                _angle = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TargetAngle));
                OnPropertyChanged(nameof(AngleText));
            }
        }

        /// <summary>Consigne entière effectivement envoyée à la carte.</summary>
        public int TargetAngle => Math.Clamp((int)Math.Round(_angle), AngleMin, AngleMax);

        public string AngleText => $"{TargetAngle}°";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
