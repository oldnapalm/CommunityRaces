using GTA;
using GTA.Math;
using GTA.Native;
using System;
using System.Collections.Generic;

namespace CommunityRaces
{
    public class Replay
    {
        public int Time;
        public Record[] Records;

        public Replay(int time, Record[] records)
        {
            Time = time;
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

        private readonly List<Record> Records;
        private readonly Blip Blip;
        private int Index;
        private int StopTime;

        public Ghost(List<Record> records, VehicleHash vehicle)
        {
            Records = records;
            Index = 0;
            var record = Records[Index];
            Vehicle = World.CreateVehicle(Helpers.RequestModel((int)vehicle), record.GetPosition(), record.GetRotation().Z);
            Vehicle.Quaternion = record.GetRotation();
            Vehicle.IsInvincible = true;
            Vehicle.Alpha = 100;
            Ped = World.CreateRandomPed(record.GetPosition());
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

                Index++;
                var record = Records[Index];

                if (record.S > 0.2f && Vehicle.IsInRangeOf(record.GetPosition(), 7.0f))
                {
                    Vehicle.Velocity = record.GetVelocity() + (record.GetPosition() - Vehicle.Position);
                    Vehicle.Quaternion = Quaternion.Slerp(Vehicle.Quaternion, record.GetRotation(), 0.5f);
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
