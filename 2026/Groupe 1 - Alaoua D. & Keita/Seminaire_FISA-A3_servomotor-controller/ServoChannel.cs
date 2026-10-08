using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ServoMotorControl
{
    /// <summary>
    /// Une position remarquable d'un mouvement, désignée par son nom métier
    /// plutôt que par son angle. C'est ce que lit et ce que commande le
    /// technicien : « Entièrement monté », pas « 165° ».
    /// </summary>
    public class ServoPosition
    {
        public ServoPosition(int angle, string name, string shortName)
        {
            Angle = angle;
            Name = name;
            ShortName = shortName;
        }

        public int Angle { get; }

        /// <summary>Désignation complète, affichée comme état courant.</summary>
        public string Name { get; }

        /// <summary>Désignation courte, portée par le bouton de rappel.</summary>
        public string ShortName { get; }

        /// <summary>Voie à laquelle cette position appartient, renseignée par elle.</summary>
        public ServoChannel? Owner { get; internal set; }

        public string Tooltip => $"Amener « {Owner?.Role} » à : {Name} ({Angle}°)";
    }

    /// <summary>
    /// Une voie de commande : un servomoteur, donc un mouvement de la grue.
    /// L'IHM construit un bandeau par voie à partir de cette classe, ce qui
    /// évite d'avoir à retoucher le XAML quand les vérins changent.
    /// </summary>
    public class ServoChannel : INotifyPropertyChanged
    {
        // Doivent rester alignées sur ANGLE_MIN / ANGLE_MAX du sketch Arduino.
        public const int AngleMin = 15;
        public const int AngleMax = 165;
        public const int AngleInit = 90;

        private double _angle = AngleInit;
        private bool _isMoving;

        public ServoChannel(int number, int[] pins, bool mirrored, string role, string movement, IEnumerable<ServoPosition> positions)
        {
            Number = number;
            Pins = pins;
            Mirrored = mirrored;
            Role = role;
            Movement = movement;

            Positions = new ReadOnlyCollection<ServoPosition>(positions.ToList());
            foreach (ServoPosition position in Positions)
                position.Owner = this;
        }

        /// <summary>Numéro de voie tel qu'attendu par la carte, à partir de 1.</summary>
        public int Number { get; }

        /// <summary>
        /// Broches des servomoteurs qui entraînent ce mouvement. Il y en a
        /// deux : ils sont montés de part et d'autre du mécanisme.
        /// </summary>
        public int[] Pins { get; }

        /// <summary>
        /// true quand les deux servomoteurs sont montés face à face et doivent
        /// donc tourner en sens contraire. Le miroir lui-même est appliqué par
        /// la carte, pour que les deux moitiés d'une paire bougent dans la même
        /// instruction ; l'information n'est reprise ici que pour l'affichage.
        /// </summary>
        public bool Mirrored { get; }

        /// <summary>
        /// Mouvement de grue piloté par cette voie, dans le vocabulaire du
        /// métier : « Orientation » pour le pivotement de la flèche,
        /// « Levage » pour la montée et la descente de la charge.
        /// </summary>
        public string Role { get; }

        /// <summary>Sens du mouvement, en clair, pour lever toute ambiguïté.</summary>
        public string Movement { get; }

        /// <summary>
        /// Positions remarquables, de la butée basse à la butée haute. La
        /// première et la dernière servent aussi de repères aux extrémités du
        /// curseur, et chacune donne un bouton de rappel.
        /// </summary>
        public IReadOnlyList<ServoPosition> Positions { get; }

        /// <summary>Repère affiché sous l'extrémité gauche du curseur.</summary>
        public string StartLabel => Positions[0].ShortName;

        /// <summary>Repère affiché sous l'extrémité droite du curseur.</summary>
        public string EndLabel => Positions[^1].ShortName;

        public string Wiring => Pins.Length == 1
            ? $"mouvement {Number} · 1 servomoteur, broche {Pins[0]}"
            : $"mouvement {Number} · {Pins.Length} servomoteurs, broches {string.Join(" et ", Pins)}"
              + (Mirrored ? " · montés en opposition" : " · montés en tandem");

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
                OnPropertyChanged(nameof(PositionName));
                OnPropertyChanged(nameof(Readout));
            }
        }

        /// <summary>Consigne entière effectivement envoyée à la carte.</summary>
        public int TargetAngle => Math.Clamp((int)Math.Round(_angle), AngleMin, AngleMax);

        /// <summary>Part de la course totale, repère familier à un technicien.</summary>
        public int TravelPercent =>
            (int)Math.Round((TargetAngle - AngleMin) * 100.0 / (AngleMax - AngleMin));

        /// <summary>
        /// État courant en langage métier. La position retenue est la plus
        /// proche en angle : les frontières tombent donc à mi-chemin entre deux
        /// positions remarquables, ce qui évite d'annoncer « Entièrement monté »
        /// alors que la course n'est qu'aux trois quarts.
        /// </summary>
        public string PositionName => NearestPosition(TargetAngle).Name;

        /// <summary>Ligne de précision sous l'état, pour qui veut le chiffre.</summary>
        public string Readout => $"{TargetAngle}°  ·  {TravelPercent} % de la course";

        /// <summary>
        /// Vrai entre l'acceptation d'une consigne et le moment où la carte
        /// annonce l'avoir atteinte. Les mouvements étant volontairement lents,
        /// un clic resterait sans effet visible pendant plusieurs secondes si
        /// rien ne signalait que la grue est en train de se déplacer.
        /// </summary>
        public bool IsMoving
        {
            get => _isMoving;
            set
            {
                if (_isMoving == value)
                    return;

                _isMoving = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MovingText));
            }
        }

        public string MovingText => _isMoving ? "mouvement en cours…" : "position atteinte";

        public ServoPosition NearestPosition(int angle)
        {
            ServoPosition nearest = Positions[0];
            int bestGap = Math.Abs(angle - nearest.Angle);

            foreach (ServoPosition candidate in Positions)
            {
                int gap = Math.Abs(angle - candidate.Angle);
                if (gap < bestGap)
                {
                    bestGap = gap;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
