using GTA;
using GTA.Math;
using GTA.Native;

namespace CommunityRaces
{
	public class Rival
	{
		// AvoidVehicles | AvoidEmptyVehicles | AvoidPeds | AvoidObjects | AllowGoingWrongWay | AllowMedianCrossing
		public static int MainDrivingStyle = 4 | 8 | 16 | 32 | 512 | 262144;

		public Ped Character;
		public Vehicle Vehicle;
		public int Knowledge;


		public Rival(Vector3 pos, float heading, Model car)
		{
			Character = World.CreateRandomPed(pos);
			Vehicle = World.CreateVehicle(car, pos, heading);
			Function.Call(Hash.SET_PED_INTO_VEHICLE, Character.Handle, Vehicle.Handle, (int)VehicleSeat.Driver);
			Vehicle.IsPersistent = false;
			Vehicle.FreezePosition = true;

			Function.Call(Hash.SET_VEHICLE_MOD_KIT, Vehicle.Handle, 0);
			Vehicle.SetMod(VehicleMod.Engine, 3, false);
			Vehicle.SetMod(VehicleMod.Brakes, 2, false);
			Vehicle.SetMod(VehicleMod.Transmission, 2, false);
			Vehicle.SetMod(VehicleMod.Suspension, 3, false);
			
			Character.MaxDrivingSpeed = 500f;
			Character.DrivingStyle = (DrivingStyle)MainDrivingStyle;
			Character.DrivingSpeed = 200f;
			Function.Call(Hash.SET_DRIVER_ABILITY, Character.Handle, 1f);
			Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, Character.Handle, 1f);
		}

		public void Clean()
		{
			Character?.CurrentBlip?.Remove();
			Character?.Delete();
			Vehicle?.Delete();
		}
	}
}