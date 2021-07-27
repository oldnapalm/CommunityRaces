using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using NativeUI;
using Font = GTA.Font;

namespace CommunityRaces
{
	public static class GUI
	{
		public static Camera MainCamera;
		public static UIMenu MainMenu = new UIMenu("", "~w~RACE OPTIONS", new Point(0, 0));
		public static bool IsInMenu;

		public static void DrawSettings(Race settingsForRace, Vehicle previewVehicle)
		{
			if(!IsInMenu) return;

			// CAMERA
			if (MainCamera == null || MainCamera.Position != settingsForRace.Trigger + new Vector3(3f, 3f, 3f))
			{
				World.DestroyAllCameras();
				MainCamera = World.CreateCamera(settingsForRace.Trigger + new Vector3(3f, 3f, 3f), new Vector3(), 60f);
				MainCamera.PointAt(settingsForRace.Trigger);
			}
			World.RenderingCamera = MainCamera;

			// UI DRAWING
			var safe = UIMenu.GetSafezoneBounds();
			var res = UIMenu.GetScreenResolutionMantainRatio();

			new UIResText(settingsForRace.Name, new Point(safe.X, safe.Y), 0.8f, Color.White, Font.ChaletComprimeCologne, UIResText.Alignment.Left) { Outline = true}.Draw();
			new UIResText(settingsForRace.Description, new Point(safe.X, 50 + safe.Y), 0.4f, Color.White, Font.ChaletComprimeCologne, UIResText.Alignment.Left) {WordWrap = new Size(Convert.ToInt32(res.Width) - (safe.X*2),0), Outline = true}.Draw();

			new UIResRectangle(new Point(safe.X + 435, safe.Y + 107), new Size(1200, 37), Color.Black).Draw();
			new UIResText("VEHICLE", new Point(safe.X + 1000, safe.Y + 110), 0.35f, Color.White, Font.ChaletLondon, UIResText.Alignment.Centered).Draw();

			string vehClass = "";
			switch (Function.Call<int>(Hash.GET_VEHICLE_CLASS, previewVehicle.Handle))
			{
				case 0:
					vehClass = "Compacts";
					break;
				case 1:
					vehClass = "Sedans";
					break;
				case 2:
					vehClass = "SUVs";
					break;
				case 3:
					vehClass = "Coupes";
					break;
				case 4:
					vehClass = "Muscle";
					break;
				case 5:
					vehClass = "Sports Classics";
					break;
				case 6:
					vehClass = "Sports";
					break;
				case 7:
					vehClass = "Super";
					break;
				case 8:
					vehClass = "Motorcycle";
					break;
				case 9:
					vehClass = "Offroad";
					break;
				case 10:
					vehClass = "Industrial";
					break;
				case 11:
					vehClass = "Utility";
					break;
				case 12:
					vehClass = "Vans";
					break;
				case 13:
					vehClass = "Bicycle";
					break;
				case 14:
					vehClass = "Boats";
					break;
				case 15:
					vehClass = "Helicopter";
					break;
				case 16:
					vehClass = "Airplane";
					break;
				case 17:
					vehClass = "Service";
					break;
				case 18:
					vehClass = "Emergency";
					break;
				case 19:
					vehClass = "Military";
					break;
				case 20:
					vehClass = "Commercial";
					break;
			}

			new UIResText(vehClass, new Point(Convert.ToInt32(res.Width) - 430 - safe.X, 800 - safe.Y), 1.5f, Color.White, Font.ChaletComprimeCologne, UIResText.Alignment.Left) {DropShadow = true}.Draw();
			new UIResText(previewVehicle.FriendlyName, new Point(Convert.ToInt32(res.Width) - 400 - safe.X, 840 - safe.Y), 1.5f, Color.DodgerBlue, Font.HouseScript, UIResText.Alignment.Left) {DropShadow = true}.Draw();

			// MENU CORRECTIONS
			MainMenu.Subtitle.Position = new Point(safe.X + 200, MainMenu.Subtitle.Position.Y);
			MainMenu.Subtitle.TextAlignment = UIResText.Alignment.Centered;
			
		}

	}
}