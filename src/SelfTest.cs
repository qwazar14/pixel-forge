using Godot;
using System;
using System.Linq;

namespace PixelForge;

/// <summary>Headless check: godot --headless --path . -- --selftest  (exit code 0 = ok).</summary>
public static class SelfTest
{
	static int fails;
	const string NL = "\n", TAB = "\t";

	static void Check(bool ok, string what)
	{
		GD.Print((ok ? "ok   " : "FAIL ") + what);
		if (!ok) fails++;
	}

	static int Count(Image img, Color c)
	{
		int n = 0;
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
				if (img.GetPixel(x, y) == c) n++;
		return n;
	}

	static bool Same(Image a, Image b)
	{
		if (a.GetSize() != b.GetSize()) return false;
		for (int y = 0; y < a.GetHeight(); y++)
			for (int x = 0; x < a.GetWidth(); x++)
				if (a.GetPixel(x, y) != b.GetPixel(x, y)) return false;
		return true;
	}

	static void LayersAndPalette(string dir)
	{
		var red = Color.Color8(255, 0, 0);
		var green = Color.Color8(0, 255, 0);
		var d = new Doc(8, 8);
		d.BeginStroke(); d.Plot(1, 1, red); d.EndStroke();
		d.AddLayer();
		Check(d.Layers.Count == 2 && d.Current == 1, "add layer goes above and becomes current");
		d.BeginStroke(); d.Plot(1, 1, green); d.Plot(2, 2, green); d.EndStroke();
		Check(d.PixelAt(1, 1) == green, "upper layer covers lower");

		d.Cur.Opacity = 0.5f;
		var half = d.Composite().GetPixel(1, 1);
		Check(Mathf.Abs(half.G - 0.5f) < 0.02f && Mathf.Abs(half.R - 0.5f) < 0.02f, $"50% layer blends in export ({half})");
		d.Cur.Opacity = 1;

		d.Cur.Reference = true;
		Check(d.Composite().GetPixel(1, 1) == red && d.Composite().GetPixel(2, 2).A == 0, "reference layer is not exported");
		d.Cur.Reference = false;

		d.Cur.Locked = true;
		Check(!d.BeginStroke(), "locked layer refuses strokes");
		d.Cur.Locked = false;

		d.MergeDown();
		Check(d.Layers.Count == 1 && d.Cur.Img.GetPixel(1, 1) == green && d.Cur.Img.GetPixel(2, 2) == green, "merge down");
		d.Undo();
		Check(d.Layers.Count == 2 && d.Layers[0].Img.GetPixel(1, 1) == red, "undo merge restores both layers");

		d.Select(0);
		d.DeleteLayer();
		Check(d.Layers.Count == 1 && d.Cur.Img.GetPixel(2, 2) == green, "delete bottom layer");
		d.Undo();
		Check(d.Layers.Count == 2 && d.Layers[0].Img.GetPixel(1, 1) == red && d.Current == 0, "undo delete");

		d.DuplicateLayer();
		d.MoveLayer(1);
		Check(d.Layers.Count == 3 && d.Current == 2 && d.Layers[2].Img.GetPixel(1, 1) == red, "duplicate + move up");
		d.AddLayer();
		d.Cur.Reference = true;
		d.FlattenVisible();
		Check(d.Layers.Count == 2 && d.Layers[1].Reference && d.Layers[0].Img.GetPixel(1, 1) == red && d.Layers[0].Img.GetPixel(2, 2) == green,
			"flatten keeps the reference, merges the rest");
		d.Undo();
		Check(d.Layers.Count == 4, "undo flatten");

		int n = d.ReplaceColor(green, red, true);
		Check(n == 2 && Count(d.Layers[1].Img, green) == 0 && d.PixelAt(2, 2) == red, "replace colour across the project");
		d.Undo();
		Check(d.PixelAt(2, 2) == green, "undo replace colour");

		// palette
		var p = d.Palette;
		var dark = Color.Color8(40, 20, 10); var mid = Color.Color8(90, 60, 30); var light = Color.Color8(160, 120, 70);
		p.Ramps.Add(new Ramp { Name = "дерево", Colors = { new() { C = dark }, new() { C = mid, Name = "ствол" }, new() { C = light } } });
		Check(p.Step(mid, 1) == light && p.Step(mid, -1) == dark && p.Step(light, 1) == null, "lighter / darker along the ramp");
		p.Strict = true;
		Check(p.Contains(mid) && !p.Contains(red), "only-palette check");

		var proj = dir + "/lp.pforge";
		d.Layers[1].Locked = true; d.Layers[1].Opacity = 0.25f; d.Layers[1].Name = "снег";
		d.Save(proj);
		var b = Doc.Load(proj);
		Check(b.Layers.Count == 4 && b.Layers[1].Name == "снег" && b.Layers[1].Locked && Mathf.IsEqualApprox(b.Layers[1].Opacity, 0.25f) && b.Layers[3].Reference,
			"layer props survive save/load");
		Check(b.Palette.Strict && b.Palette.Ramps[1].Name == "дерево" && b.Palette.Ramps[1].Colors[1].Name == "ствол" && b.Palette.Step(dark, 1) == mid,
			"palette with ramps survives save/load");

		p.SaveJson(dir + "/t.pal.json");
		Check(Palette.LoadJson(dir + "/t.pal.json").Ramps[1].Colors.Count == 3, ".pal.json round trip");
		System.IO.File.WriteAllText(dir + "/t.gpl", "GIMP Palette" + NL + "Name: t" + NL + "Columns: 4" + NL + "#" + NL + " 40  20  10" + TAB + "Тьма" + NL + "255 0 0 Red" + NL);
		var g = Palette.ImportRamp(dir + "/t.gpl");
		Check(g.Colors.Count == 2 && g.Colors[0].C == dark && g.Colors[0].Name == "Тьма", ".gpl import");
		System.IO.File.WriteAllText(dir + "/t.hex", "ff0000" + NL + "00ff00" + NL);
		Check(Palette.ImportRamp(dir + "/t.hex").Colors.Count == 2, ".hex import");
		var u = Palette.UniqueColors(d.Composite(), 10);
		Check(u.Count == 2 && u[0].Luminance <= u[1].Luminance, "unique colours of the image, dark first");
	}

