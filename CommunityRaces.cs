using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;
using GTA;
using GTA.Math;
using GTA.Native;
using MapEditor;
using MapEditor.API;
using NativeUI;
using Newtonsoft.Json;
using Font = GTA.Font;

namespace CommunityRaces
{
    public class CommunityRaces : Script
    {
        private static readonly Random RandGen = new Random();

        private Blip _nextBlip;
        private Blip _secondBlip;

        private bool _isInRace;
        private int _countdown = -1;
        private uint _missionStart;
        private uint _seconds = 0;
        private int _totalLaps;
        private float _oldAngle;
        private bool _wanted;
        private bool _traffic;
        private bool _peds;
        private int _laps;
        private readonly UIMenu _quitMenu;
        private Race _previewRace;
        private Race _currentRace;
        private Sprite _fadeoutSprite;
        private Vehicle _previewVehicle;
        private Vehicle _currentVehicle;
        private VehicleHash _vehicleHash;
        private VehicleColor _vehiclePrimaryColor;
        private VehicleColor _vehicleSecondaryColor;
        private MissionPassedScreen _passed;
        private int _lasttime = Environment.TickCount;
        private List<Vector3> _checkpoints = new List<Vector3>();
        private readonly List<Record> _records = new List<Record>();
        private int _lastRecord = Environment.TickCount;
        private Replay _replay;
        private Ghost _ghost;
        private readonly Blip _island;
        private SpawnPoint _respawnPoint;

        private readonly List<RaceBlip> _races = new List<RaceBlip>();
        private readonly List<Entity> _cleanupBag = new List<Entity>();
        private readonly List<Vehicle> _participants = new List<Vehicle>();
        private readonly List<Rival> _currentRivals = new List<Rival>();
        private readonly List<Rival> _finishedParticipants = new List<Rival>();
        private readonly List<Tuple<Rival, int>> _rivalCheckpointStatus = new List<Tuple<Rival, int>>();

        private readonly XmlSerializer _serializer = new XmlSerializer(typeof(Race));

        private const int Mode = 4;

        public CommunityRaces()
        {
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAbort;
            LoadRaces();

            _quitMenu = new UIMenu("", "~r~ARE YOU SURE YOU WANT TO QUIT?", new Point(0, -107));
            var ritem = new UIMenuItem("Respawn at last checkpoint.");
            ritem.Activated += (item, index) =>
            {
                _quitMenu.Visible = false;
                Game.Player.Character.SetIntoVehicle(_currentVehicle, VehicleSeat.Driver);
                _currentVehicle.Position = _respawnPoint.Position;
                _currentVehicle.Heading = _respawnPoint.Heading;
                _currentVehicle.Repair();
            };
            _quitMenu.AddItem(ritem);
            var qitem = new UIMenuItem("Quit current race.");
            qitem.Activated += (item, index) =>
            {
                if (_currentRace == null) return;
                _quitMenu.Visible = false;
                Game.FadeScreenOut(500);
                Wait(1000);
                Game.Player.Character.Position = _currentRace.Trigger + new Vector3(4f, 0f, 0f);
                EndRace(true);
                Game.FadeScreenIn(500);
                AddRacesBlips();
            };
            _quitMenu.AddItem(qitem);
            var citem = new UIMenuItem("Cancel.");
            citem.Activated += (item, index) =>
            {
                _quitMenu.Visible = false;
            };
            _quitMenu.AddItem(citem);
            _quitMenu.RefreshIndex();
            _quitMenu.SetBannerType(new UIResRectangle());

            AddRacesBlips();

            if (File.Exists("scripts\\MapEditor.dll"))
                AttachMapEditor();

            if (Config.CayoPericoLoader)
            {
                // invisible blip to make the map clickable at the island
                _island = World.CreateBlip(new Vector3(5943.567f, -6272.114f, 2f));
                _island.Sprite = BlipSprite.Invisible;
                _island.Scale = 0f;
                _island.IsShortRange = false;
            }
        }

        private void AddRacesBlips()
        {
            if (Config.RacesBlips)
                foreach (RaceBlip race in _races)
                    race.Add();
        }

        private void RemoveRacesBlips()
        {
            if (Config.RacesBlips)
                foreach (RaceBlip race in _races)
                    race.Remove();
        }

        /// <summary>
        /// This method is encapsulated, so if MapEditor.dll is missing, the script won't crash!
        /// </summary>
        private void AttachMapEditor()
        {
            var thisMod = new ModListener()
            {
                ButtonString = "Create a Community Race",
                Description = "Create a race for the Community Races mod.",
                Name = "Community Races",
            };
            ModManager.SuscribeMod(thisMod);
            thisMod.OnMapSaved += SaveMap;
        }

        private void LoadRaces()
        {
            if (!Directory.Exists("scripts\\Races")) return;

            foreach (string path in Directory.GetFiles("scripts\\Races", "*.xml"))
                using (StreamReader file = new StreamReader(path))
                {
                    try
                    {
                        var raceout = (Race)_serializer.Deserialize(file);
                        _races.Add(new RaceBlip(raceout.Name, path, raceout.Trigger));
                    }
                    catch
                    {
                        UI.Notify($"Error loading {Path.GetFileName(path)}");
                    }
                }
        }

