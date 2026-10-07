using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace PixelForge;

public class Layer
{
	public string Name;
	public Image Img;
	public bool Visible = true, Locked, Reference;
	public float Opacity = 1;
	/// <summary>Which game views (summer, winter, turned, turned winter) show this layer.</summary>
	public bool[] Views = { true, true, true, true };

	public Layer With(Image img) => new() { Name = Name, Img = img, Visible = Visible, Locked = Locked, Reference = Reference, Opacity = Opacity, Views = (bool[])Views.Clone() };
}

public enum Place { Center, TopLeft, Anchor }

/// <summary>The document: canvas size, layers, palette, undo history. No UI here, so the self-test can drive it.</summary>
public class Doc
{
	public int W, H;
	public List<Layer> Layers = new();
	public int Current;
	/// <summary>Building anchor: the pixel where an image's bottom-middle pixel stands (the footprint's front corner).</summary>
	public Vector2I Pivot;
	/// <summary>Footprint in cells (as the building stands unturned); Turned shows the turned view's D×W.</summary>
	public int BaseW = 2, BaseD = 2;
	public bool Turned;
	public static readonly string[] ViewNames = { "Лето", "Зима", "Поворот", "Поворот, зима" };
	public static readonly string[] ViewSuffix = { "", "_winter", "_turn", "_turn_winter" };
	public bool[] ViewOn = { true, false, false, false };
	public string GameId, ExportDir;
	public Palette Palette = new();
	public string Path;
	public bool Dirty;
	public event Action Changed;

	// one undo step = before/after of every pixel it touched (deltas, not copies),
	// plus the layer list before/after when the step changed the layer stack
	class Step
	{
		public Dictionary<Layer, Dictionary<int, (Color a, Color b)>> Px = new();
		public List<Layer> ListA, ListB;
		public int CurA, CurB;
		public Vector2I SizeA, SizeB, PivotA, PivotB;
	}
	readonly List<Step> undo = new(), redo = new();
	Step stroke;
	Layer strokeLayer;
	const int MaxUndo = 200;

	public Doc(int w, int h, bool withLayer = true)
	{
		W = w; H = h;
		if (withLayer) Layers.Add(NewLayer("Слой 1"));
		AxisX2 = w; AxisY2 = h;
		Pivot = new Vector2I(w / 2, h - 1);
	}

