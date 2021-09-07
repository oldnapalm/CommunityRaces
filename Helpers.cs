using System;
using GTA;
using GTA.Math;
using GTA.Native;

namespace CommunityRaces
{
    public static class Helpers
    {
        public static Vector3 RotationToDirection(Vector3 rotation)
        {
            var z = DegToRad(rotation.Z);
            var x = DegToRad(rotation.X);
            var num = Math.Abs(Math.Cos(x));
            return new Vector3
            {
                X = (float)(-Math.Sin(z) * num),
                Y = (float)(Math.Cos(z) * num),
                Z = (float)Math.Sin(x)
            };
        }

        public static Vector3 DirectionToRotation(Vector3 direction)
        {
            direction.Normalize();

            var x = Math.Atan2(direction.Z, direction.Y);
            var y = 0;
            var z = -Math.Atan2(direction.X, direction.Y);

            return new Vector3
            {
                X = (float)RadToDeg(x),
                Y = (float)RadToDeg(y),
                Z = (float)RadToDeg(z)
            };
        }

        public static double DegToRad(double deg)
        {
            return deg * Math.PI / 180.0;
        }

        public static double RadToDeg(double deg)
        {
            return deg * 180.0 / Math.PI;
        }

        public static double BoundRotationDeg(double angleDeg)
        {
            var twoPi = (int)(angleDeg / 360);
            var res = angleDeg - twoPi * 360;
            if (res < 0) res += 360;
            return res;
        }

        public static Model RequestModel(int hash, int limit = 1000)
        {
            var tmpModel = new Model(hash);
            int counter = 0;
            while (!tmpModel.IsLoaded && counter < limit)
            {
                tmpModel.Request();
                Script.Yield();
                counter++;
            }
            return tmpModel;
        }

        public static Vector3 LinearVectorLerp(Vector3 start, Vector3 end, int currentTime, int duration)
        {
            return new Vector3()
            {
                X = LinearFloatLerp(start.X, end.X, currentTime, duration),
                Y = LinearFloatLerp(start.Y, end.Y, currentTime, duration),
                Z = LinearFloatLerp(start.Z, end.Z, currentTime, duration),
            };
        }

        public static float LinearFloatLerp(float start, float end, int currentTime, int duration)
        {
            return (end - start) * currentTime / duration + start;
        }

        public static void Teleport(Vector3 location)
        {
            if (Config.CayoPericoLoader)
            {
                var island = location.DistanceTo2D(new Vector2(5031.428f, -5150.907f)) < 2000f;
                Function.Call((Hash)0x9A9D1BA639675CF1, "HeistIsland", island); // island hopper
                Function.Call((Hash)0xF74B1FFA4A15FBEA, island); // path nodes
            }

            int i = 0;
            float groundHeight;
            location.Z = -50;
            do
            {
                Script.Wait(50);
                groundHeight = World.GetGroundHeight(location);
                if (groundHeight == 0)
                    location.Z += 50;
                else
                    location.Z = groundHeight;
                if (Game.Player.Character.CurrentVehicle != null)
                    Game.Player.Character.CurrentVehicle.Position = location;
                else
                    Game.Player.Character.Position = location;
                i++;
            }
            while (groundHeight == 0 && i < 20);
        }
    }
}