        private int CalculatePlayerPositionInRace()
        {
            int output = 0;
            int playerCheckpoint = _currentRace.Checkpoints.Length - _checkpoints.Count;

            int beforeYou = _rivalCheckpointStatus.Count(tuple => tuple.Item2 > playerCheckpoint);
            output += beforeYou;

            var samePosAsYou = _rivalCheckpointStatus.Where(tuple => tuple.Item2 == playerCheckpoint);
            output +=
                samePosAsYou.Count(
                    tuple =>
                        (_currentRace.Checkpoints[playerCheckpoint] - tuple.Item1.Vehicle.Position).Length() <
                        (_currentRace.Checkpoints[playerCheckpoint] - Game.Player.Character.Position).Length());

            return output;
        }

        private void StartRace(Race race)
        {
            _wanted = Config.Wanted;
            _traffic = Config.Traffic;
            _peds = Config.Peds;
            _laps = Config.Laps;

            _records.Clear();
            _ghost = null;
            var fileName = "scripts\\Races\\Replays\\" + race.FileName + ".json";
            if (File.Exists(fileName))
                using (StreamReader reader = new StreamReader(fileName))
                    using (JsonTextReader jsonReader = new JsonTextReader(reader))
                        _replay = new JsonSerializer().Deserialize<Replay>(jsonReader);
            else
                _replay = new Replay(0, (uint)_vehicleHash, new Record[0]);

            Game.FadeScreenOut(500);
            Wait(500);
            _isInRace = true;
            _currentRace = race;
            if (_laps > 1)
            {
                _totalLaps = race.Checkpoints.Length;
                List<Vector3> tmpCheckpoints = new List<Vector3>();
                for (int i = 0; i < _laps; i++)
                {
                    tmpCheckpoints.AddRange(race.Checkpoints);
                }
                _currentRace.Checkpoints = tmpCheckpoints.ToArray();
            }

            if (Config.Weather != "Current")
            {
                Enum.TryParse(Config.Weather, out Weather wout);
                World.Weather = wout;
            }

            switch (Config.Time)
            {
                case "Current":
                    break;
                case "Sunrise":
                    World.CurrentDayTime = new TimeSpan(06, 00, 00);
                    break;
                case "Day":
                    World.CurrentDayTime = new TimeSpan(16, 00, 00);
                    break;
                case "Sunset":
                    World.CurrentDayTime = new TimeSpan(20, 00, 00);
                    break;
                case "Night":
                    World.CurrentDayTime = new TimeSpan(02, 00, 00);
                    break;
            }

            List<SpawnPoint> availalbleSpawnPoints = new List<SpawnPoint>(race.SpawnPoints);

            int spawnId = RandGen.Next(availalbleSpawnPoints.Count);
            var spawn = availalbleSpawnPoints[spawnId];
            availalbleSpawnPoints.RemoveAt(spawnId);
            _respawnPoint = spawn;

            _currentVehicle?.Delete();
            _currentVehicle = World.CreateVehicle(Helpers.RequestModel((int)_vehicleHash), spawn.Position, spawn.Heading);
            _currentVehicle.PrimaryColor = _vehiclePrimaryColor;
            _currentVehicle.SecondaryColor = _vehicleSecondaryColor;
            _currentVehicle.RimColor = _vehicleSecondaryColor;
            _currentVehicle.DashboardColor = _vehicleSecondaryColor;
            Function.Call(Hash.SET_PED_INTO_VEHICLE, Game.Player.Character.Handle, _currentVehicle.Handle, (int)VehicleSeat.Driver);
            _currentVehicle.IsPersistent = false;
            _currentVehicle.FreezePosition = true;

            if (Config.Radio == "RadioOff")
                Function.Call(Hash.SET_VEH_RADIO_STATION, _currentVehicle.Handle, "OFF");
            else
                SetRadioStation();

            int spawnlen = 0;
            switch (Config.Opponents)
            {
                case "Random":
                    spawnlen = RandGen.Next(1, race.SpawnPoints.Length - 1);
                    break;
                case "Ghost":
                    if (_replay.Records.Length > 0)
                        _ghost = new Ghost(_replay);
                    break;
                case "None":
                    break;
                default:
                    spawnlen = Convert.ToInt32(Config.Opponents);
                    break;
            }

            for (int i = 0; i < spawnlen; i++)
            {
                var spid = RandGen.Next(availalbleSpawnPoints.Count);
                Model mod = Helpers.RequestModel((int)race.AvailableVehicles[RandGen.Next(race.AvailableVehicles.Length)]);
                var riv = new Rival(availalbleSpawnPoints[spid].Position, availalbleSpawnPoints[spid].Heading, mod);
                _participants.Add(riv.Vehicle);
                availalbleSpawnPoints.RemoveAt(spid);
                Function.Call(Hash.TASK_VEHICLE_MISSION_COORS_TARGET, riv.Character.Handle, riv.Vehicle.Handle, race.Checkpoints[0].X, race.Checkpoints[0].Y, race.Checkpoints[0].Z, Mode, 200f, Rival.MainDrivingStyle, 5f, 0f, 0);
                _rivalCheckpointStatus.Add(new Tuple<Rival, int>(riv, 0));
                var tmpblip = riv.Character.AddBlip();
                tmpblip.Color = BlipColor.Blue;
                tmpblip.IsShortRange = true;
                tmpblip.Scale = 0.6f;
                _currentRivals.Add(riv);
            }

            foreach (SavedProp prop in race.DecorativeProps)
            {
                var tmpProp = World.CreateProp(Helpers.RequestModel(prop.Hash), prop.Position, prop.Rotation, false, false);
                tmpProp.Position = prop.Position;
                if (prop.Dynamic)
                    tmpProp.FreezePosition = true;
                if (prop.Texture > 0 && prop.Texture < 16)
                    Function.Call((Hash)0x971DA0055324D033, tmpProp, prop.Texture);
                _cleanupBag.Add(tmpProp);
            }

            _checkpoints = race.Checkpoints.ToList();
            _missionStart = _seconds;
            Game.FadeScreenIn(500);
            Wait(500);
            _countdown = 5;
            _participants.Add(_currentVehicle);

            if (!_traffic)
                Function.Call(Hash.CLEAR_AREA_OF_VEHICLES, spawn.Position.X, spawn.Position.Y, spawn.Position.Z, 1000f, 0);

            if (!_peds)
                Function.Call(Hash.CLEAR_AREA_OF_PEDS, spawn.Position.X, spawn.Position.Y, spawn.Position.Z, 1000f, 0);
        }