	static void Tools()
	{
		var red = Color.Color8(255, 0, 0);
		var green = Color.Color8(0, 255, 0);
		var none = new Color(0, 0, 0, 0);

		bool ellipsesOk = true;
		for (int w = 0; w <= 12; w++)
			for (int h = 0; h <= 9; h++)
			{
				var pts = Doc.EllipsePoints(2, 3, 2 + w, 3 + h);
				ellipsesOk &= pts.All(q => q.X >= 2 && q.X <= 2 + w && q.Y >= 3 && q.Y <= 3 + h)
					&& pts.Any(q => q.X == 2) && pts.Any(q => q.X == 2 + w) && pts.Any(q => q.Y == 3) && pts.Any(q => q.Y == 3 + h)
					&& pts.All(q => pts.Contains(new Vector2I(4 + w - q.X, q.Y)) && pts.Contains(new Vector2I(q.X, 6 + h - q.Y)));
				if (!ellipsesOk) { GD.Print($"  ellipse {w}x{h} broken"); goto done; }
			}
		done:
		Check(ellipsesOk, "ellipse outlines fill their box exactly and are symmetric (0..12 × 0..9)");

		var d = new Doc(8, 8);
		d.BeginStroke(); d.Rect(0, 0, 4, 3, 1, red, false); d.EndStroke();
		Check(Count(d.Cur.Img, red) == 14, "rectangle outline 5×4 = 14 px");
		d.BeginStroke(); d.Rect(4, 3, 0, 0, 1, green, true); d.EndStroke();
		Check(Count(d.Cur.Img, green) == 20, "filled rectangle, corners given backwards");
		d.BeginStroke(); d.Ellipse(0, 0, 7, 7, 1, red, true); d.EndStroke();
		Check(d.Cur.Img.GetPixel(4, 4) == red && d.Cur.Img.GetPixel(0, 0) == green, "filled ellipse covers the middle, not the corner");

		d = new Doc(8, 8);
		d.BeginStroke(); d.Line(0, 0, 7, 7, 1, red); d.RestartStroke(); d.Line(0, 7, 7, 7, 1, red); d.EndStroke();
		Check(Count(d.Cur.Img, red) == 8 && d.Cur.Img.GetPixel(3, 3) == none, "shape preview restart leaves only the final shape");
		d.Undo();
		Check(Count(d.Cur.Img, red) == 0, "…and undoes in one step");

		d.BeginStroke(); d.IsoLine(0, 0, 1, 1, 4, 1, red); d.EndStroke();
		Check(d.Cur.Img.GetPixel(1, 0) == red && d.Cur.Img.GetPixel(2, 1) == red && d.Cur.Img.GetPixel(3, 1) == red && d.Cur.Img.GetPixel(4, 2) == red && Count(d.Cur.Img, red) == 5,
			"iso line goes two across per one down");

		d = new Doc(8, 8);
		d.BeginStroke(); d.Line(4, 0, 4, 7, 1, red); d.EndStroke();
		d.BeginStroke(false); d.Fill(0, 0, green, 0, true); d.EndStroke();
		Check(Count(d.Cur.Img, green) == 32, "contiguous fill stops at the wall");
		d.Undo();
		d.BeginStroke(false); d.Fill(0, 0, green, 0, false); d.EndStroke();
		Check(Count(d.Cur.Img, green) == 56, "global fill takes every match");
		d.Undo();
		d.BeginStroke(); d.Plot(6, 6, Color.Color8(250, 0, 0)); d.EndStroke();
		d.BeginStroke(false); d.Fill(4, 0, green, 0, true); d.EndStroke();
		Check(Count(d.Cur.Img, green) == 8, "tolerance 0 skips a near colour");
		d.Undo();
		d.BeginStroke(false); d.Fill(4, 0, green, 10, false); d.EndStroke();
		Check(Count(d.Cur.Img, green) == 9, "tolerance 10 takes it");

		d = new Doc(8, 8) { SymX = true, SymY = true };
		d.BeginStroke(); d.Plot(1, 2, red); d.EndStroke();
		Check(Count(d.Cur.Img, red) == 4 && d.Cur.Img.GetPixel(6, 2) == red && d.Cur.Img.GetPixel(1, 5) == red && d.Cur.Img.GetPixel(6, 5) == red, "symmetry in both axes");
		d.AxisX2 = 5; d.SymY = false; // axis through the middle of pixel 2
		d.BeginStroke(); d.Plot(0, 0, green); d.EndStroke();
		Check(d.Cur.Img.GetPixel(4, 0) == green, "moved axis mirrors around pixel 2");

		// selection
		d = new Doc(8, 8);
		d.Sel = new Rect2I(0, 0, 2, 2);
		d.BeginStroke(); d.Stamp(1, 1, 3, red); d.EndStroke();
		Check(Count(d.Cur.Img, red) == 4, "painting is clipped to the selection");
		Check(d.Lift(), "lift");
		d.MoveFloat(new Vector2I(5, 5));
		d.Anchor();
		Check(d.Cur.Img.GetPixel(0, 0) == none && d.Cur.Img.GetPixel(5, 5) == red && d.Cur.Img.GetPixel(6, 6) == red && d.Sel == new Rect2I(5, 5, 2, 2), "move selection");
		d.Undo();
		Check(d.Cur.Img.GetPixel(0, 0) == red && d.Cur.Img.GetPixel(5, 5) == none, "undo move in one step");
		d.Sel = new Rect2I(0, 0, 2, 2);
		d.Lift(); d.MoveFloat(new Vector2I(3, 0));
		d.Undo();
		Check(d.Float == null && d.Cur.Img.GetPixel(0, 0) == red && Count(d.Cur.Img, red) == 4, "undo while floating puts it back");

		d.Sel = new Rect2I(0, 0, 2, 1);
		d.Copy();
		d.Deselect();
		d.Paste();
		d.MoveFloat(new Vector2I(6, 7));
		d.Deselect();
		Check(Count(d.Cur.Img, red) == 6 && d.Cur.Img.GetPixel(7, 7) == red, "copy + paste + move");

		d = new Doc(4, 4);
		d.BeginStroke(); d.Plot(0, 1, red); d.EndStroke();
		d.FlipH();
		Check(d.Cur.Img.GetPixel(3, 1) == red && d.Cur.Img.GetPixel(0, 1) == none && d.Float == null && d.Sel == null, "flip the whole layer");
		d.FlipV();
		Check(d.Cur.Img.GetPixel(3, 2) == red, "flip vertically");
		d.Sel = new Rect2I(2, 2, 2, 1); // red at (3,2), nothing at (2,2)
		d.Rotate(true);
		Check(d.Float.GetSize() == new Vector2I(1, 2) && d.Float.GetPixel(0, 1) == red, "rotate selection 90° clockwise");
		d.Anchor();
		d.Sel = null;
		d.BeginStroke(); d.Plot(0, 0, red); d.EndStroke();
		d.Sel = new Rect2I(0, 0, 1, 1);
		d.Cut();
		Check(d.Cur.Img.GetPixel(0, 0) == none, "cut clears");
	}

