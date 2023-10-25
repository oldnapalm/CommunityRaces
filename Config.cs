using GTA;
using System;
using System.Windows.Forms;

namespace CommunityRaces
{
    public static class Config
    {
        public static ScriptSettings _settings;
        public static int Opacity;
        public static bool Collision;
        public static bool CayoPericoLoader;
        public static bool RacesBlips;
        public static Keys TeleportKey;

        static Config()
        {
            _settings = ScriptSettings.Load(@".\Scripts\CommunityRaces.ini");
            var o = _settings.GetValue("Ghost", "Opacity", 2);
            if (o < 1) o = 1;
            if (o > 5) o = 5;
            Opacity = o * 51;
            Collision = _settings.GetValue("Ghost", "Collision", false);
            CayoPericoLoader = _settings.GetValue("Main", "CayoPericoLoader", true);
            RacesBlips = _settings.GetValue("Main", "RacesBlips", true);
            TeleportKey = (Keys)Enum.Parse(typeof(Keys), _settings.GetValue("Main", "TeleportKey", "F8"), true);
        }

        public static string Time
        {
            get
            {
                return _settings.GetValue<string>("Main", "Time", "Current");
            }
            set
            {
                _settings.SetValue<string>("Main", "Time", value);
                _settings.Save();
            }
        }

        public static string Weather
        {
            get
            {
                return _settings.GetValue<string>("Main", "Weather", "Current");
            }
            set
            {
                _settings.SetValue<string>("Main", "Weather", value);
                _settings.Save();
            }
        }

        public static int Wanted
        {
            get
            {
                return _settings.GetValue("Main", "Wanted", 0);
            }
            set
            {
                _settings.SetValue("Main", "Wanted", value);
                _settings.Save();
            }
        }

        public static int WantedCheckInterval
        {
            get
            {
                return _settings.GetValue("Main", "WantedCheckInterval", 10);
            }
        }

        public static int WantedProbability
        {
            get
            {
                return _settings.GetValue("Main", "WantedProbability", 20);
            }
        }

        public static string Vehicle
        {
            get
            {
                return _settings.GetValue<string>("Main", "Vehicle", "");
            }
            set
            {
                _settings.SetValue<string>("Main", "Vehicle", value);
                _settings.Save();
            }
        }

        public static string PrimaryColor
        {
            get
            {
                return _settings.GetValue<string>("Main", "PrimaryColor", "Random");
            }
            set
            {
                _settings.SetValue<string>("Main", "PrimaryColor", value);
                _settings.Save();
            }
        }

        public static string SecondaryColor
        {
            get
            {
                return _settings.GetValue<string>("Main", "SecondaryColor", "Random");
            }
            set
            {
                _settings.SetValue<string>("Main", "SecondaryColor", value);
                _settings.Save();
            }
        }

        public static string Radio
        {
            get
            {
                return _settings.GetValue<string>("Main", "Radio", "Random");
            }
            set
            {
                _settings.SetValue<string>("Main", "Radio", value);
                _settings.Save();
            }
        }

        public static string Opponents
        {
            get
            {
                return _settings.GetValue<string>("Main", "Opponents", "Random");
            }
            set
            {
                _settings.SetValue<string>("Main", "Opponents", value);
                _settings.Save();
            }
        }

        public static int Bet
        {
            get
            {
                return _settings.GetValue("Main", "Bet", 0);
            }
            set
            {
                _settings.SetValue("Main", "Bet", value);
                _settings.Save();
            }
        }

        public static bool Traffic
        {
            get
            {
                return _settings.GetValue("Main", "Traffic", true);
            }
            set
            {
                _settings.SetValue("Main", "Traffic", value);
                _settings.Save();
            }
        }

        public static bool Peds
        {
            get
            {
                return _settings.GetValue("Main", "Peds", true);
            }
            set
            {
                _settings.SetValue("Main", "Peds", value);
                _settings.Save();
            }
        }

        public static int Laps
        {
            get
            {
                return _settings.GetValue("Main", "Laps", 1);
            }
            set
            {
                _settings.SetValue("Main", "Laps", value);
                _settings.Save();
            }
        }
    }
}