        private void EndRace(bool reset)
        {
            _isInRace = false;
            _currentRace = null;

            _secondBlip?.Remove();
            _nextBlip?.Remove();
            _checkpoints.Clear();
            if (reset)
                _currentVehicle?.Delete();
            foreach (Entity entity in _cleanupBag)
            {
                entity?.Delete();
            }
            _cleanupBag.Clear();
            _participants.Clear();
            _countdown = -1;
            foreach (Rival rival in _currentRivals)
            {
                rival.Clean();
            }
            _currentRivals.Clear();
            _rivalCheckpointStatus.Clear();
            _finishedParticipants.Clear();

            _records.Clear();
            _ghost?.Delete();
            _ghost = null;
        }

        private void CloseMissionPassedScreen(bool reset)
        {
            Game.FadeScreenOut(1000);
            Wait(1000);
            Function.Call(Hash._STOP_SCREEN_EFFECT, "HeistCelebPass");
            if (reset)
                Game.Player.Character.Position = _currentRace.Trigger + new Vector3(4f, 0f, 0f);
            else if (Game.Player.Character.IsInVehicle())
                Game.Player.Character.CurrentVehicle.HandbrakeOn = false;
            Game.Player.CanControlCharacter = true;
            World.RenderingCamera = null;
            EndRace(reset);
            _passed = null;
            Game.FadeScreenIn(1500);
            AddRacesBlips();
        }

