using BenMcLean.Wolf3D.Shared.Setup;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BenMcLean.Wolf3D.Shared
{
	public partial class DosScreen : Control
	{
		/// <summary>
		/// For text mode, I would like to make the screen be the same width as a
		/// Wolfenstein 3-D wall. Typical MS-DOS VGA text mode was 720x400 resolution
		/// in a 4:3 aspect ratio showing a 9x16 fixed width font which could be
		/// displayed in 80 columns and 25 rows.
		/// </summary>
		public class VirtualScreenText
		{
			private readonly Queue<string> lines = new Queue<string>();

			public string Text
			{
				get
				{
					StringBuilder stringBuilder = new();
					foreach (string line in lines)
						stringBuilder.Append(line).Append('\n');
					return stringBuilder.ToString();
				}
				set
				{
					lines.Clear();
					WriteLine(value);
				}
			}

			private Godot.Label label;
			public Godot.Label Label
			{
				get => label;
				set
				{
					label = value;
					if (label is not null)
						label.Text = Text;
				}
			}

			private Godot.ColorRect cursor;
			public Godot.ColorRect Cursor
			{
				get => cursor;
				set
				{
					cursor = value;
					if (cursor is not null)
						SetCursor();
				}
			}

			public VirtualScreenText SetCursor() =>
				SetCursor((uint)(lines.Count == 0 ? 0 : lines.Last().Length > 79 ? 0 : lines.Last().Length),
						(uint)(lines.Count - (lines.Count > 0 ? 1 : 0)));

			public VirtualScreenText SetCursor(uint x, uint y)
			{
				if (Cursor is not null)
					Cursor.GlobalPosition = new Godot.Vector2(x * 9, y * 16 + 12);
				return this;
			}

			public const byte Height = 25, Width = 80;
			public const float BlinkRate = 0.25f;
			private float Blink { get; set; } = 0f;

			public bool ShowCursor
			{
				get => Cursor is not null && Cursor.Visible;
				set
				{
					if (Cursor is not null)
						Cursor.Visible = value;
				}
			}

			public VirtualScreenText UpdateCursor(float delta)
			{
				Blink += delta;
				while (Blink > BlinkRate)
				{
					Blink -= BlinkRate;
					ShowCursor = !ShowCursor;
				}
				return this;
			}

			public override string ToString() => Text;

			public VirtualScreenText CLS()
			{
				Text = string.Empty;
				return this;
			}

			public VirtualScreenText WriteLine(string value)
			{
				foreach (string line in Wrap(value).Split('\n'))
					lines.Enqueue(line);
				if (Height > 0)
					while (lines.Count > Height)
						lines.Dequeue();
				if (Label is not null)
					Label.Text = Text;
				SetCursor();
				return this;
			}

			public string Wrap(string value) => Wrap(value, Width);
			public static string Wrap(string value, uint width)
			{
				if (width <= 0)
					return value;
				StringBuilder stringBuilder = new();
				foreach (string a in value.Split('\n'))
					foreach (string b in ChunksUpto(a, width))
						stringBuilder.Append(b).Append('\n');
				return TrimLastCharacter(stringBuilder.ToString());
			}

			public static IEnumerable<string> ChunksUpto(string str, uint maxChunkSize)
			{
				for (int i = 0; i < str.Length; i += (int)maxChunkSize)
					yield return str.Substring(i, Math.Min((int)maxChunkSize, str.Length - i));
			}

			public static string TrimLastCharacter(string str) =>
				string.IsNullOrEmpty(str) ? str : str[..^1];
		}
		private static readonly Color _grey = Color.Color8(170, 170, 170, 255);
		public readonly VirtualScreenText Screen = new();
		private SubViewport Viewport;

		public DosScreen()
		{
			AddChild(Viewport = new SubViewport()
			{
				Size = new Vector2I(720, 400),
				Disable3D = true,
				RenderTargetClearMode = SubViewport.ClearMode.Once,
			});
			Viewport.AddChild(new ColorRect()
			{
				Color = Color.Color8(0, 0, 0, 255),
				Size = Viewport.Size,
			});
			FontFile font = BiosFont.GetFont();
			Label label = new()
			{
				Theme = BiosFont.GetTheme(),
				Position = new Vector2(0f, 0f),
				LabelSettings = new LabelSettings
				{
					Font = font,
					FontSize = BiosFont.CharacterHeight,
					LineSpacing = 0,
					FontColor = _grey,
				},
			};
			Viewport.AddChild(label);
			Screen.Label = label;
			ColorRect cursor = new()
			{
				Color = _grey,
				Size = new Vector2(9f, 2f),
			};
			Viewport.AddChild(cursor);
			Screen.Cursor = cursor;
		}

		public DosScreen WriteLine(string text)
		{
			Screen.WriteLine(text);
			return this;
		}

		public override void _Process(double delta) => Screen.UpdateCursor((float)delta);

		public SubViewport GetViewport() => Viewport;
	}
}