	/// <summary>Drives the real canvas with synthetic mouse events (zoom 10, canvas at the origin).</summary>
	static void Canvas(Node root)
	{
		var red = Color.Color8(255, 0, 0);
		var v = new CanvasView { Size = new Vector2(400, 400) };
		root.AddChild(v);
		var d = new Doc(16, 16);
		v.SetDoc(d);
		v.Zoom = 10; v.Offset = Vector2.Zero; v.Fg = red;
		Vector2 At(int x, int y) => new(x * 10 + 5, y * 10 + 5);
		void Down(int x, int y, bool shift = false) => v._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = At(x, y), ShiftPressed = shift });
		void Up(int x, int y) => v._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = At(x, y) });
		void To(int x, int y, bool shift = false) => v._GuiInput(new InputEventMouseMotion { Position = At(x, y), ShiftPressed = shift });

		v.Tool = Tool.Pencil;
		Down(1, 1); To(5, 1); Up(5, 1);
		Check(Count(d.Cur.Img, red) == 5, "canvas: pencil drag draws a line");

		v.Tool = Tool.Rect;
		Down(0, 3); To(9, 9); To(4, 6); Up(4, 6);
		Check(Count(d.Cur.Img, red) == 5 + 14 && d.Cur.Img.GetPixel(9, 9).A == 0, "canvas: rectangle preview follows the mouse, final 5×4");

		v.Tool = Tool.Line;
		Down(0, 12); To(7, 14, true); Up(7, 14);
		Check(d.Cur.Img.GetPixel(1, 12) == red && d.Cur.Img.GetPixel(2, 13) == red && d.Cur.Img.GetPixel(4, 14) == red, "canvas: shift-line snaps to 2:1");
		int before = Count(d.Cur.Img, red);

		v.Tool = Tool.Select;
		Down(0, 0); To(5, 1); Up(5, 1);
		Check(d.Sel == new Rect2I(0, 0, 6, 2), "canvas: drag a selection");
		Down(2, 1); To(2, 9); Up(2, 9);
		Check(d.Float != null && d.FloatPos == new Vector2I(0, 8), "canvas: drag inside lifts and moves it");
		d.Anchor();
		Check(d.Cur.Img.GetPixel(1, 9) == red && d.Cur.Img.GetPixel(1, 1).A == 0 && Count(d.Cur.Img, red) == before, "canvas: anchored where dropped");
		Down(12, 12); Up(12, 12);
		Check(d.Sel == null, "canvas: click outside deselects");
		v.QueueFree();
	}

	static void ImportAndResize(string dir)
	{
		var red = Color.Color8(255, 0, 0);
		var blue = Color.Color8(0, 0, 255);
		var d = new Doc(8, 8);
		d.BeginStroke(); d.Plot(0, 0, red); d.Plot(7, 7, blue); d.EndStroke();

		d.Resize(12, 10, d.ResizeOffset(12, 10, 1, 1));
		Check(d.W == 12 && d.H == 10 && d.Cur.Img.GetSize() == new Vector2I(12, 10) && d.Cur.Img.GetPixel(2, 1) == red && d.Cur.Img.GetPixel(9, 8) == blue,
			"resize anchored at the centre moves content by half the growth");
		Check(d.Pivot == new Vector2I(6, 8), $"anchor moves with the content ({d.Pivot})");
		d.Undo();
		Check(d.W == 8 && d.Cur.Img.GetSize() == new Vector2I(8, 8) && d.Cur.Img.GetPixel(0, 0) == red && d.Pivot == new Vector2I(4, 7), "undo resize");
		d.Undo();
		Check(Count(d.Cur.Img, red) == 0, "pixel undo still works after a resize undo");
		d.Redo(); d.Redo();
		Check(d.W == 12 && d.Cur.Img.GetPixel(9, 8) == blue, "redo resize");
		d.Resize(6, 6, d.ResizeOffset(6, 6, 2, 2));
		Check(d.Cur.Img.GetPixel(3, 4) == blue && Count(d.Cur.Img, red) == 0, "shrink anchored bottom-right crops the top-left");

		var img = Image.CreateEmpty(3, 2, false, Image.Format.Rgba8);
		img.Fill(red);
		img.SetPixel(1, 1, blue); // bottom-middle pixel
		d = new Doc(10, 10);
		d.ImportLayer(img, "a", Place.Anchor, false);
		Check(d.Layers.Count == 2 && d.Cur.Name == "a" && d.Cur.Img.GetPixel(d.Pivot.X, d.Pivot.Y) == blue && d.Cur.Img.GetPixel(4, 8) == red,
			"anchored import puts the bottom-middle pixel on the anchor");
		d.ImportLayer(img, "b", Place.Center, true);
		Check(d.Cur.Reference && d.Cur.Img.GetPixel(4, 5) == blue && d.Cur.Img.GetPixel(3, 4) == red, "centred import as a reference");
		d.ImportLayer(img, "c", Place.TopLeft, false);
		Check(d.Cur.Img.GetPixel(1, 1) == blue, "top-left import");
		var big = Image.CreateEmpty(14, 4, false, Image.Format.Rgba8);
		big.Fill(red);
		d.ImportLayer(big, "big", Place.Center, false);
		Check(Count(d.Cur.Img, red) == 40, "too wide image is clipped, not stretched");
		d.Undo(); d.Undo(); d.Undo(); d.Undo();
		Check(d.Layers.Count == 1, "imports undo");

		d.Pivot = new Vector2I(3, 9);
		d.Save(dir + "/pivot.pforge");
		Check(Doc.Load(dir + "/pivot.pforge").Pivot == new Vector2I(3, 9), "anchor survives save/load");
	}

	static void Game(string dir)
	{
		var red = Color.Color8(255, 0, 0);
		var white = Color.Color8(250, 250, 255);
		var none = new Color(0, 0, 0, 0);

		var d = new Doc(20, 20) { Pivot = new Vector2I(10, 15) };
		d.BeginStroke(); d.Plot(4, 5, red); d.Plot(12, 15, red); d.EndStroke();
		var c = d.GameCrop(d.ViewImage(0), out bool cut);
		Check(c.GetSize() == new Vector2I(13, 11) && c.GetPixel(0, 0) == red && c.GetPixel(8, 10) == red && !cut,
			$"game crop: symmetric about the anchor, anchor bottom-middle ({c.GetSize()})");
		d.BeginStroke(); d.Plot(10, 17, red); d.EndStroke();
		d.GameCrop(d.ViewImage(0), out cut);
		Check(cut, "pixels under the anchor are reported");
		d.Undo();

		d.AddLayer();
		d.Cur.Name = "снег";
		d.Cur.Views = new[] { false, true, false, true };
		d.BeginStroke(); d.Plot(10, 2, white); d.EndStroke();
		d.ViewOn = new[] { true, true, false, true };
		var outDir = dir + "/game";
		System.IO.Directory.CreateDirectory(outDir);
		foreach (var f in System.IO.Directory.GetFiles(outDir)) System.IO.File.Delete(f);
		var report = d.ExportGame(outDir, "hut");
		var summer = Image.LoadFromFile(outDir + "/hut.png");
		var winter = Image.LoadFromFile(outDir + "/hut_winter.png");
		Check(summer != null && winter != null && !System.IO.File.Exists(outDir + "/hut_turn.png"), "export writes the switched-on views: " + string.Join("; ", report));
		Check(summer.GetSize() == new Vector2I(13, 11) && winter.GetSize() == new Vector2I(13, 14) && winter.GetPixel(6, 0) == white,
			"snow layer only in the winter picture, which grows to hold it");
		Check(System.IO.File.Exists(outDir + "/hut_turn_winter.png"), "turned winter view written too");

		d.Save(dir + "/game.pforge");
		var b = Doc.Load(dir + "/game.pforge");
		Check(b.Layers[1].Views[1] && !b.Layers[1].Views[0] && b.ViewOn[3] && !b.ViewOn[2] && b.GameId == "hut" && b.ExportDir == outDir, "views, id and export folder survive save/load");

		d = new Doc(9, 3) { Pivot = new Vector2I(3, 2) };
		d.BeginStroke(); d.Plot(1, 0, red); d.EndStroke();
		d.MirrorAll();
		Check(d.Cur.Img.GetPixel(5, 0) == red && d.Cur.Img.GetPixel(1, 0) == none && d.Pivot == new Vector2I(3, 2), "mirror the canvas around the anchor");
		d.Undo();
		d.SetPivot(new Vector2I(7, 1));
		d.Undo();
		Check(d.Cur.Img.GetPixel(1, 0) == red && d.Pivot == new Vector2I(3, 2), "anchor move and mirror undo");

		var nb = Doc.ForBuilding(6, 5, 80);
		Check(nb.W % 2 == 1 && nb.Pivot.X == nb.W / 2 && nb.Pivot.X - 6 * 9 >= 0 && nb.Pivot.X + 5 * 9 < nb.W && nb.Pivot.Y - 50 - 80 >= 0 && nb.BaseW == 6,
			$"building canvas fits footprint and height ({nb.W}×{nb.H}, anchor {nb.Pivot})");

		var all = Sketch.List(Sketch.Dir);
		var izba = all.FirstOrDefault(x => x.Name == "izba");
		var izbaT = all.FirstOrDefault(x => x.Name == "izba_turn");
		if (izba == null) { GD.Print("skip  rf-game sketches not found"); return; }
		Check(izba.Fx == 59 && izba.Fy == 107 && izba.W == 6 && izba.D == 5, "index.json read");
		var src = izba.Load();
		var sd = izba.NewDoc();
		sd.Save(dir + "/izba.pforge");
		var refL = sd.Layers[0];
		Check(refL.Reference && refL.Img.GetPixel(sd.Pivot.X, sd.Pivot.Y) == src.GetPixel(59, 107) && sd.BaseW == 6 && sd.BaseD == 5 && !sd.Turned && sd.GameId == "izba",
			"sketch opens as a reference with its front corner on the anchor");
		var td = izbaT.NewDoc();
		Check(td.Turned && td.BaseW == 6 && td.BaseD == 5 && td.ViewOn[2] && !td.ViewOn[0], "turned sketch: unturned W×D kept, turned view on");

		// the real pipeline: the sketch painted over as a normal layer comes out with the anchor bottom-middle
		sd.ImportLayerAt(src, "краска", sd.Pivot - izba.Corner(src), false);
		var ex = sd.GameCrop(sd.ViewImage(0), out _);
		int half = ex.GetWidth() / 2, dx = half - 59;
		bool same = ex.GetHeight() == 108 && ex.GetWidth() == 2 * Math.Max(59, src.GetWidth() - 1 - 59) + 1;
		for (int y = 0; y < 108 && same; y++)
			for (int x = 0; x < src.GetWidth() && same; x++)
				same = ex.GetPixel(x + dx, y) == src.GetPixel(x, y);
		Check(same, $"izba exported {ex.GetSize()}: the art is unchanged, front corner at the bottom middle");
	}

	static void Files(string dir)
	{
		var d = new Doc(8, 8);
		d.BeginStroke(); d.Plot(1, 1, Color.Color8(255, 0, 0)); d.EndStroke();
		d.Layers[0].Name = "стена: кирпич";
		d.AddLayer();
		var auto = dir + "/a.pforge.autosave";
		Check(d.Write(auto) == Error.Ok && d.Dirty && d.Path == null && !System.IO.File.Exists(auto + ".tmp") && Doc.Load(auto)?.Layers.Count == 2,
			"autosave write leaves the document unsaved and no temp file behind");
		var ldir = dir + "/layers";
		System.IO.Directory.CreateDirectory(ldir);
		foreach (var f in System.IO.Directory.GetFiles(ldir)) System.IO.File.Delete(f);
		Check(d.ExportLayers(ldir, "hut") == 2 && System.IO.File.Exists(ldir + "/hut_01_стена__кирпич.png") && System.IO.File.Exists(ldir + "/hut_02_Слой_2.png"),
			"layer sheet: one PNG per layer, names made file-safe");
	}

	static void IsoAndArrays(Node root)
	{
		var red = Color.Color8(255, 0, 0);
		var blue = Color.Color8(0, 0, 255);
		var none = new Color(0, 0, 0, 0);

		var st = new Vector2I(10, 10);
		var pts = Doc.IsoRectPoints(st, st + new Vector2I(2 * 3 + 2 * 2, 3 - 2)); // a = 3 along (2,1), b = 2 along (2,-1)
		Check(pts.Contains(st) && pts.Contains(new Vector2I(16, 13)) && pts.Contains(new Vector2I(14, 8)) && pts.Contains(new Vector2I(20, 11))
			&& pts.Count(q => q.Y == 13) == 2 && pts.Count(q => q.Y == 8) == 2, "iso rectangle: corners on 2:1 sides, pairs at the tips");
		var d = new Doc(32, 32);
		d.BeginStroke(); d.IsoRect(st, new Vector2I(20, 11), 1, red, true); d.EndStroke();
		Check(d.Cur.Img.GetPixel(15, 10) == red && d.Cur.Img.GetPixel(10, 13).A == 0, "filled iso rectangle covers its inside only");

		d = new Doc(16, 16);
		d.BeginStroke(); d.Plot(0, 0, red); d.EndStroke();
		d.Sel = new Rect2I(0, 0, 1, 1);
		Check(d.ArrayCopies(4, new Vector2I(2, 1)), "array of 4");
		Check(Count(d.Cur.Img, red) == 4 && d.Cur.Img.GetPixel(6, 3) == red && d.Sel == new Rect2I(6, 3, 1, 1) && d.Float == null, "array copies along 2:1, selection ends on the last copy");
		Check(d.StepRepeat() && d.Cur.Img.GetPixel(8, 4) == red && Count(d.Cur.Img, red) == 5 && d.Sel == new Rect2I(8, 4, 1, 1), "repeat step adds one copy and moves the selection onto it");
		d.Undo(); d.Undo();
		Check(Count(d.Cur.Img, red) == 1, "array and repeat undo in one step each");

		d.Sel = new Rect2I(0, 0, 2, 2);
		d.LayerViaCopy();
		Check(d.Layers.Count == 2 && d.Current == 1 && d.Cur.Img.GetPixel(0, 0) == red && Count(d.Cur.Img, red) == 1, "Ctrl+J copies the selection to a new layer");
		d.FillSelection(blue);
		Check(Count(d.Cur.Img, blue) == 4, "fill the selection");
		Check(d.Nudge(new Vector2I(10, 0)) && d.Float != null, "arrow nudge lifts");
		d.Anchor();
		Check(d.Cur.Img.GetPixel(10, 0) == blue && d.Cur.Img.GetPixel(0, 0).A == 0, "nudged 10 px");
		d.Sel = null;
		d.LayerViaCopy();
		Check(d.Layers.Count == 3, "Ctrl+J without a selection duplicates the layer");

		// canvas: iso mode and the Move tool
		var v = new CanvasView { Size = new Vector2(400, 400) };
		root.AddChild(v);
		d = new Doc(32, 32);
		v.SetDoc(d);
		v.Zoom = 10; v.Offset = Vector2.Zero; v.Fg = red;
		Vector2 At(int x, int y) => new(x * 10 + 5, y * 10 + 5);
		void Down(int x, int y, bool alt = false) => v._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = At(x, y), AltPressed = alt });
		void Up(int x, int y) => v._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = At(x, y) });
		void To(int x, int y) => v._GuiInput(new InputEventMouseMotion { Position = At(x, y) });

		v.IsoMode = true;
		v.Tool = Tool.Line;
		Down(0, 0); To(9, 5); Up(9, 5);
		Check(d.Cur.Img.GetPixel(1, 0) == red && d.Cur.Img.GetPixel(2, 1) == red && d.Cur.Img.GetPixel(3, 1) == red, "iso mode: line snaps to 2:1 without Shift");
		v.Tool = Tool.Ellipse;
		Down(0, 10); To(20, 14); Up(20, 14);
		var used = d.Cur.Img.GetRegion(new Rect2I(0, 10, 32, 22)).GetUsedRect();
		Check(used.Size.X == 2 * used.Size.Y - 1 || used.Size.X == 2 * used.Size.Y || used.Size.X == 2 * used.Size.Y + 1, $"iso mode: ellipse is twice as wide as tall ({used.Size})");
		d.Undo(); d.Undo();
		v.IsoMode = false;

		d.BeginStroke(); d.Plot(1, 1, red); d.EndStroke();
		v.Tool = Tool.Move;
		Down(5, 5); To(8, 7); Up(8, 7);
		d.Anchor();
		Check(d.Cur.Img.GetPixel(4, 3) == red && d.Cur.Img.GetPixel(1, 1).A == 0, "move tool drags the whole layer without a selection");
		d.Sel = new Rect2I(4, 3, 1, 1);
		v.Tool = Tool.Select;
		Down(4, 3, alt: true); To(4, 9); Up(4, 9);
		d.Anchor();
		Check(d.Cur.Img.GetPixel(4, 3) == red && d.Cur.Img.GetPixel(4, 9) == red, "Alt-drag with the selection tool leaves a copy");
		v.QueueFree();
	}

	static void ShadingTools(Node root, string dir)
	{
		var red = Color.Color8(255, 0, 0);
		var blue = Color.Color8(0, 0, 255);
		var none = new Color(0, 0, 0, 0);
		var warm = Enumerable.Range(0, 4).Select(i => Color.Color8((byte)(60 + 50 * i), (byte)(30 + 40 * i), 20)).ToArray();
		var cool = Enumerable.Range(0, 3).Select(i => Color.Color8((byte)(40 + 40 * i), (byte)(40 + 40 * i), (byte)(70 + 50 * i))).ToArray();

		var d = new Doc(16, 16);
		d.Palette.Ramps.Add(new Ramp { Name = "дерево", Colors = warm.Select(c => new PalColor { C = c }).ToList() });
		d.Palette.Ramps.Add(new Ramp { Name = "тень", Colors = cool.Select(c => new PalColor { C = c }).ToList() });
		d.BeginStroke(); d.Rect(0, 0, 7, 0, 1, warm[1], true); d.Plot(0, 1, red); d.EndStroke();

		d.BeginStroke(); d.Shade(1, 0, 1, 1); d.Shade(1, 0, 1, 1); d.Shade(0, 1, 1, 1); d.EndStroke();
		Check(d.Cur.Img.GetPixel(1, 0) == warm[2] && d.Cur.Img.GetPixel(0, 1) == red && d.Cur.Img.GetPixel(2, 0) == warm[1],
			"shade: one step lighter once per stroke, colours outside the palette untouched");
		d.BeginStroke(); d.Shade(2, 0, 3, -1); d.EndStroke();
		Check(d.Cur.Img.GetPixel(2, 0) == warm[0] && d.Cur.Img.GetPixel(3, 0) == warm[0], "shade: 3-px brush darker");
		d.Undo(); d.Undo();
		Check(Count(d.Cur.Img, warm[1]) == 8, "shade strokes undo");

		d.BeginStroke(); d.Plot(0, 0, warm[3]); d.EndStroke();
		d.Sel = new Rect2I(0, 0, 8, 1);
		int n = d.ShiftRamp(0, d.Palette.Ramps[2]);
		Check(n == 8 && d.Cur.Img.GetPixel(0, 0) == cool[2] && d.Cur.Img.GetPixel(1, 0) == cool[1] && d.Cur.Img.GetPixel(0, 1) == red,
			"lit wood onto the shadow ramp, same place by share of length, selection only");
		d.ShiftRamp(-1, null);
		Check(d.Cur.Img.GetPixel(0, 0) == cool[1] && d.Cur.Img.GetPixel(1, 0) == cool[0], "then one step darker in its own ramp");
		d.Sel = null;

		var flat = Image.CreateEmpty(4, 1, false, Image.Format.Rgba8);
		flat.Fill(red);
		var sk = Doc.IsoSkew(flat, 1);
		Check(sk.GetSize() == new Vector2I(4, 2) && sk.GetPixel(1, 0) == red && sk.GetPixel(2, 1) == red && sk.GetPixel(2, 0) == none, "iso skew down: pairs drop a row");
		sk = Doc.IsoSkew(flat, -1);
		Check(sk.GetPixel(0, 1) == red && sk.GetPixel(3, 0) == red, "iso skew up");
		d = new Doc(16, 16);
		d.Sel = new Rect2I(2, 2, 6, 3);
		d.FillSelection(red);
		d.Skew(1);
		Check(d.Float != null && d.Float.GetSize() == new Vector2I(6, 5), "skew a selection: it floats, taller by half its width");
		d.Anchor();
		Check(Count(d.Cur.Img, red) == 18, "skewed piece keeps its pixels");

		var wall = Doc.IsoWallPoints(new Vector2I(0, 0), new Vector2I(6, 8), 1);
		Check(wall.Contains(new Vector2I(6, 3)) && wall.Contains(new Vector2I(6, 8)) && wall.Contains(new Vector2I(0, 5)) && wall.Contains(new Vector2I(3, 1)) && !wall.Contains(new Vector2I(3, 3)),
			"iso wall: vertical sides, top and bottom 2:1");

		d = new Doc(8, 8);
		d.BeginStroke(); d.Plot(1, 1, red); d.EndStroke();
		d.Cur.LockAlpha = true;
		d.FillSelection(blue);
		Check(Count(d.Cur.Img, blue) == 1 && d.Cur.Img.GetPixel(1, 1) == blue, "lock transparency: fill recolours only what's there");
		d.BeginStroke(); d.Line(0, 0, 7, 7, 1, red); d.EndStroke();
		Check(Count(d.Cur.Img, red) == 1 && d.Cur.Img.GetPixel(1, 1) == red, "lock transparency: a line paints only over the pixel");
		d.Save(dir + "/alpha.pforge");
		Check(Doc.Load(dir + "/alpha.pforge").Layers[0].LockAlpha, "lock transparency survives save/load");

		// canvas: shade brush and iso wall
		var v = new CanvasView { Size = new Vector2(400, 400) };
		root.AddChild(v);
		d = new Doc(32, 32);
		d.Palette.Ramps.Add(new Ramp { Name = "дерево", Colors = warm.Select(c => new PalColor { C = c }).ToList() });
		v.SetDoc(d);
		v.Zoom = 10; v.Offset = Vector2.Zero; v.Fg = warm[1];
		Vector2 At(int x, int y) => new(x * 10 + 5, y * 10 + 5);
		void Down(int x, int y, MouseButton b = MouseButton.Left) => v._GuiInput(new InputEventMouseButton { ButtonIndex = b, Pressed = true, Position = At(x, y) });
		void Up(int x, int y, MouseButton b = MouseButton.Left) => v._GuiInput(new InputEventMouseButton { ButtonIndex = b, Pressed = false, Position = At(x, y) });
		void To(int x, int y) => v._GuiInput(new InputEventMouseMotion { Position = At(x, y) });
		v.IsoMode = true; v.IsoShape = IsoShape.WallDown; v.FillShapes = true; v.Tool = Tool.Rect;
		Down(0, 0); To(6, 8); Up(6, 8);
		Check(d.Cur.Img.GetPixel(3, 3) == warm[1] && d.Cur.Img.GetPixel(6, 8) == warm[1] && d.Cur.Img.GetPixel(6, 1).A == 0, "canvas: iso wall drawn and filled");
		v.Tool = Tool.Shade;
		Down(0, 2, MouseButton.Right); To(5, 2); To(0, 2); Up(0, 2, MouseButton.Right);
		Check(d.Cur.Img.GetPixel(2, 2) == warm[0] && d.Cur.Img.GetPixel(2, 4) == warm[1], "canvas: shade brush darkens once even when going back over");
		v.QueueFree();
	}

	public static int Run(Node root)
	{
		var red = Color.Color8(255, 0, 0);
		var blue = Color.Color8(0, 0, 255, 128);
		var d = new Doc(16, 16);

		d.BeginStroke(); d.Line(0, 0, 15, 7, 1, red); d.EndStroke();
		Check(Count(d.Cur.Img, red) == 16 && d.Cur.Img.GetPixel(15, 7) == red, "line 0,0→15,7: 16 pixels, one per column");

		d.BeginStroke(); d.Stamp(5, 10, 3, blue); d.Stamp(0, 0, 2, blue); d.EndStroke();
		Check(Count(d.Cur.Img, blue) == 9 + 4, "3×3 stamp + clipped 2×2 stamp at the corner");

		var before = (Image)d.Cur.Img.Duplicate();
		d.Undo();
		Check(Count(d.Cur.Img, blue) == 0 && d.Cur.Img.GetPixel(0, 0) == red, "undo restores the line pixel under the stamp");
		d.Undo();
		Check(Count(d.Cur.Img, red) == 0, "undo clears the line");
		Check(!d.Undo(), "undo stops at the start");
		d.Redo(); d.Redo();
		Check(Same(d.Cur.Img, before), "redo replays both strokes");

		for (int i = 0; i < 150; i++) { d.BeginStroke(); d.Plot(i % 16, 15, Color.Color8((byte)i, 1, 1)); d.EndStroke(); }
		int undone = 0;
		while (d.Undo()) undone++;
		Check(undone >= 100, $"undo depth {undone} ≥ 100");
		while (d.Redo()) { }

		var dir = ProjectSettings.GlobalizePath("user://selftest");
		DirAccess.MakeDirRecursiveAbsolute(dir);
		var proj = dir + "/t.pforge";
		Check(d.Save(proj) == Error.Ok && !d.Dirty, "save .pforge");
		var back = Doc.Load(proj);
		Check(back != null && back.W == 16 && Same(back.Cur.Img, d.Cur.Img), "load .pforge gives the same pixels");

		var png = dir + "/t.png";
		Check(d.ExportPng(png) == Error.Ok, "export png");
		var img = Image.LoadFromFile(png);
		img.Convert(Image.Format.Rgba8);
		Check(Same(img, d.Composite()), "exported png matches the canvas");

		LayersAndPalette(dir);
		Tools();
		Canvas(root);
		ImportAndResize(dir);
		Game(dir);
		Files(dir);
		IsoAndArrays(root);
		ShadingTools(root, dir);

		GD.Print(fails == 0 ? "SELFTEST PASSED" : $"SELFTEST FAILED: {fails}");
		return fails == 0 ? 0 : 1;
	}
}
