using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private uint _seconds;
        private int _totalLaps;
        private float _oldAngle;
        private readonly UIMenu _quitMenu;
        private Race _previewRace;
        private Race _currentRace;
        private Sprite _fadeoutSprite;
        private Vehicle _previewVehicle;
        private Vehicle _currentVehicle;
        private VehicleHash _vehicleHash;
        private VehicleColor _vehicleColor;
        private MissionPassedScreen _passed;
        private int _lasttime = Environment.TickCount;
        private List<Vector3> _checkpoints = new List<Vector3>();
        private readonly List<Record> _records = new List<Record>();
        private int _lastRecord = Environment.TickCount;
        private Replay _replay;
        private Ghost _ghost;

        private readonly List<RaceBlip> _races = new List<RaceBlip>();
        private readonly List<Entity> _cleanupBag = new List<Entity>();
        private readonly List<Vehicle> _participants = new List<Vehicle>();
        private readonly List<Rival> _currentRivals = new List<Rival>();
        private readonly List<Rival> _finishedParticipants = new List<Rival>();
        private readonly List<Tuple<Rival, int>> _rivalCheckpointStatus = new List<Tuple<Rival, int>>();
        private readonly Dictionary<string, dynamic> _raceSettings = new Dictionary<string, dynamic>();

        private readonly XmlSerializer _serializer = new XmlSerializer(typeof(Race));

        private const int Mode = 4;

        public CommunityRaces()
        {
            Tick += OnTick;
            Aborted += OnAbort;
            LoadRaces();

            _quitMenu = new UIMenu("", "~r~ARE YOU SURE YOU WANT TO QUIT?", new Point(0, -107));
            var qitem = new UIMenuItem("Quit current race.");
            qitem.Activated += (item, index) =>
            {
                if (_currentRace == null) return;
                _quitMenu.Visible = false;
                Game.FadeScreenOut(500);
                Wait(1000);
                Game.Player.Character.Position = _currentRace.Trigger;
                EndRace();
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
        }

        private void AddRacesBlips()
        {
            foreach (RaceBlip race in _races)
                race.Add();
        }

        private void RemoveRacesBlips()
        {
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
                    var raceout = (Race)_serializer.Deserialize(file);
                    _races.Add(new RaceBlip(raceout.Name, path, raceout.Trigger));
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
            _records.Clear();
            _replay = new Replay(0, new Record[0]);
            _ghost = null;
            var fileName = "scripts\\Races\\Replays\\" + race.FileName + ".json";
            if (File.Exists(fileName))
                using (StreamReader reader = new StreamReader(fileName))
                    using (JsonTextReader jsonReader = new JsonTextReader(reader))
                        _replay = new JsonSerializer().Deserialize<Replay>(jsonReader);

            Game.FadeScreenOut(500);
            Wait(500);
            _isInRace = true;
            _currentRace = race;
            if (_raceSettings["Laps"] > 1)
            {
                _totalLaps = race.Checkpoints.Length;
                List<Vector3> tmpCheckpoints = new List<Vector3>();
                for (int i = 0; i < _raceSettings["Laps"]; i++)
                {
                    tmpCheckpoints.AddRange(race.Checkpoints);
                }
                _currentRace.Checkpoints = tmpCheckpoints.ToArray();
            }

            if (_raceSettings["Weather"] != "Current")
            {
                Enum.TryParse(_raceSettings["Weather"], out Weather wout);
                World.Weather = wout;
            }

            switch ((string)_raceSettings["TOD"])
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

            _currentVehicle?.Delete();
            _currentVehicle = World.CreateVehicle(Helpers.RequestModel((int)_vehicleHash), spawn.Position, spawn.Heading);
            _currentVehicle.PrimaryColor = _vehicleColor;
            _currentVehicle.SecondaryColor = _vehicleColor;
            Function.Call(Hash.SET_PED_INTO_VEHICLE, Game.Player.Character.Handle, _currentVehicle.Handle, (int)VehicleSeat.Driver);
            _currentVehicle.IsPersistent = false;
            _currentVehicle.FreezePosition = true;

            int spawnlen = 0;
            switch (_raceSettings["Opponents"].ToString())
            {
                case "Random":
                    spawnlen = RandGen.Next(1, race.SpawnPoints.Length - 1);
                    break;
                case "Ghost":
                    if (_replay.Records.Length > 0)
                        _ghost = new Ghost(_replay.Records.ToList(), _vehicleHash);
                    break;
                case "None":
                    break;
                default:
                    spawnlen = Convert.ToInt32(_raceSettings["Opponents"]);
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
                _cleanupBag.Add(tmpProp);
            }

            _checkpoints = race.Checkpoints.ToList();
            _missionStart = _seconds;
            Game.FadeScreenIn(500);
            Wait(500);
            _countdown = 5;
            _participants.Add(_currentVehicle);
            _cleanupBag.Add(_currentVehicle);

            if (!Convert.ToBoolean(_raceSettings["Traffic"]))
                Function.Call(Hash.CLEAR_AREA_OF_VEHICLES, spawn.Position.X, spawn.Position.Y, spawn.Position.Z, 1000f, 0);
        }

        private void EndRace()
        {
            _isInRace = false;
            _currentRace = null;

            _secondBlip?.Remove();
            _nextBlip?.Remove();
            _checkpoints.Clear();
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
        }

        public void OnTick(object sender, EventArgs e)
        {
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

                    if (Game.IsControlJustPressed(0, Control.Context))
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
                if (!_raceSettings["Wanted"])
                    Function.Call(Hash.SET_MAX_WANTED_LEVEL, 0);
                if (Game.Player.Character.IsInVehicle())
                    Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, (int)Control.VehicleExit);
                if (Game.IsControlJustPressed(0, Control.VehicleExit))
                {
                    _quitMenu.RefreshIndex();
                    _quitMenu.Visible = !_quitMenu.Visible;
                }

                if (!Convert.ToBoolean(_raceSettings["Traffic"]))
                {
                    Function.Call(Hash.SET_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                    Function.Call(Hash.SET_RANDOM_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                    Function.Call(Hash.SET_PARKED_VEHICLE_DENSITY_MULTIPLIER_THIS_FRAME, 0f);
                }

                var res = UIMenu.GetScreenResolutionMaintainRatio();
                var safe = UIMenu.GetSafezoneBounds();
                const int interval = 45;
                if (_countdown <= 0)
                {
                    new UIResText("TIME", new Point(Convert.ToInt32(res.Width) - safe.X - 180, Convert.ToInt32(res.Height) - safe.Y - (90 + (1 * interval))), 0.3f, Color.White).Draw();
                    new UIResText(FormatTime((int)unchecked(_seconds - _missionStart)), new Point(Convert.ToInt32(res.Width) - safe.X - 20, Convert.ToInt32(res.Height) - safe.Y - (102 + (1 * interval))), 0.5f, Color.White, Font.ChaletLondon, UIResText.Alignment.Right).Draw();
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

                    if (_raceSettings["Laps"] > 1)
                    {
                        int playerCheckpoint = _currentRace.Checkpoints.Length - _checkpoints.Count;
                        int currentLap = Convert.ToInt32(Math.Floor(playerCheckpoint / (decimal)_totalLaps)) + 1;

                        new UIResText("LAP", new Point(Convert.ToInt32(res.Width) - safe.X - 180, Convert.ToInt32(res.Height) - safe.Y - (90 + (3 * interval))), 0.3f, Color.White).Draw();
                        new UIResText(currentLap + "/" + _raceSettings["Laps"], new Point(Convert.ToInt32(res.Width) - safe.X - 20, Convert.ToInt32(res.Height) - safe.Y - (102 + (3 * interval))), 0.5f, Color.White, Font.ChaletLondon, UIResText.Alignment.Right).Draw();
                        new Sprite("timerbars", "all_black_bg", new Point(Convert.ToInt32(res.Width) - safe.X - 248, Convert.ToInt32(res.Height) - safe.Y - (100 + (3 * interval))), new Size(250, 37), 0f, Color.FromArgb(200, 255, 255, 255)).Draw();
                    }

                    if (Environment.TickCount >= _lastRecord + 20)
                    {
                        _lastRecord = Environment.TickCount;
                        _records.Add(new Record(_currentVehicle.Position, _currentVehicle.Quaternion, _currentVehicle.Velocity, _currentVehicle.Speed));
                        _ghost?.Update();
                    }
                }

                for (int i = 0; i < _rivalCheckpointStatus.Count; i++)
                {
                    Tuple<Rival, int> tuple = _rivalCheckpointStatus[i];
                    if (tuple.Item1.Vehicle.IsInRangeOf(_currentRace.Checkpoints[tuple.Item2], 20f))
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
                        _passed.AddItem("Time Elapsed", FormatTime((int)unchecked(_seconds - _missionStart)), MissionPassedScreen.TickboxState.None);
                        _passed.AddItem("Position", position + "/" + peoplecount, position == 1 ? MissionPassedScreen.TickboxState.Tick : MissionPassedScreen.TickboxState.Empty);
                        _passed.OnContinueHit += () =>
                        {
                            Game.FadeScreenOut(1000);
                            Wait(1000);
                            Function.Call(Hash._STOP_SCREEN_EFFECT, "HeistCelebPass");
                            Game.Player.Character.Position = _currentRace.Trigger;
                            Game.Player.CanControlCharacter = true;
                            World.RenderingCamera = null;
                            EndRace();
                            _passed = null;
                            Game.FadeScreenIn(1500);
                            AddRacesBlips();
                        };
                        _passed.Show();
                        _isInRace = false;

                        if (_replay.Time == 0 || _seconds - _missionStart < _replay.Time)
                        {
                            if (!Directory.Exists("scripts\\Races\\Replays"))
                                Directory.CreateDirectory("scripts\\Races\\Replays");

                            using (StreamWriter file = File.CreateText("scripts\\Races\\Replays\\" + _currentRace.FileName + ".json"))
                                new JsonSerializer().Serialize(file, new Replay((int)(_seconds - _missionStart), _records.ToArray()));
                        }
                    }
                }
            }

            if (_ghost != null)
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

        private void OnAbort(object sender, EventArgs e)
        {
            Tick -= OnTick;
            EndRace();
            RemoveRacesBlips();
            _races.Clear();
        }

        public string FormatTime(int seconds)
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
            _raceSettings.Clear();

            _raceSettings["TOD"] = "Current";
            _raceSettings["Weather"] = "Current";
            _raceSettings["Wanted"] = false;
            _raceSettings["Opponents"] = "Random";
            _raceSettings["Traffic"] = true;
            _raceSettings["Laps"] = 1;

            _vehicleHash = race.AvailableVehicles[0];
            _previewVehicle = World.CreateVehicle(Helpers.RequestModel((int)_vehicleHash), race.Trigger);
            _vehicleColor = _previewVehicle.PrimaryColor;
            _previewVehicle.IsPersistent = false;

            List<dynamic> timeList = new List<dynamic> { "Current", "Sunrise", "Day", "Sunset", "Night" };
            var timeItem = new UIMenuListItem("Time of Day", timeList, 0);
            timeItem.OnListChanged += (item, index) =>
            {
                _raceSettings["TOD"] = item.Items[index];
            };

            var weatherList = new List<dynamic> { "Current" };
            Enum.GetNames(typeof(Weather)).ToList().ForEach(w => weatherList.Add(w));
            var weatherItem = new UIMenuListItem("Weather", weatherList, 0);
            weatherItem.OnListChanged += (item, index) =>
            {
                _raceSettings["Weather"] = item.Items[index];
            };

            var copItem = new UIMenuCheckboxItem("Wanted Levels", false);
            copItem.CheckboxEvent += (i, checkd) =>
            {
                _raceSettings["Wanted"] = checkd;
            };

            var opponentsList = new List<dynamic> { "Random", "Ghost", "None" };
            Enumerable.Range(1, race.SpawnPoints.Length - 1).ToList().ForEach(n => opponentsList.Add(n));
            var opponentsItem = new UIMenuListItem("Opponents", opponentsList, 0);
            opponentsItem.OnListChanged += (item, index) =>
            {
                _raceSettings["Opponents"] = item.Items[index];
            };

            var trafficItem = new UIMenuCheckboxItem("Traffic", true);
            trafficItem.CheckboxEvent += (i, checkd) =>
            {
                _raceSettings["Traffic"] = checkd;
            };

            List<dynamic> tmpList = new List<dynamic>();
            race.AvailableVehicles.ToList().ForEach(x => tmpList.Add(x));
            var carItem = new UIMenuListItem("Vehicle", tmpList, 0);
            carItem.OnListChanged += (item, index) =>
            {
                Enum.TryParse(item.Items[index].ToString(), out _vehicleHash);
                _previewVehicle?.Delete();
                _previewVehicle = World.CreateVehicle(Helpers.RequestModel((int)_vehicleHash), race.Trigger);
                if (_previewVehicle == null) return;
                _previewVehicle.PrimaryColor = _vehicleColor;
                _previewVehicle.SecondaryColor = _vehicleColor;
                _previewVehicle.IsPersistent = false;
            };

            List<dynamic> colors = new List<dynamic>
            {
                VehicleColor.MatteYellow,
                VehicleColor.Orange,
                VehicleColor.MatteRed,
                VehicleColor.HotPink,
                VehicleColor.MattePurple,
                VehicleColor.MatteDarkBlue,
                VehicleColor.Blue,
                VehicleColor.EpsilonBlue,
                VehicleColor.MatteLimeGreen,
                VehicleColor.Green,
            };
            var colorItem = new UIMenuListItem("Color", colors, 0);
            colorItem.OnListChanged += (ite, index) =>
            {
                Enum.TryParse(ite.Items[index].ToString(), out _vehicleColor);
                _previewVehicle.PrimaryColor = _vehicleColor;
                _previewVehicle.SecondaryColor = _vehicleColor;
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
            GUI.MainMenu.AddItem(carItem);
            GUI.MainMenu.AddItem(colorItem);
            GUI.MainMenu.AddItem(opponentsItem);
            GUI.MainMenu.AddItem(trafficItem);
            if (race.LapsAvailable)
            {
                var lapList = new List<dynamic>();
                Enumerable.Range(1, 20).ToList().ForEach(n => lapList.Add(n));
                var lapItem = new UIMenuListItem("Laps", lapList, 0);
                lapItem.OnListChanged += (item, index) =>
                {
                    _raceSettings["Laps"] = item.Items[index];
                };
                GUI.MainMenu.AddItem(lapItem);
            }
            GUI.MainMenu.AddItem(confimItem);
            GUI.MainMenu.RefreshIndex();
        }
    }
}
