using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelForge;

/// <summary>A sketch from rf-game's art_src/prefab_src: the PNG plus index.json's footprint and front corner.</summary>
public class Sketch
{
	public const string DefaultDir = "../rf-game/art_src/prefab_src";
	const int Margin = 8;

	public string Name, Id, Ru, File;
	public int W, D, Fx = -1, Fy = -1; // W, D as this view stands (a turned one has them swapped)
	public bool Turn, Winter;

	public string Title => (Ru ?? Name) + (Turn ? ", поворот" : "") + (Winter ? ", зима" : "") + $"  ({Name})";

	class Entry
	{
		public string name { get; set; }
		public string id { get; set; }
		public string ru { get; set; }
		public bool turn { get; set; }
		public bool winter { get; set; }
		public int w { get; set; }
		public int d { get; set; }
		public int fx { get; set; }
		public int fy { get; set; }
	}

	/// <summary>The folder's PNGs, with index.json's data where it has them.</summary>
	public static List<Sketch> List(string dir)
	{
		if (!Directory.Exists(dir)) return new();
		var known = new Dictionary<string, Entry>();
		var idx = Path.Combine(dir, "index.json");
		if (System.IO.File.Exists(idx))
			try { foreach (var e in JsonSerializer.Deserialize<List<Entry>>(System.IO.File.ReadAllText(idx))) known[e.name] = e; }
			catch (Exception e) { GD.PrintErr($"index.json: {e.Message}"); }
		return Directory.GetFiles(dir, "*.png").OrderBy(f => f).Select(f =>
		{
			var name = Path.GetFileNameWithoutExtension(f);
			var s = new Sketch { Name = name, File = f };
			if (known.TryGetValue(name, out var e))
			{
				s.Id = e.id; s.Ru = e.ru; s.Turn = e.turn; s.Winter = e.winter;
				s.W = e.w; s.D = e.d; s.Fx = e.fx; s.Fy = e.fy;
			}
			return s;
		}).ToList();
	}

	public Image Load()
	{
		var img = Image.LoadFromFile(File);
		img?.Convert(Image.Format.Rgba8);
		return img;
	}

	/// <summary>The sketch pixel that stands on the anchor: index.json's front corner, else the bottom middle.</summary>
	public Vector2I Corner(Image img) => Fx >= 0 ? new Vector2I(Fx, Fy) : new Vector2I(img.GetWidth() / 2, img.GetHeight() - 1);

	void SetBase(Doc d)
	{
		if (W <= 0) return;
		// the doc keeps the unturned footprint; a turned sketch's w, d are already swapped
		d.BaseW = Turn ? D : W;
		d.BaseD = Turn ? W : D;
		d.Turned = Turn;
	}

	/// <summary>A new project around the sketch: symmetric about the anchor, the sketch as a half-faded reference under an empty layer.</summary>
	public Doc NewDoc()
	{
		var img = Load();
		if (img == null) return null;
		var c = Corner(img);
		int half = Math.Max(Math.Max(c.X, img.GetWidth() - 1 - c.X), Math.Max(W, D) * 9) + Margin;
		var pivot = new Vector2I(half, Math.Max(c.Y, 0) + Margin);
		var d = new Doc(Math.Min(2 * half + 1, 1024), Math.Min(pivot.Y + 1 + 2, 1024), false) { GameId = Id ?? Name };
		d.Pivot = pivot;
		SetBase(d);
		var refLayer = d.NewLayer("эскиз " + Name);
		refLayer.Reference = true;
		refLayer.Opacity = 0.5f;
		refLayer.Img.BlitRect(img, new Rect2I(Vector2I.Zero, img.GetSize()), pivot - c);
		d.Layers.Add(refLayer);
		d.Layers.Add(d.NewLayer("Слой 1"));
		d.Current = 1;
		// the view this sketch is: only it gets exported to start with
		int v = (Turn ? 2 : 0) + (Winter ? 1 : 0);
		d.ViewOn = new bool[4];
		d.ViewOn[v] = true;
		return d;
	}

	/// <summary>Adds the sketch to a project as a reference layer with its front corner on the anchor.</summary>
	public bool AddTo(Doc d)
	{
		var img = Load();
		if (img == null) return false;
		SetBase(d);
		d.ImportLayerAt(img, "эскиз " + Name, d.Pivot - Corner(img), true);
		return true;
	}
}
