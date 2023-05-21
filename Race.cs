using GTA;
using GTA.Math;
using GTA.Native;

namespace CommunityRaces
{
    public class Race
    {
        public Vector3[] Checkpoints;
        public SpawnPoint[] SpawnPoints;
        public VehicleHash[] AvailableVehicles;
        public bool LapsAvailable = true;
        public SavedProp[] DecorativeProps;
        public Vector3 Trigger;

        public string Name;
        public string Description;
        public string FileName;

        public Race() { }
    }

    public class RaceBlip
    {
        public string Name;
        public string Path;
        public Vector3 Trigger;
        private Blip Blip;

        public RaceBlip(string name, string path, Vector3 trigger)
        {
            Name = name;
            Path = path;
            Trigger = trigger;
        }

        public void Add()
        {
            var blip = World.CreateBlip(Trigger);
            blip.IsShortRange = true;
            blip.Sprite = BlipSprite.RaceFinish;
            blip.Name = "Community Race: " + Name;
            Blip = blip;
        }

        public void Remove()
        {
            Blip.Remove();
        }
    }
}