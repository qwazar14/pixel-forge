using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PixelForge;

public class PalColor
{
	public Color C;
	public string Name = "";
}

/// <summary>A named chain of colours, dark to light.</summary>
public class Ramp
{
	public string Name;
	public List<PalColor> Colors = new();
}

public class Palette
{
	public List<Ramp> Ramps = new() { new Ramp { Name = "Основная" } };
	public bool Strict; // "only palette": colours outside it can't be painted

	public (Ramp ramp, int i) Find(Color c)
	{
		uint k = c.ToRgba32();
		foreach (var r in Ramps)
			for (int i = 0; i < r.Colors.Count; i++)
				if (r.Colors[i].C.ToRgba32() == k) return (r, i);
		return (null, -1);
	}

	public bool Contains(Color c) => Find(c).ramp != null;

	/// <summary>Neighbour in the colour's ramp: +1 lighter, -1 darker.</summary>
	public Color? Step(Color c, int dir)
	{
		var (r, i) = Find(c);
		if (r == null) return null;
		int j = i + dir;
		return j >= 0 && j < r.Colors.Count ? r.Colors[j].C : null;
	}

	/// <summary>Most frequent opaque-ish colours of an image, sorted dark to light.</summary>
	public static List<Color> UniqueColors(Image img, int limit)
	{
		var count = new Dictionary<uint, int>();
		var data = img.GetData(); // Rgba8
		for (int i = 0; i < data.Length; i += 4)
		{
			if (data[i + 3] == 0) continue;
			uint k = (uint)(data[i] << 24 | data[i + 1] << 16 | data[i + 2] << 8 | data[i + 3]);
			count[k] = count.GetValueOrDefault(k) + 1;
		}
		return count.OrderByDescending(p => p.Value).Take(limit)
			.Select(p => new Color(p.Key)).OrderBy(c => c.Luminance).ToList();
	}

	// ---- files ----

	public class ColorDto { public string hex { get; set; } public string name { get; set; } }
	public class RampDto { public string name { get; set; } public List<ColorDto> colors { get; set; } = new(); }
	public class Dto { public bool strict { get; set; } public List<RampDto> ramps { get; set; } = new(); }

	public Dto ToDto() => new()
	{
		strict = Strict,
		ramps = Ramps.Select(r => new RampDto { name = r.Name, colors = r.Colors.Select(c => new ColorDto { hex = "#" + c.C.ToHtml(c.C.A < 1), name = c.Name }).ToList() }).ToList(),
	};

	public static Palette FromDto(Dto d)
	{
		var p = new Palette { Strict = d.strict, Ramps = d.ramps.Select(r => new Ramp { Name = r.name, Colors = r.colors.Select(c => new PalColor { C = Color.FromHtml(c.hex), Name = c.name ?? "" }).ToList() }).ToList() };
		if (p.Ramps.Count == 0) p.Ramps.Add(new Ramp { Name = "Основная" });
		return p;
	}

	public void SaveJson(string path) => System.IO.File.WriteAllText(path, JsonSerializer.Serialize(ToDto(), new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));

	public static Palette LoadJson(string path) => FromDto(JsonSerializer.Deserialize<Dto>(System.IO.File.ReadAllText(path)));

	/// <summary>Colours from .gpl, .hex or an image, as one ramp named after the file. Null if unreadable.</summary>
	public static Ramp ImportRamp(string path, int limit = 256)
	{
		var name = System.IO.Path.GetFileNameWithoutExtension(path);
		var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
		var r = new Ramp { Name = name };
		if (ext == ".gpl")
		{
			foreach (var line in System.IO.File.ReadLines(path))
			{
				var t = line.Trim();
				if (t.Length == 0 || t.StartsWith('#') || !char.IsDigit(t[0])) continue; // header, Name:, Columns:, comments
				var parts = t.Split((char[])null, 4, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length >= 3 && byte.TryParse(parts[0], out var rr) && byte.TryParse(parts[1], out var gg) && byte.TryParse(parts[2], out var bb))
					r.Colors.Add(new PalColor { C = Color.Color8(rr, gg, bb), Name = parts.Length > 3 ? parts[3] : "" });
			}
		}
		else if (ext == ".hex")
		{
			foreach (var line in System.IO.File.ReadLines(path))
			{
				var t = line.Trim().TrimStart('#');
				if (t.Length is 6 or 8 && Color.HtmlIsValid(t)) r.Colors.Add(new PalColor { C = Color.FromHtml(t) });
			}
		}
		else
		{
			var img = Image.LoadFromFile(path);
			if (img == null) return null;
			img.Convert(Image.Format.Rgba8);
			r.Colors = UniqueColors(img, limit).Select(c => new PalColor { C = c }).ToList();
		}
		return r.Colors.Count > 0 ? r : null;
	}
}
