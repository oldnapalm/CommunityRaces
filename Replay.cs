using GTA;
using GTA.Math;
using GTA.Native;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace CommunityRaces
{
    public class Replay
    {
        public Record[] Records;

        public Replay() { }

        public Replay(string fileName)
        {
            if (File.Exists(fileName))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(Replay));
                StreamReader file = new StreamReader(fileName);
                var replay = (Replay)serializer.Deserialize(file);
                file.Close();
                Records = replay.Records;
            }
        }
    }

    public class Record
    {
        public float X;
        public float Y;
        public float Z;
        public float Speed;

        public Record() { }

        public Record(float x, float y, float z, float speed)
        {
            X = x;
            Y = y;
            Z = z;
            Speed = speed;
        }
    }

    public class Ghost
    {
        public Vehicle Vehicle;
        public Ped Ped;

        private List<Record> Records;
        private Blip Blip;
        private int Index;

        private static int GhostDrivingStyle = 16777216; // IgnorePathFinding

        public Ghost(List<Record> records, VehicleHash vehicle)
        {
            Records = records;
            Index = 0;
            Vector3 start = GetPoint(Index);
            Vehicle = World.CreateVehicle(Helpers.RequestModel((int)vehicle), start, GetHeading(Index));
            Vehicle.IsInvincible = true;
            Vehicle.Alpha = 100;
            Ped = World.CreateRandomPed(start);
            Ped.IsInvincible = true;
            Ped.Alpha = 100;
            Ped.SetIntoVehicle(Vehicle, VehicleSeat.Driver);
            Blip = Vehicle.AddBlip();
            Blip.Sprite = BlipSprite.Ghost;
        }

        public void Update()
        {
            if (Records.Count > Index + 1)
            {
                if (!Ped.IsInVehicle(Vehicle))
                    Ped.SetIntoVehicle(Vehicle, VehicleSeat.Driver);
                float speed = Records[Index].Speed;
                float distance = Vehicle.Position.DistanceTo(GetPoint(Index));
                if (distance > 20f)
                {
                    Vehicle.Position = GetPoint(Index);
                    Vehicle.Heading = GetHeading(Index);
                }
                else if (distance > 5f)
                    speed *= 1.1f;
                Index++;
                Ped.Task.ClearAll();
                if (!Vehicle.IsInAir || Vehicle.Model.IsHelicopter || Vehicle.Model.IsPlane)
                {
                    Function.Call(Hash.TASK_VEHICLE_MISSION_COORS_TARGET, Ped.Handle, Vehicle.Handle, Records[Index].X, Records[Index].Y, Records[Index].Z, CommunityRaces.Mode, speed, GhostDrivingStyle, 5f, 0f, 0);
                    Vehicle.Speed = speed;
                }
            }
            else
            {
                Ped.Task.ClearAll();
                Vehicle.HandbrakeOn = true;
            }
        }

        private Vector3 GetPoint(int i)
        {
            return new Vector3(Records[i].X, Records[i].Y, Records[i].Z);
        }

        private float GetHeading(int i)
        {
            return (new Vector2(Records[i + 1].X, Records[i + 1].Y) - new Vector2(Records[i].X, Records[i].Y)).ToHeading();
        }

        public void Delete()
        {
            Blip?.Remove();
            Ped?.Delete();
            Vehicle?.Delete();
            Records?.Clear();
        }
    }
}