        public void OnTick(object sender, EventArgs e)
        {
            Function.Call((Hash)0xF8DEE0A5600CBB93, true); // SET_MINIMAP_REVEALED

            if (Config.CayoPericoLoader && Function.Call<int>(Hash.GET_INTERIOR_FROM_ENTITY, Game.Player.Character) == 0)
            {
                Function.Call(Hash.SET_RADAR_AS_EXTERIOR_THIS_FRAME);
                Function.Call(Hash.SET_RADAR_AS_INTERIOR_THIS_FRAME, 0xc0a90510, 4700.0f, -5145.0f, 0, 0);
            }

            if (Environment.TickCount >= _lasttime + 1000)
            {
                _seconds++;
                _lasttime = Environment.TickCount;
                if (_isInRace)
                {
                    if (_countdown > 0)
                    {
                        var screen = UIMenu.GetScreenResolutionMaintainRatio();
                        var w = Convert.ToInt32(screen.Width / 2);
                        _countdown--;
                        if (_countdown > 3) return;
                        _fadeoutSprite = new Sprite("mpinventory", "in_world_circle", new Point(w - 125, 200), new Size(250, 250), 0f, _countdown == 0 ? Color.FromArgb(49, 235, 126) : Color.FromArgb(241, 247, 57));
                        Function.Call(Hash.REQUEST_SCRIPT_AUDIO_BANK, "HUD_MINI_GAME_SOUNDSET", true);
                        Function.Call(Hash.PLAY_SOUND_FRONTEND, 0, "CHECKPOINT_NORMAL", "HUD_MINI_GAME_SOUNDSET");
                        if (_countdown == 0)
                        {
                            _participants.ForEach(car => car.FreezePosition = false);
                            _missionStart = _seconds;
                        }
                    }
                    else if (_countdown == 0) _countdown = -1;
                }
            }

            GUI.MainMenu.ProcessControl();
            GUI.MainMenu.ProcessMouse();
            GUI.MainMenu.Draw();

            _quitMenu.ProcessControl();
            _quitMenu.ProcessMouse();
            _quitMenu.Draw();

            GUI.DrawSettings(_previewRace, _previewVehicle);

            _passed?.Draw();

            if (_countdown > -1 && _countdown <= 3)
            {
                var screen = UIMenu.GetScreenResolutionMaintainRatio();
                var w = Convert.ToInt32(screen.Width / 2);
                new UIResText(_countdown == 0 ? "GO" : _countdown.ToString(), new Point(w, 260), 2f, Color.White, Font.Pricedown, UIResText.Alignment.Centered).Draw();
            }

            if (_fadeoutSprite?.Color.A > 5)
            {
                _fadeoutSprite.Color = Color.FromArgb(_fadeoutSprite.Color.A - 5, _fadeoutSprite.Color.R, _fadeoutSprite.Color.G,
                    _fadeoutSprite.Color.B);
                _fadeoutSprite.Draw();
            }

            if (!_isInRace)
            {
                if (GUI.IsInMenu) return;
                foreach (var race in _races)
                {
                    if (!Game.Player.Character.IsInRangeOf(race.Trigger, 50f)) continue;
                    World.DrawMarker(MarkerType.VerticalCylinder, race.Trigger, new Vector3(0, 0, 0), new Vector3(0, 0, 0), new Vector3(5f, 5f, 1f), Color.FromArgb(200, 255, 255, 255));
                    if (!Game.Player.Character.IsInRangeOf(race.Trigger, 10f)) continue;
                    var tmpSF = new Scaleform("PLAYER_NAME_01");
                    tmpSF.CallFunction("SET_PLAYER_NAME", race.Name);

                    tmpSF.Render3D(race.Trigger + new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, _oldAngle), new Vector3(12, 6, 2));

                    var tmpT = new Scaleform("PLAYER_NAME_02");
                    tmpT.CallFunction("SET_PLAYER_NAME", "Community Race");

                    tmpT.Render3D(race.Trigger + new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, _oldAngle), new Vector3(6, 3, 1));

                    _oldAngle += 2f;

                    if (!Game.Player.Character.IsInRangeOf(race.Trigger, 5f)) continue;

                    Function.Call(Hash._SET_TEXT_COMPONENT_FORMAT, "STRING");
                    Function.Call(Hash._ADD_TEXT_COMPONENT_STRING, "Press ~INPUT_CONTEXT~ to participate in this Community Race.");
                    Function.Call(Hash._0x238FFE5C7B0498A6, 0, 0, 1, -1);

                    if (Game.IsControlJustPressed(0, GTA.Control.Context))
                    {
                        Game.Player.CanControlCharacter = false;
                        Game.Player.Character.Position = race.Trigger + new Vector3(4f, 0f, 0f);
                        using (StreamReader file = new StreamReader(race.Path))
                        {
                            var raceout = (Race)_serializer.Deserialize(file);
                            raceout.FileName = Path.GetFileNameWithoutExtension(race.Path);
                            _previewRace = raceout;
                            BuildMenu(raceout);
                        }
                        GUI.MainMenu.Visible = true;
                        GUI.IsInMenu = true;
                        break;
                    }
                }
            }
            else
            {
                if (!_wanted)
                    Function.Call(Hash.SET_MAX_WANTED_LEVEL, 0);
                if (Game.Player.Character.IsInVehicle())
                    Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)GTA.Control.VehicleExit);
                if (Game.IsControlJustPressed(0, GTA.Control.VehicleExit))
                {
                    _quitMenu.RefreshIndex();
                    _quitMenu.Visible = !_quitMenu.Visible;
                }

                if (!_traffic)
                {
                    Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                    Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                    Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                }

                if (!_peds)
                {
                    Function.Call(Hash.SET_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                    Function.Call(Hash.SET_SCENARIO_PED_DENSITY_MULTIPLIER_THIS_FRAME, 0f, 0f);
                }

                var res = UIMenu.GetScreenResolutionMaintainRatio();
                var safe = UIMenu.GetSafezoneBounds();
                const int interval = 45;
                if (_countdown <= 0)
                {
                    new UIResText("TIME", new Point(Convert.ToInt32(res.Width) - safe.X - 180, Convert.ToInt32(res.Height) - safe.Y - (90 + (1 * interval))), 0.3f, Color.White).Draw();
                    new UIResText(FormatTime(_seconds - _missionStart), new Point(Convert.ToInt32(res.Width) - safe.X - 20, Convert.ToInt32(res.Height) - safe.Y - (102 + (1 * interval))), 0.5f, Color.White, Font.ChaletLondon, UIResText.Alignment.Right).Draw();
                    new Sprite("timerbars", "all_black_bg", new Point(Convert.ToInt32(res.Width) - safe.X - 248, Convert.ToInt32(res.Height) - safe.Y - (100 + (1 * interval))), new Size(250, 37), 0f, Color.FromArgb(200, 255, 255, 255)).Draw();

                    string label, value;
                    if (_currentRivals.Any())
                    {
                        label = "POSITION";
                        value = $"{CalculatePlayerPositionInRace() + 1}/{_currentRivals.Count + 1}";
                    }
                    else
                    {
                        label = "BEST";
                        value = _replay.Time > 0 ? FormatTime(_replay.Time) : "--:--";
                    }
                    new UIResText(label, new Point(Convert.ToInt32(res.Width) - safe.X - 180, Convert.ToInt32(res.Height) - safe.Y - (90 + (2 * interval))), 0.3f, Color.White).Draw();
                    new UIResText(value, new Point(Convert.ToInt32(res.Width) - safe.X - 20, Convert.ToInt32(res.Height) - safe.Y - (102 + (2 * interval))), 0.5f, Color.White, Font.ChaletLondon, UIResText.Alignment.Right).Draw();
                    new Sprite("timerbars", "all_black_bg", new Point(Convert.ToInt32(res.Width) - safe.X - 248, Convert.ToInt32(res.Height) - safe.Y - (100 + (2 * interval))), new Size(250, 37), 0f, Color.FromArgb(200, 255, 255, 255)).Draw();

                    if (_laps > 1)
                    {
                        int playerCheckpoint = _currentRace.Checkpoints.Length - _checkpoints.Count;
                        int currentLap = Convert.ToInt32(Math.Floor(playerCheckpoint / (decimal)_totalLaps)) + 1;

                        new UIResText("LAP", new Point(Convert.ToInt32(res.Width) - safe.X - 180, Convert.ToInt32(res.Height) - safe.Y - (90 + (3 * interval))), 0.3f, Color.White).Draw();
                        new UIResText(currentLap + "/" + _laps, new Point(Convert.ToInt32(res.Width) - safe.X - 20, Convert.ToInt32(res.Height) - safe.Y - (102 + (3 * interval))), 0.5f, Color.White, Font.ChaletLondon, UIResText.Alignment.Right).Draw();
                        new Sprite("timerbars", "all_black_bg", new Point(Convert.ToInt32(res.Width) - safe.X - 248, Convert.ToInt32(res.Height) - safe.Y - (100 + (3 * interval))), new Size(250, 37), 0f, Color.FromArgb(200, 255, 255, 255)).Draw();
                    }

                    if (Environment.TickCount >= _lastRecord + 100)
                    {
                        _lastRecord = Environment.TickCount;
                        _records.Add(new Record(_currentVehicle.Position, _currentVehicle.Quaternion, _currentVehicle.Velocity, _currentVehicle.Speed));
                        _ghost?.NextRecord();
                    }
                    _ghost?.Update();
                }

                for (int i = 0; i < _rivalCheckpointStatus.Count; i++)
                {
                    Tuple<Rival, int> tuple = _rivalCheckpointStatus[i];
                    if (tuple.Item1.Vehicle.IsInRangeOf(_currentRace.Checkpoints[tuple.Item2], 50f))
                    {
                        tuple.Item1.Character.Task.ClearAll();
                        if (_currentRace.Checkpoints.Length <= tuple.Item2 + 1)
                        {
                            if (!_finishedParticipants.Contains(tuple.Item1))
                                _finishedParticipants.Add(tuple.Item1);
                            tuple.Item1.Vehicle.HandbrakeOn = true;
                            continue;
                        }
                        _rivalCheckpointStatus[i] = new Tuple<Rival, int>(tuple.Item1, tuple.Item2 + 1);
                        Function.Call(Hash.TASK_VEHICLE_MISSION_COORS_TARGET, tuple.Item1.Character.Handle, tuple.Item1.Vehicle.Handle,
                            _currentRace.Checkpoints[tuple.Item2 + 1].X, _currentRace.Checkpoints[tuple.Item2 + 1].Y,
                            _currentRace.Checkpoints[tuple.Item2 + 1].Z, Mode, 200f, Rival.MainDrivingStyle, 5f, 0f, 0); // TODO: Debuggin // old - 6
                    }
                }

                World.DrawMarker(MarkerType.VerticalCylinder, _checkpoints[0], new Vector3(0, 0, 0), new Vector3(0, 0, 0), new Vector3(10f, 10f, 2f), Color.FromArgb(100, 241, 247, 57));
                if (_nextBlip == null)
                    _nextBlip = World.CreateBlip(_checkpoints[0]);
                if (_checkpoints.Count >= 2)
                {
                    if (_secondBlip == null)
                    {
                        _secondBlip = World.CreateBlip(_checkpoints[1]);
                        _secondBlip.Scale = 0.5f;
                        if (_checkpoints.Count == 2)
                            _secondBlip.Sprite = BlipSprite.RaceFinish;
                    }
                    Vector3 dir = _checkpoints[1] - _checkpoints[0];
                    dir.Normalize();
                    World.DrawMarker(MarkerType.ChevronUpx1, _checkpoints[0] + new Vector3(0f, 0f, 2f), dir, new Vector3(60f, 0f, 0f), new Vector3(4f, 4f, 4f), Color.FromArgb(200, 87, 193, 250));
                }
                else
                {
                    Vector3 dir = Game.Player.Character.Position - _checkpoints[0];
                    dir.Normalize();
                    World.DrawMarker(MarkerType.CheckeredFlagRect, _checkpoints[0] + new Vector3(0f, 0f, 2f), dir, new Vector3(0f, 0f, 0f), new Vector3(4f, 4f, 4f), Color.FromArgb(200, 87, 193, 250));
                    _nextBlip.Sprite = BlipSprite.RaceFinish;
                }

                if (Game.Player.Character.IsInVehicle() && Game.Player.Character.IsInRangeOf(_checkpoints[0], 10f))
                {
                    Function.Call(Hash.REQUEST_SCRIPT_AUDIO_BANK, "HUD_MINI_GAME_SOUNDSET", true);
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, 0, "CHECKPOINT_NORMAL", "HUD_MINI_GAME_SOUNDSET");
                    _respawnPoint.Position = _checkpoints[0];
                    _respawnPoint.Heading = Game.Player.Character.Heading;
                    _checkpoints.RemoveAt(0);
                    _nextBlip?.Remove();
                    _secondBlip?.Remove();
                    _nextBlip = null;
                    _secondBlip = null;
                    if (_checkpoints.Count == 0)
                    {
                        Game.Player.CanControlCharacter = false;
                        Function.Call(Hash._START_SCREEN_EFFECT, "HeistCelebPass", 0, true);
                        if (Game.Player.Character.IsInVehicle())
                            Game.Player.Character.CurrentVehicle.HandbrakeOn = true;
                        World.DestroyAllCameras();
                        World.RenderingCamera = World.CreateCamera(GameplayCamera.Position, GameplayCamera.Rotation, 60f);
                        Function.Call(Hash.PLAY_SOUND_FRONTEND, 0, "CHECKPOINT_UNDER_THE_BRIDGE", "HUD_MINI_GAME_SOUNDSET");
                        int position = _finishedParticipants.Count + 1;
                        int peoplecount = _currentRivals.Count + 1;
                        int score = 100 - ((position - 1) * 10);
                        if (score < 0)
                            score = 0;
                        _passed = new MissionPassedScreen(_currentRace.Name, score, score > 50 ? score > 90 ? MissionPassedScreen.Medal.Gold : MissionPassedScreen.Medal.Silver : MissionPassedScreen.Medal.Bronze);
                        _passed.AddItem("Time Elapsed", FormatTime(_seconds - _missionStart), MissionPassedScreen.TickboxState.None);
                        if (peoplecount > 1)
                        {
                            _passed.AddItem("Position", position + "/" + peoplecount, position == 1 ? MissionPassedScreen.TickboxState.Tick : MissionPassedScreen.TickboxState.Empty);
                            if (position <= 3)
                            {
                                var reward = position == 1 ? 50000 : position == 2 ? 30000 : 10000;
                                _passed.AddItem("Reward", $"${reward}", MissionPassedScreen.TickboxState.None);
                                Game.Player.Money += reward;
                            }
                        }
                        if (_replay.Time == 0 || _seconds - _missionStart < _replay.Time)
                        {
                            _passed.AddItem("New Record!", "", MissionPassedScreen.TickboxState.Tick);
                            if (!Directory.Exists("scripts\\Races\\Replays"))
                                Directory.CreateDirectory("scripts\\Races\\Replays");
                            using (StreamWriter file = File.CreateText("scripts\\Races\\Replays\\" + _currentRace.FileName + ".json"))
                                new JsonSerializer().Serialize(file, new Replay(_seconds - _missionStart, (uint)_vehicleHash, _records.ToArray()));
                        }
                        _passed.OnContinueHit += () =>
                        {
                            CloseMissionPassedScreen(true);
                        };
                        _passed.OnCancelHit += () =>
                        {
                            CloseMissionPassedScreen(false);
                        };
                        _passed.Show();
                        _isInRace = false;
                    }
                }
            }

            if (_ghost != null && Config.Collision == false)
            {
                foreach (Vehicle vehicle in World.GetNearbyVehicles(_ghost.Vehicle.Position, 50f))
                    if (vehicle != _ghost.Vehicle)
                    {
                        Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, _ghost.Vehicle.Handle, vehicle.Handle, false);
                        Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, vehicle.Handle, _ghost.Vehicle.Handle, false);
                    }
                foreach (Ped ped in World.GetNearbyPeds(_ghost.Vehicle.Position, 50f))
                    if (ped != _ghost.Ped)
                    {
                        Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, _ghost.Vehicle.Handle, ped.Handle, false);
                        Function.Call(Hash.SET_ENTITY_NO_COLLISION_ENTITY, ped.Handle, _ghost.Vehicle.Handle, false);
                    }
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Config.TeleportKey)
                if (Game.IsWaypointActive)
                    Helpers.Teleport(World.GetWaypointPosition());
                else
                    UI.Notify("Select teleport destination");
        }

        private void OnAbort(object sender, EventArgs e)
        {
            Tick -= OnTick;
            KeyDown -= OnKeyDown;
            EndRace(true);
            RemoveRacesBlips();
            _races.Clear();
            if (Config.CayoPericoLoader)
                _island.Remove();
        }

        public string FormatTime(uint seconds)
        {
            var minutes = Convert.ToInt32(Math.Floor(seconds / 60f));
            var secs = seconds % 60;
            return string.Format("{0:00}:{1:00}", minutes, secs);
        }

        public void SaveMap(Map map, string filename)
        {
            if (!filename.EndsWith(".xml"))
                filename += ".xml";
            Race tmpRace = new Race
            {
                AvailableVehicles =
                    map.Objects.Where(obj => obj.Type == ObjectTypes.Vehicle)
                        .Select(obj => (VehicleHash)obj.Hash)
                        .Distinct()
                        .ToArray(),
                Checkpoints = map.Markers.Select(mar => mar.Position).ToArray()
            };
            var props = map.Objects.Where(obj => obj.Type == ObjectTypes.Prop).ToArray();
            SavedProp[] tmpProps = new SavedProp[props.Length];
            for (int i = 0; i < props.Length; i++)
            {
                tmpProps[i] = new SavedProp()
                {
                    Dynamic = props[i].Dynamic,
                    Hash = props[i].Hash,
                    Position = props[i].Position,
                    Rotation = props[i].Rotation,
                    Texture = props[i].Texture
                };
            }
            tmpRace.DecorativeProps = tmpProps;
            tmpRace.Trigger = map.Objects.First(obj => obj.Type == ObjectTypes.Ped).Position - new Vector3(0f, 0f, 1f);
            tmpRace.SpawnPoints = map.Objects.Where(obj => obj.Type == ObjectTypes.Vehicle).Select(obj => new SpawnPoint() { Position = obj.Position, Heading = obj.Rotation.Z }).ToArray();
            tmpRace.Name = "Nameless Map";
            tmpRace.Description = "Cool race!";

            if (!Directory.Exists("scripts\\Races"))
                Directory.CreateDirectory("scripts\\Races");
            using (StreamWriter writer = new StreamWriter("scripts\\Races\\" + filename))
                _serializer.Serialize(writer, tmpRace);
            UI.Notify("~b~~h~Community Races~h~~n~~w~Race saved as ~h~" + filename + "~h~!");
            UI.Notify("Don't forget to include your name and the map description in the file!");
        }

        public void BuildMenu(Race race)
        {
            GUI.MainMenu.Clear();
            GUI.MainMenu.SetBannerType(new UIResRectangle());

            List<dynamic> vehicles = new List<dynamic>();
            race.AvailableVehicles.ToList().ForEach(x => vehicles.Add(x.ToString()));
            int selectedVehicle = vehicles.IndexOf(Config.Vehicle);
            if (selectedVehicle == -1) selectedVehicle = 0;
            _vehicleHash = race.AvailableVehicles[selectedVehicle];
            _previewVehicle = World.CreateVehicle(Helpers.RequestModel((int)_vehicleHash), race.Trigger);
            SetVehicleColors();
            _previewVehicle.IsPersistent = false;

            List<dynamic> timeList = new List<dynamic> { "Current", "Sunrise", "Day", "Sunset", "Night" };
            var timeItem = new UIMenuListItem("Time of Day", timeList, timeList.IndexOf(Config.Time));
            timeItem.OnListChanged += (item, index) =>
            {
                Config.Time = item.Items[index].ToString();
            };

            var weatherList = new List<dynamic> { "Current" };
            Enum.GetNames(typeof(Weather)).ToList().ForEach(w => weatherList.Add(w));
            var weatherItem = new UIMenuListItem("Weather", weatherList, weatherList.IndexOf(Config.Weather));
            weatherItem.OnListChanged += (item, index) =>
            {
                Config.Weather = item.Items[index].ToString();
            };

            var copItem = new UIMenuCheckboxItem("Wanted Levels", Config.Wanted);
            copItem.CheckboxEvent += (i, checkd) =>
            {
                Config.Wanted = checkd;
            };

            var opponentsList = new List<dynamic> { "Random", "Ghost", "None" };
            Enumerable.Range(1, race.SpawnPoints.Length - 1).ToList().ForEach(n => opponentsList.Add(n.ToString()));
            if (!opponentsList.Contains(Config.Opponents))
                Config.Opponents = opponentsList.Last().ToString();
            var opponentsItem = new UIMenuListItem("Opponents", opponentsList, opponentsList.IndexOf(Config.Opponents));
            opponentsItem.OnListChanged += (item, index) =>
            {
                Config.Opponents = item.Items[index].ToString();
            };

            var trafficItem = new UIMenuCheckboxItem("Traffic", Config.Traffic);
            trafficItem.CheckboxEvent += (i, checkd) =>
            {
                Config.Traffic = checkd;
            };

            var pedsItem = new UIMenuCheckboxItem("Pedestrians", Config.Peds);
            pedsItem.CheckboxEvent += (i, checkd) =>
            {
                Config.Peds = checkd;
            };

            var carItem = new UIMenuListItem("Vehicle", vehicles, selectedVehicle);
            carItem.OnListChanged += (item, index) =>
            {
                _vehicleHash = race.AvailableVehicles[index];
                _previewVehicle?.Delete();
                _previewVehicle = World.CreateVehicle(Helpers.RequestModel((int)_vehicleHash), race.Trigger);
                if (_previewVehicle == null) return;
                SetVehicleColors();
                _previewVehicle.IsPersistent = false;
                Config.Vehicle = _vehicleHash.ToString();
            };

            List<dynamic> colors = new List<dynamic> { "Random" };
            Enum.GetValues(typeof(VehicleColor)).Cast<VehicleColor>().ToList().ForEach(c => colors.Add(c));
            int selectedColor = 0;
            if (Enum.TryParse(Config.PrimaryColor, out VehicleColor color))
                selectedColor = colors.IndexOf(color);
            var primaryColorItem = new UIMenuListItem("Color 1", colors, selectedColor);
            primaryColorItem.OnListChanged += (item, index) =>
            {
                if (index > 0)
                {
                    _vehiclePrimaryColor = (VehicleColor)item.Items[index];
                    _previewVehicle.PrimaryColor = _vehiclePrimaryColor;
                }
                Config.PrimaryColor = item.Items[index].ToString();
            };
            selectedColor = 0;
            if (Enum.TryParse(Config.SecondaryColor, out color))
                selectedColor = colors.IndexOf(color);
            var secondaryColorItem = new UIMenuListItem("Color 2", colors, selectedColor);
            secondaryColorItem.OnListChanged += (item, index) =>
            {
                if (index > 0)
                {
                    _vehicleSecondaryColor = (VehicleColor)item.Items[index];
                    _previewVehicle.SecondaryColor = _vehicleSecondaryColor;
                    _previewVehicle.RimColor = _vehicleSecondaryColor;
                    _previewVehicle.DashboardColor = _vehicleSecondaryColor;
                }
                Config.SecondaryColor = item.Items[index].ToString();
            };

            SetRadioStation();
            var radioList = new List<dynamic> { "Random" };
            Enum.GetValues(typeof(RadioStation)).Cast<RadioStation>().ToList().ForEach(r => { if (r != RadioStation.Unknown) radioList.Add(r); });
            int selectedRadio = 0;
            if (Enum.TryParse(Config.Radio, out RadioStation radio))
                selectedRadio = radioList.IndexOf(radio);
            var radioItem = new UIMenuListItem("Radio", radioList, selectedRadio);
            radioItem.OnListChanged += (item, index) =>
            {
                Config.Radio = item.Items[index].ToString();
                SetRadioStation();
            };

            var confimItem = new UIMenuItem("Start Race");
            confimItem.Activated += (item, index) =>
            {
                GUI.MainMenu.Visible = false;
                GUI.IsInMenu = false;
                Game.Player.CanControlCharacter = true;
                World.RenderingCamera = null;
                RemoveRacesBlips();
                StartRace(race);
            };

            GUI.MainMenu.OnMenuClose += menu =>
            {
                World.RenderingCamera = null;
                GUI.IsInMenu = false;
                Game.Player.CanControlCharacter = true;
                _previewVehicle?.Delete();
            };

            GUI.MainMenu.AddItem(timeItem);
            GUI.MainMenu.AddItem(weatherItem);
            GUI.MainMenu.AddItem(copItem);
            GUI.MainMenu.AddItem(trafficItem);
            GUI.MainMenu.AddItem(pedsItem);
            GUI.MainMenu.AddItem(carItem);
            GUI.MainMenu.AddItem(primaryColorItem);
            GUI.MainMenu.AddItem(secondaryColorItem);
            GUI.MainMenu.AddItem(radioItem);
            GUI.MainMenu.AddItem(opponentsItem);
            if (race.LapsAvailable)
            {
                var lapList = new List<dynamic>();
                Enumerable.Range(1, 20).ToList().ForEach(n => lapList.Add(n));
                var lapItem = new UIMenuListItem("Laps", lapList, lapList.IndexOf(Config.Laps));
                lapItem.OnListChanged += (item, index) =>
                {
                    Config.Laps = (int)item.Items[index];
                };
                GUI.MainMenu.AddItem(lapItem);
            }
            GUI.MainMenu.AddItem(confimItem);
            GUI.MainMenu.RefreshIndex();
            GUI.MainMenu.CurrentSelection = race.LapsAvailable ? 11 : 10;
        }

        private void SetVehicleColors()
        {
            if (Enum.TryParse(Config.PrimaryColor, out VehicleColor color))
            {
                _vehiclePrimaryColor = color;
                _previewVehicle.PrimaryColor = color;
            }
            else _vehiclePrimaryColor = _previewVehicle.PrimaryColor;

            if (Enum.TryParse(Config.SecondaryColor, out color))
            {
                _vehicleSecondaryColor = color;
                _previewVehicle.SecondaryColor = color;
                _previewVehicle.RimColor = color;
                _previewVehicle.DashboardColor = color;
            }
            else _vehicleSecondaryColor = _previewVehicle.SecondaryColor;
        }

        private void SetRadioStation()
        {
            if (Enum.TryParse(Config.Radio, out RadioStation radio))
                Game.RadioStation = radio;
        }
    }
}
