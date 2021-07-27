using System;
using System.Collections.Generic;
using System.Drawing;
using GTA;
using GTA.Native;
using NativeUI;
using Font = GTA.Font;

namespace CommunityRaces
{
	public delegate void EmptyArgs();

	public class MissionPassedScreen
	{
		public event EmptyArgs OnContinueHit;
		public string Title { get; set; }

		private List<Tuple<string, string, TickboxState>> _items = new List<Tuple<string, string, TickboxState>>();
		private int _completionRate;
		private Medal _medal;

		public bool Visible { get; set; }

		public MissionPassedScreen(string title, int completionRate, Medal medal)
		{
			Title = title;
			_completionRate = completionRate;
			_medal = medal;

			Visible = false;
		}

		public void AddItem(string label, string status, TickboxState state)
		{
			_items.Add(new Tuple<string, string, TickboxState>(label, status, state));
		}

		public void Show()
		{
			Visible = true;
		}

		public void Draw()
		{
			if (!Visible) return;

			SizeF res = UIMenu.GetScreenResolutionMaintainRatio();
			int middle = Convert.ToInt32(res.Width / 2);

			new Sprite("mpentry", "mp_modenotselected_gradient", new Point(0, 10), new Size(Convert.ToInt32(res.Width), 450 + (_items.Count * 40)),
				0f, Color.FromArgb(200, 255, 255, 255)).Draw();

			new UIResText("race completed", new Point(middle, 100), 2.5f, Color.FromArgb(255, 199, 168, 87), Font.Pricedown, UIResText.Alignment.Centered).Draw();

			new UIResText(Title, new Point(middle, 230), 0.5f, Color.White, Font.ChaletLondon, UIResText.Alignment.Centered).Draw();

			new UIResRectangle(new Point(middle - 300, 290), new Size(600, 2), Color.White).Draw();

			for (int i = 0; i < _items.Count; i++)
			{
				new UIResText(_items[i].Item1, new Point(middle - 230, 300 + (40 * i)), 0.35f, Color.White, Font.ChaletLondon, UIResText.Alignment.Left).Draw();
				new UIResText(_items[i].Item2, new Point(_items[i].Item3 == TickboxState.None ? middle + 265 : middle + 230, 300 + (40 * i)), 0.35f, Color.White, Font.ChaletLondon, UIResText.Alignment.Right).Draw();
				if (_items[i].Item3 == TickboxState.None) continue;
				string spriteName = "shop_box_blank";
				switch (_items[i].Item3)
				{
					case TickboxState.Tick:
						spriteName = "shop_box_tick";
						break;
					case TickboxState.Cross:
						spriteName = "shop_box_cross";
						break;
				}
				new Sprite("commonmenu", spriteName, new Point(middle + 230, 290 + (40 * i)), new Size(48, 48)).Draw();
			}
			new UIResRectangle(new Point(middle - 300, 300 + (40 * _items.Count)), new Size(600, 2), Color.White).Draw();

			new UIResText("Completion", new Point(middle - 150, 320 + (40 * _items.Count)), 0.4f).Draw();
			new UIResText(_completionRate + "%", new Point(middle + 150, 320 + (40 * _items.Count)), 0.4f, Color.White, Font.ChaletLondon, UIResText.Alignment.Right).Draw();

			string medalSprite = "bronzemedal";
			switch (_medal)
			{
				case Medal.Silver:
					medalSprite = "silvermedal";
					break;
				case Medal.Gold:
					medalSprite = "goldmedal";
					break;
			}

			new Sprite("mpmissionend", medalSprite, new Point(middle + 150, 320 + (40 * _items.Count)), new Size(32, 32)).Draw();

			var scaleform = new Scaleform("instructional_buttons");
			scaleform.CallFunction("CLEAR_ALL");
			scaleform.CallFunction("TOGGLE_MOUSE_BUTTONS", 0);
			scaleform.CallFunction("CREATE_CONTAINER");

			scaleform.CallFunction("SET_DATA_SLOT", 0, Function.Call<string>(Hash._0x0499D7B09FC9B407, 2, (int)Control.FrontendAccept, 0), "Continue");
			scaleform.CallFunction("DRAW_INSTRUCTIONAL_BUTTONS", -1);
			scaleform.Render2D();
			if (Game.IsControlJustPressed(0, Control.FrontendAccept))
			{
				Game.PlaySound("SELECT", "HUD_FRONTEND_DEFAULT_SOUNDSET");
				ContinueHit();
			}
		}

		public enum Medal
		{
			Bronze,
			Silver,
			Gold
		}

		public enum TickboxState
		{
			None,
			Empty,
			Tick,
			Cross,
		}

		protected virtual void ContinueHit()
		{
			OnContinueHit?.Invoke();
		}
	}
}