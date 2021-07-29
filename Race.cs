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

        public Race(Race copyFrom)
        {
            Checkpoints = copyFrom.Checkpoints;
            SpawnPoints = copyFrom.SpawnPoints;
            AvailableVehicles = copyFrom.AvailableVehicles;
            LapsAvailable = copyFrom.LapsAvailable;
            DecorativeProps = copyFrom.DecorativeProps;
            Trigger = copyFrom.Trigger;

            Name = copyFrom.Name;
            Description = copyFrom.Description;
            FileName = copyFrom.FileName;
        }
    }
}