	public Layer NewLayer(string name) => new() { Name = name, Img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8) };
	public Layer Cur => Layers[Current];
	public bool Painting => stroke != null && Float == null; // a tool's stroke is open (not a floating selection)
	public bool CanPaint => Cur.Visible && !Cur.Locked;

	/// <summary>Marks a non-undoable change (layer props, palette) and redraws.</summary>
	public void Touch() { Dirty = true; Changed?.Invoke(); }
	public void Select(int i) { Anchor(); Current = Math.Clamp(i, 0, Layers.Count - 1); Changed?.Invoke(); }

	// ---- pixels ----

	// symmetry axes in half-pixel units: AxisX2 = W puts the vertical axis through the middle of the canvas
	public bool SymX, SymY;
	public int AxisX2, AxisY2;
	bool mirror;

	/// <summary>Selection rectangle; painting is clipped to it.</summary>
	public Rect2I? Sel;

	public bool BeginStroke(bool mirrored = true)
	{
		Anchor();
		if (stroke != null || !CanPaint) return false;
		stroke = new Step();
		strokeLayer = Cur;
		mirror = mirrored;
		return true;
	}

	void Record(Layer l, int x, int y, Color c)
	{
		var old = l.Img.GetPixel(x, y);
		if (old == c) return;
		if (!stroke.Px.TryGetValue(l, out var px)) stroke.Px[l] = px = new();
		int k = y * W + x;
		px[k] = px.TryGetValue(k, out var p) ? (p.a, c) : (old, c);
		l.Img.SetPixel(x, y, c);
	}

	void Put(int x, int y, Color c)
	{
		if (x < 0 || y < 0 || x >= W || y >= H || (Sel is Rect2I s && !s.HasPoint(new Vector2I(x, y)))) return;
		Record(strokeLayer, x, y, c);
	}

	public void Plot(int x, int y, Color c)
	{
		if (stroke == null) return;
		Put(x, y, c);
		if (!mirror) return;
		if (SymX) Put(AxisX2 - 1 - x, y, c);
		if (SymY) Put(x, AxisY2 - 1 - y, c);
		if (SymX && SymY) Put(AxisX2 - 1 - x, AxisY2 - 1 - y, c);
	}

	/// <summary>Undoes the open stroke's pixels but keeps it open (shape tools redraw their preview this way).</summary>
	public void RestartStroke()
	{
		if (stroke == null) return;
		foreach (var (l, px) in stroke.Px)
			foreach (var (k, p) in px) l.Img.SetPixel(k % l.Img.GetWidth(), k / l.Img.GetWidth(), p.a);
		stroke.Px.Clear();
	}

	/// <summary>Square brush of the given size centred on (x, y).</summary>
	public void Stamp(int x, int y, int size, Color c)
	{
		int o = (size - 1) / 2;
		for (int dy = 0; dy < size; dy++)
			for (int dx = 0; dx < size; dx++)
				Plot(x - o + dx, y - o + dy, c);
	}

	/// <summary>Bresenham line of stamps.</summary>
	public void Line(int x0, int y0, int x1, int y1, int size, Color c)
	{
		int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
		int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
		int err = dx + dy;
		while (true)
		{
			Stamp(x0, y0, size, c);
			if (x0 == x1 && y0 == y1) break;
			int e2 = 2 * err;
			if (e2 >= dy) { err += dy; x0 += sx; }
			if (e2 <= dx) { err += dx; y0 += sy; }
		}
	}

	/// <summary>Clean isometric 2:1 line: two pixels across per pixel down, n pixels across.</summary>
	public void IsoLine(int x0, int y0, int sx, int sy, int n, int size, Color c)
	{
		for (int i = 0; i <= n; i++) Stamp(x0 + sx * i, y0 + sy * (i / 2), size, c);
	}

	public void Rect(int x0, int y0, int x1, int y1, int size, Color c, bool fill)
	{
		if (fill)
		{
			for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
				for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++) Plot(x, y, c);
			return;
		}
		Line(x0, y0, x1, y0, size, c); Line(x1, y0, x1, y1, size, c);
		Line(x1, y1, x0, y1, size, c); Line(x0, y1, x0, y0, size, c);
	}

	public void Ellipse(int x0, int y0, int x1, int y1, int size, Color c, bool fill)
	{
		var pts = EllipsePoints(x0, y0, x1, y1);
		if (!fill) { foreach (var p in pts) Stamp(p.X, p.Y, size, c); return; }
		foreach (var row in pts.GroupBy(p => p.Y))
			for (int x = row.Min(p => p.X); x <= row.Max(p => p.X); x++) Plot(x, row.Key, c);
	}

	/// <summary>Ellipse outline inscribed in the box (A. Zingl's Bresenham ellipse-in-rectangle).</summary>
	public static HashSet<Vector2I> EllipsePoints(int x0, int y0, int x1, int y1)
	{
		var pts = new HashSet<Vector2I>();
		long a = Math.Abs(x1 - x0), b = Math.Abs(y1 - y0), b1 = b & 1;
		long dx = 4 * (1 - a) * b * b, dy = 4 * (b1 + 1) * a * a;
		long err = dx + dy + b1 * a * a, e2;
		if (x0 > x1) { x0 = x1; x1 += (int)a; }
		if (y0 > y1) y0 = y1;
		y0 += (int)((b + 1) / 2); y1 = y0 - (int)b1;
		a *= 8 * a; b1 = 8 * b * b;
		do
		{
			pts.Add(new(x1, y0)); pts.Add(new(x0, y0)); pts.Add(new(x0, y1)); pts.Add(new(x1, y1));
			e2 = 2 * err;
			if (e2 <= dy) { y0++; y1--; err += dy += a; }
			if (e2 >= dx || 2 * err > dy) { x0++; x1--; err += dx += b1; }
		} while (x0 <= x1);
		while (y0 - y1 <= b) // flat ellipses: finish the tips
		{
			pts.Add(new(x0 - 1, y0)); pts.Add(new(x1 + 1, y0++));
			pts.Add(new(x0 - 1, y1)); pts.Add(new(x1 + 1, y1--));
		}
		return pts;
	}

	/// <summary>Bucket fill on the stroke's layer: tolerance 0–255 per channel, contiguous (4-way) or every match.</summary>
	public void Fill(int x, int y, Color c, int tolerance, bool contiguous)
	{
		var area = (Sel ?? new Rect2I(0, 0, W, H)).Intersection(new Rect2I(0, 0, W, H));
		if (stroke == null || !area.HasPoint(new Vector2I(x, y))) return;
		var img = strokeLayer.Img;
		var t = img.GetPixel(x, y);
		bool Match(int px, int py)
		{
			var p = img.GetPixel(px, py);
			return Math.Abs(p.R8 - t.R8) <= tolerance && Math.Abs(p.G8 - t.G8) <= tolerance
				&& Math.Abs(p.B8 - t.B8) <= tolerance && Math.Abs(p.A8 - t.A8) <= tolerance;
		}
		var hits = new List<Vector2I>();
		if (!contiguous)
		{
			for (int py = area.Position.Y; py < area.End.Y; py++)
				for (int px = area.Position.X; px < area.End.X; px++)
					if (Match(px, py)) hits.Add(new(px, py));
		}
		else
		{
			var seen = new bool[W * H];
			var todo = new Stack<Vector2I>();
			todo.Push(new(x, y));
			seen[y * W + x] = true;
			while (todo.Count > 0)
			{
				var p = todo.Pop();
				hits.Add(p);
				foreach (var n in new Vector2I[] { new(p.X + 1, p.Y), new(p.X - 1, p.Y), new(p.X, p.Y + 1), new(p.X, p.Y - 1) })
					if (area.HasPoint(n) && !seen[n.Y * W + n.X] && Match(n.X, n.Y)) { seen[n.Y * W + n.X] = true; todo.Push(n); }
			}
		}
		foreach (var p in hits) Plot(p.X, p.Y, c); // collected first so the fill can't feed on itself
	}

	public void EndStroke()
	{
		if (stroke == null) return;
		foreach (var px in stroke.Px.Values) // pixels that ended where they started (a lift dropped in place) are no change
			foreach (var k in px.Where(e => e.Value.a == e.Value.b).Select(e => e.Key).ToList()) px.Remove(k);
		foreach (var l in stroke.Px.Where(e => e.Value.Count == 0).Select(e => e.Key).ToList()) stroke.Px.Remove(l);
		if (stroke.Px.Count > 0) Push(stroke);
		stroke = null;
		Changed?.Invoke();
	}

	void Push(Step s)
	{
		undo.Add(s);
		if (undo.Count > MaxUndo) undo.RemoveAt(0);
		redo.Clear();
		Dirty = true;
	}

	// ---- selection: a floating piece rides on an open stroke until it is anchored ----

	public Image Float;
	public Vector2I FloatPos;
	public Layer FloatLayer => Float == null ? null : strokeLayer;
	static Image clip;
	static Vector2I clipPos;

	Rect2I Bounds => new(0, 0, W, H);

	public void SelectAll() { Anchor(); Sel = Bounds; }
	public void Deselect() { Anchor(); Sel = null; }

	/// <summary>Cuts (or copies) the selected pixels of the current layer into a floating piece.</summary>
	public bool Lift(bool copy = false)
	{
		if (Float != null) return true;
		var r = Sel?.Intersection(Bounds) ?? Bounds;
		if (r.Area == 0 || !BeginStroke(false)) return false;
		Float = Cur.Img.GetRegion(r);
		FloatPos = r.Position;
		Sel = r;
		if (!copy)
			for (int y = r.Position.Y; y < r.End.Y; y++)
				for (int x = r.Position.X; x < r.End.X; x++) Record(strokeLayer, x, y, new Color(0, 0, 0, 0));
		Changed?.Invoke();
		return true;
	}

	public void MoveFloat(Vector2I pos)
	{
		if (Float == null) return;
		FloatPos = pos;
		Sel = new Rect2I(pos, Float.GetSize());
	}

	/// <summary>Drops the floating piece onto its layer: one undo step together with its lift.</summary>
	public void Anchor()
	{
		if (Float == null) return;
		var f = Float;
		Float = null;
		for (int y = 0; y < f.GetHeight(); y++)
			for (int x = 0; x < f.GetWidth(); x++)
			{
				int tx = FloatPos.X + x, ty = FloatPos.Y + y;
				var c = f.GetPixel(x, y);
				if (c.A > 0 && tx >= 0 && ty >= 0 && tx < W && ty < H) Record(strokeLayer, tx, ty, c);
			}
		Sel = Sel?.Intersection(Bounds);
		if (Sel is { Area: 0 }) Sel = null;
		EndStroke();
	}

	/// <summary>Throws the floating piece away and puts the lifted pixels back.</summary>
	public void CancelFloat()
	{
		if (Float == null) return;
		Float = null;
		RestartStroke();
		stroke = null;
		Changed?.Invoke();
	}

	/// <summary>Flip/rotate the selection (left floating) or, with no selection, the whole layer.</summary>
	public void Transform(Func<Image, Image> f)
	{
		bool whole = Sel == null && Float == null;
		if (!Lift()) return;
		var center = FloatPos * 2 + Float.GetSize();
		Float = f((Image)Float.Duplicate());
		MoveFloat((center - Float.GetSize()) / 2);
		if (whole) { Anchor(); Sel = null; }
		Changed?.Invoke();
	}

	public void FlipH() => Transform(i => { i.FlipX(); return i; });
	public void FlipV() => Transform(i => { i.FlipY(); return i; });
	public void Rotate(bool cw) => Transform(i => { i.Rotate90(cw ? ClockDirection.Clockwise : ClockDirection.Counterclockwise); return i; });

	public void Copy()
	{
		if (Float != null) { clip = (Image)Float.Duplicate(); clipPos = FloatPos; return; }
		var r = Sel?.Intersection(Bounds) ?? Bounds;
		if (r.Area == 0) return;
		clip = Cur.Img.GetRegion(r);
		clipPos = r.Position;
	}

	public void Cut() { Copy(); DeleteSelection(); }

	public bool Paste()
	{
		if (clip == null || !BeginStroke(false)) return false;
		Float = (Image)clip.Duplicate();
		MoveFloat(clipPos);
		Changed?.Invoke();
		return true;
	}

	/// <summary>Clears the selected pixels (or drops the floating piece).</summary>
	public void DeleteSelection()
	{
		if (Float != null) { Float = null; EndStroke(); return; }
		if (Sel == null || !BeginStroke(false)) return;
		var r = Sel.Value.Intersection(Bounds);
		for (int y = r.Position.Y; y < r.End.Y; y++)
			for (int x = r.Position.X; x < r.End.X; x++) Record(strokeLayer, x, y, new Color(0, 0, 0, 0));
		EndStroke();
	}

	/// <summary>Replaces one colour with another on the current layer or on every unlocked layer. Returns pixels changed.</summary>
	public int ReplaceColor(Color from, Color to, bool allLayers)
	{
		if (!BeginStroke(false)) return 0;
		uint key = from.ToRgba32();
		int n = 0;
		foreach (var l in allLayers ? Layers.Where(l => !l.Locked) : Cur.Locked ? Enumerable.Empty<Layer>() : new[] { Cur })
			for (int y = 0; y < H; y++)
				for (int x = 0; x < W; x++)
					if (l.Img.GetPixel(x, y).ToRgba32() == key) { Record(l, x, y, to); n++; }
		EndStroke();
		return n;
	}

	// ---- layer stack (undoable) ----

	/// <summary>Runs a change to the layer list as one undo step.</summary>
	void Structure(Action change)
	{
		Anchor();
		if (stroke != null) return;
		var s = new Step { ListA = new(Layers), CurA = Current, SizeA = new(W, H), PivotA = Pivot };
		change();
		Current = Math.Clamp(Current, 0, Layers.Count - 1);
		s.ListB = new(Layers);
		s.CurB = Current;
		s.SizeB = new(W, H);
		s.PivotB = Pivot;
		Push(s);
		Changed?.Invoke();
	}

	public void AddLayer() => Structure(() => Layers.Insert(++Current, NewLayer($"Слой {Layers.Count + 1}")));

	public void DuplicateLayer() => Structure(() =>
	{
		var c = Cur;
		var copy = c.With((Image)c.Img.Duplicate());
		copy.Name = c.Name + " копия";
		copy.Locked = false;
		Layers.Insert(++Current, copy);
	});

	public void DeleteLayer()
	{
		if (Layers.Count > 1) Structure(() => Layers.RemoveAt(Current--));
	}

	public void MoveLayer(int dir)
	{
		int j = Current + dir;
		if (j >= 0 && j < Layers.Count) Structure(() => { (Layers[Current], Layers[j]) = (Layers[j], Layers[Current]); Current = j; });
	}

	public void MergeDown()
	{
		if (Current == 0) return;
		Structure(() =>
		{
			var lo = Layers[Current - 1];
			var img = (Image)lo.Img.Duplicate();
			Blend(img, Cur);
			Layers[Current - 1] = lo.With(img);
			Layers.RemoveAt(Current--);
		});
	}

	/// <summary>Visible non-reference layers become one, placed where the lowest of them was.</summary>
	public void FlattenVisible()
	{
		var flat = Layers.Where(Exported).ToList();
		if (flat.Count < 2) return;
		Structure(() =>
		{
			int at = Layers.IndexOf(flat[0]);
			Layers.Insert(at, new Layer { Name = "Сведённый", Img = Composite() });
			Layers.RemoveAll(flat.Contains);
			Current = at;
		});
	}

	void SetSize(int w, int h)
	{
		W = w; H = h;
		Sel = null;
		AxisX2 = w; AxisY2 = h;
	}

	/// <summary>New canvas size; old content keeps its pixels and lands at offset (no stretching).</summary>
	public void Resize(int w, int h, Vector2I offset)
	{
		if (w == W && h == H && offset == Vector2I.Zero) return;
		Structure(() =>
		{
			for (int i = 0; i < Layers.Count; i++)
			{
				var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
				img.BlitRect(Layers[i].Img, new Rect2I(0, 0, W, H), offset);
				Layers[i] = Layers[i].With(img);
			}
			SetSize(w, h);
			Pivot += offset;
		});
	}

	/// <summary>Offset for a 3×3 anchor choice (0..2 per axis: start, middle, end) when going to size w×h.</summary>
	public Vector2I ResizeOffset(int w, int h, int ax, int ay) => new(ax * (w - W) / 2, ay * (h - H) / 2);

	/// <summary>Where an image's top-left goes for a placement.</summary>
	public Vector2I PlaceAt(Vector2I size, Place place) => place switch
	{
		Place.Center => (new Vector2I(W, H) - size) / 2,
		Place.TopLeft => Vector2I.Zero,
		_ => new Vector2I(Pivot.X - size.X / 2, Pivot.Y - (size.Y - 1)), // bottom-middle pixel on the anchor
	};

	/// <summary>Adds an image as a new layer above the current one.</summary>
	public void ImportLayer(Image src, string name, Place place, bool reference) => ImportLayerAt(src, name, PlaceAt(src.GetSize(), place), reference);

	public Layer ImportLayerAt(Image src, string name, Vector2I at, bool reference)
	{
		src = (Image)src.Duplicate();
		src.Convert(Image.Format.Rgba8);
		var l = NewLayer(name);
		l.Reference = reference;
		if (reference) l.Opacity = 0.5f;
		l.Img.BlitRect(src, new Rect2I(Vector2I.Zero, src.GetSize()), at);
		Structure(() => Layers.Insert(++Current, l));
		return l;
	}

	/// <summary>A canvas for a W×D building of the given height above its footprint: symmetric about the anchor, which
	/// sits bottom-middle with a small margin all round.</summary>
	public static Doc ForBuilding(int w, int d, int height)
	{
		const int margin = 6;
		int half = Math.Max(w, d) * 9 + margin;
		int baseH = Mathf.CeilToInt((w + d) * 4.5f);
		int cw = Math.Min(2 * half + 1, 1024), ch = Math.Clamp(baseH + height + margin + 3, 8, 1024);
		return new Doc(cw, ch) { BaseW = w, BaseD = d, Pivot = new Vector2I(cw / 2, ch - 1 - 2) };
	}

	public void SetPivot(Vector2I p)
	{
		if (p != Pivot) Structure(() => Pivot = p);
	}

	/// <summary>Every layer mirrored left-right around the anchor's column (the anchor stays put).</summary>
	public void MirrorAll() => Structure(() =>
	{
		for (int i = 0; i < Layers.Count; i++)
		{
			var f = (Image)Layers[i].Img.Duplicate();
			f.FlipX(); // x -> W-1-x; shifting by 2P-(W-1) makes it x -> 2P-x
			var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
			img.BlitRect(f, new Rect2I(0, 0, W, H), new Vector2I(2 * Pivot.X - (W - 1), 0));
			Layers[i] = Layers[i].With(img);
		}
	});

	// ---- game export ----

	/// <summary>The layers of one view (references never), flattened.</summary>
	public Image ViewImage(int v)
	{
		Anchor();
		var outImg = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
		foreach (var l in Layers)
			if (!l.Reference && l.Views[v]) Blend(outImg, l);
		return outImg;
	}

	/// <summary>Trims empty margins keeping the anchor the bottom-middle pixel: as wide on both sides of it, nothing below it.
	/// Null if the picture is empty; cutBelow tells whether there were pixels under the anchor row.</summary>
	public Image GameCrop(Image img, out bool cutBelow)
	{
		var used = img.GetUsedRect();
		cutBelow = used.Size.Y > 0 && used.End.Y - 1 > Pivot.Y;
		if (used.Size.X == 0 || used.Position.Y > Pivot.Y) return null;
		int half = Math.Max(Pivot.X - used.Position.X, used.End.X - 1 - Pivot.X);
		half = Math.Max(half, 0);
		int top = Math.Min(used.Position.Y, Pivot.Y);
		var outImg = Image.CreateEmpty(2 * half + 1, Pivot.Y - top + 1, false, Image.Format.Rgba8);
		outImg.BlitRect(img, new Rect2I(Pivot.X - half, top, 2 * half + 1, Pivot.Y - top + 1), Vector2I.Zero);
		return outImg;
	}

	/// <summary>Writes &lt;id&gt;.png, &lt;id&gt;_winter.png… for the views switched on. Returns lines for the user.</summary>
	public List<string> ExportGame(string dir, string id)
	{
		var report = new List<string>();
		for (int v = 0; v < 4; v++)
		{
			if (!ViewOn[v]) continue;
			var file = id + ViewSuffix[v] + ".png";
			var img = GameCrop(ViewImage(v), out bool cut);
			if (img == null) { report.Add($"{file}: пусто, пропущен"); continue; }
			var err = img.SavePng(System.IO.Path.Combine(dir, file));
			report.Add(err != Error.Ok ? $"{file}: ошибка {err}" : $"{file}: {img.GetWidth()}×{img.GetHeight()}" + (cut ? " (пиксели ниже якоря обрезаны)" : ""));
		}
		GameId = id;
		ExportDir = dir;
		Dirty = true;
		return report;
	}

	public bool Undo()
	{
		if (Float != null) { CancelFloat(); return true; }
		return Move(undo, redo, true);
	}

	public bool Redo() { Anchor(); return Move(redo, undo, false); }

	bool Move(List<Step> from, List<Step> to, bool back)
	{
		if (stroke != null || from.Count == 0) return false;
		var s = from[^1];
		from.RemoveAt(from.Count - 1);
		foreach (var (l, px) in s.Px)
			foreach (var (k, p) in px) l.Img.SetPixel(k % l.Img.GetWidth(), k / l.Img.GetWidth(), back ? p.a : p.b);
		if (s.ListA != null)
		{
			Layers = new(back ? s.ListA : s.ListB);
			Current = back ? s.CurA : s.CurB;
			var size = back ? s.SizeA : s.SizeB;
			if (size != new Vector2I(W, H)) SetSize(size.X, size.Y);
			Pivot = back ? s.PivotA : s.PivotB;
		}
		to.Add(s);
		Dirty = true;
		Changed?.Invoke();
		return true;
	}

	// ---- composite / export ----

	static bool Exported(Layer l) => l.Visible && !l.Reference;

	/// <summary>Blends a layer onto img honouring its opacity.</summary>
	static void Blend(Image img, Layer l)
	{
		var src = l.Img;
		if (l.Opacity < 1)
		{
			var data = src.GetData();
			for (int i = 3; i < data.Length; i += 4) data[i] = (byte)Mathf.RoundToInt(data[i] * l.Opacity);
			src = Image.CreateFromData(src.GetWidth(), src.GetHeight(), false, Image.Format.Rgba8, data);
		}
		img.BlendRect(src, new Rect2I(0, 0, src.GetWidth(), src.GetHeight()), Vector2I.Zero);
	}

	/// <summary>Visible layers without references flattened, what the export writes.</summary>
	public Image Composite()
	{
		var outImg = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
		foreach (var l in Layers)
			if (Exported(l)) Blend(outImg, l);
		return outImg;
	}

	/// <summary>The exported colour of one pixel (for the eyedropper).</summary>
	public Color PixelAt(int x, int y)
	{
		var c = new Color(0, 0, 0, 0);
		foreach (var l in Layers)
			if (Exported(l))
			{
				var p = l.Img.GetPixel(x, y);
				p.A *= l.Opacity;
				c = c.Blend(p);
			}
		return c;
	}

	public Error ExportPng(string path) { Anchor(); return Composite().SavePng(path); }

	public static Doc FromImage(Image img, string name)
	{
		img = (Image)img.Duplicate();
		img.Convert(Image.Format.Rgba8);
		var d = new Doc(img.GetWidth(), img.GetHeight(), false);
		d.Layers.Add(new Layer { Name = name, Img = img });
		return d;
	}

	// ---- .pforge: zip with project.json + one PNG per layer ----

	class LayerDto
	{
		public string name { get; set; }
		public bool visible { get; set; } = true;
		public bool locked { get; set; }
		public bool reference { get; set; }
		public float opacity { get; set; } = 1;
		public bool[] views { get; set; }
		public string file { get; set; }
	}
	class ProjectDto
	{
		public int version { get; set; } = 1;
		public int width { get; set; }
		public int height { get; set; }
		public int current { get; set; }
		public int[] anchor { get; set; }
		public int baseW { get; set; } = 2;
		public int baseD { get; set; } = 2;
		public bool[] viewsOn { get; set; }
		public string gameId { get; set; }
		public string exportDir { get; set; }
		public List<LayerDto> layers { get; set; } = new();
		public Palette.Dto palette { get; set; }
	}

	public Error Save(string path)
	{
		var err = Write(path);
		if (err != Error.Ok) return err;
		Path = path;
		Dirty = false;
		Changed?.Invoke();
		return Error.Ok;
	}

	/// <summary>Writes the project without making it the document's file (autosave). Goes through a temp file so a
	/// crash mid-write never leaves a broken project.</summary>
	public Error Write(string path)
	{
		Anchor();
		var tmp = path + ".tmp";
		var zip = new ZipPacker();
		var err = zip.Open(tmp);
		if (err != Error.Ok) return err;
		var dto = new ProjectDto { width = W, height = H, current = Current, anchor = new[] { Pivot.X, Pivot.Y }, baseW = BaseW, baseD = BaseD, viewsOn = ViewOn, gameId = GameId, exportDir = ExportDir, palette = Palette.ToDto() };
		for (int i = 0; i < Layers.Count; i++)
		{
			var l = Layers[i];
			var file = $"layers/{i}.png";
			dto.layers.Add(new LayerDto { name = l.Name, visible = l.Visible, locked = l.Locked, reference = l.Reference, opacity = l.Opacity, views = l.Views, file = file });
			zip.StartFile(file);
			zip.WriteFile(l.Img.SavePngToBuffer());
			zip.CloseFile();
		}
		zip.StartFile("project.json");
		zip.WriteFile(JsonSerializer.SerializeToUtf8Bytes(dto, new JsonSerializerOptions { WriteIndented = true }));
		zip.CloseFile();
		zip.Close();
		try { System.IO.File.Move(tmp, path, true); }
		catch (Exception e) { GD.PrintErr(e.Message); return Error.CantCreate; }
		return Error.Ok;
	}

	/// <summary>Every layer as its own PNG: &lt;base&gt;_01_&lt;name&gt;.png from the bottom up. Returns how many.</summary>
	public int ExportLayers(string dir, string baseName)
	{
		Anchor();
		var bad = System.IO.Path.GetInvalidFileNameChars();
		int n = 0;
		for (int i = 0; i < Layers.Count; i++)
		{
			var name = new string(Layers[i].Name.Select(ch => bad.Contains(ch) || ch == ' ' ? '_' : ch).ToArray());
			if (Layers[i].Img.SavePng(System.IO.Path.Combine(dir, $"{baseName}_{i + 1:00}_{name}.png")) == Error.Ok) n++;
		}
		return n;
	}

	public static Doc Load(string path)
	{
		var zip = new ZipReader();
		if (zip.Open(path) != Error.Ok) return null;
		try
		{
			var dto = JsonSerializer.Deserialize<ProjectDto>(zip.ReadFile("project.json"));
			var d = new Doc(dto.width, dto.height, false) { Path = path };
			foreach (var l in dto.layers)
			{
				var img = new Image();
				if (img.LoadPngFromBuffer(zip.ReadFile(l.file)) != Error.Ok) return null;
				img.Convert(Image.Format.Rgba8);
				d.Layers.Add(new Layer { Name = l.name, Visible = l.visible, Locked = l.locked, Reference = l.reference, Opacity = l.opacity, Img = img, Views = l.views is { Length: 4 } ? l.views : new[] { true, true, true, true } });
			}
			if (dto.palette != null) d.Palette = Palette.FromDto(dto.palette);
			d.Current = Math.Clamp(dto.current, 0, d.Layers.Count - 1);
			if (dto.anchor is { Length: 2 }) d.Pivot = new Vector2I(dto.anchor[0], dto.anchor[1]);
			d.BaseW = dto.baseW; d.BaseD = dto.baseD;
			if (dto.viewsOn is { Length: 4 }) d.ViewOn = dto.viewsOn;
			d.GameId = dto.gameId; d.ExportDir = dto.exportDir;
			return d.Layers.Count > 0 ? d : null;
		}
		catch (Exception e) { GD.PrintErr(e.Message); return null; }
		finally { zip.Close(); }
	}
}
