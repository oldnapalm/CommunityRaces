using GTA;
using GTA.Math;
using System;

namespace CommunityRaces
{
    public class Replay
    {
        public uint Time;
        public uint Vehicle;
        public Record[] Records;

        public Replay(uint time, uint vehicle, Record[] records)
        {
            Time = time;
            Vehicle = vehicle;
            Records = records;
        }
    }

    public class Record
    {
        public float PX;
        public float PY;
        public float PZ;
        public float RX;
        public float RY;
        public float RZ;
        public float RW;
        public float VX;
        public float VY;
        public float VZ;
        public float S;

        public Record(Vector3 position, Quaternion rotation, Vector3 velocity, float speed)
        {
            PX = position.X;
            PY = position.Y;
            PZ = position.Z;
            RX = rotation.X;
            RY = rotation.Y;
            RZ = rotation.Z;
            RW = rotation.W;
            VX = velocity.X;
            VY = velocity.Y;
            VZ = velocity.Z;
            S = speed;
        }

        public Vector3 GetPosition()
        {
            return new Vector3(PX, PY, PZ);
        }

        public Quaternion GetRotation()
        {
            return new Quaternion(RX, RY, RZ, RW);
        }

        public Vector3 GetVelocity()
        {
            return new Vector3(VX, VY, VZ);
        }
    }

    public class Ghost
    {
        public Vehicle Vehicle;
        public Ped Ped;

        private readonly Record[] Records;
        private readonly Blip Blip;
        private int Index;
        private int StopTime;

        public Ghost(Replay replay)
        {
            Records = replay.Records;
            Index = 0;
            var record = Records[Index];
            Vehicle = World.CreateVehicle(Helpers.RequestModel((int)replay.Vehicle), record.GetPosition(), record.GetRotation().Z);
            Vehicle.Quaternion = record.GetRotation();
            Vehicle.IsInvincible = true;
            if (Config.Opacity < 255)
                Vehicle.Alpha = Config.Opacity;
            Ped = World.CreateRandomPed(record.GetPosition());
            Ped.IsInvincible = true;
            if (Config.Opacity < 255)
                Ped.Alpha = Config.Opacity;
            Ped.SetIntoVehicle(Vehicle, VehicleSeat.Driver);
            Blip = Vehicle.AddBlip();
            Blip.Sprite = BlipSprite.Ghost;
        }

        public void Update()
        {
            if (Index < Records.Length)
            {
                var record = Records[Index];

                if (record.S > 0.2f && Vehicle.IsInRangeOf(record.GetPosition(), 7.0f))
                {
                    Vehicle.Velocity = record.GetVelocity() + (record.GetPosition() - Vehicle.Position);
                    Vehicle.Quaternion = Quaternion.Slerp(Vehicle.Quaternion, record.GetRotation(), 0.25f);
                    StopTime = Environment.TickCount;
                }
                else if (Environment.TickCount - StopTime <= 1000)
                {
                    Vector3 posTarget = Helpers.LinearVectorLerp(Vehicle.Position, record.GetPosition() + (record.GetPosition() - Vehicle.Position), Environment.TickCount - StopTime, 1000);
                    Vehicle.PositionNoOffset = posTarget;
                    Vehicle.Quaternion = Quaternion.Slerp(Vehicle.Quaternion, record.GetRotation(), 0.5f);
                }
                else
                {
                    Vehicle.Position = record.GetPosition();
                    Vehicle.Quaternion = record.GetRotation();
                }
            }
            else
                Vehicle.HandbrakeOn = true;
        }

        public void NextRecord()
        {
            if (Index < Records.Length)
            {
                Index++;

                if (!Ped.IsInVehicle(Vehicle))
                    Ped.SetIntoVehicle(Vehicle, VehicleSeat.Driver);
            }
        }

        public void Delete()
        {
            Blip?.Remove();
            Ped?.Delete();
            Vehicle?.Delete();
        }
    }
}
