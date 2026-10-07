using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelForge;

public enum Tool { Move, Select, Pencil, Eraser, Shade, Fill, Picker, Line, Rect, Ellipse, Anchor, Hand }

/// <summary>What the rectangle draws in iso mode: a diamond on the ground or a wall running down / up to the right.</summary>
public enum IsoShape { Floor, WallDown, WallUp }

/// <summary>Shows the document and turns mouse input into strokes, zoom and pan.</summary>
public partial class CanvasView : Control
{
	public Doc Doc;
	public int Zoom = 8;
	public Vector2 Offset; // screen position of pixel (0, 0)
	public Tool Tool = Tool.Pencil;
	public int BrushSize = 1;
	public bool FillShapes, Contiguous = true;
	/// <summary>Isometric mode: lines snap to 2:1, rectangles lie on the ground as 2:1 diamonds, ellipses are iso circles.</summary>
	public bool IsoMode;
	public IsoShape IsoShape;
	public int Tolerance;
	public Color Fg = Colors.Black, Bg = Colors.White;
	public bool ShowGrid = true, ShowIso, ShowBase = true, ShowLight;
	public Vector2I Hover = new(-1, -1);

	public event Action StatusChanged; // zoom, hover, selection
	public event Action ColorsChanged;
	public event Action<string> Message;

	enum Drag { None, Pan, Pick, Paint, Shape, Select, Move }
	Drag drag;
	MouseButton dragButton;
	Vector2I start, last, floatStart;
	bool moved;

	readonly Dictionary<Layer, ImageTexture> tex = new();
	ImageTexture floatTex;
	Image floatSrc;
	Texture2D checker;
	bool fitPending;

	public override void _Ready()
	{
		TextureFilter = TextureFilterEnum.Nearest;
		TextureRepeat = TextureRepeatEnum.Enabled;
		ClipContents = true;
		FocusMode = FocusModeEnum.Click;
		MouseDefaultCursorShape = CursorShape.Cross;
		var c = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
		c.Fill(new Color(0.40f, 0.40f, 0.42f));
		c.FillRect(new Rect2I(0, 0, 8, 8), new Color(0.30f, 0.30f, 0.32f));
		c.FillRect(new Rect2I(8, 8, 8, 8), new Color(0.30f, 0.30f, 0.32f));
		checker = ImageTexture.CreateFromImage(c);
		Resized += () => { if (fitPending) Fit(); };
	}

	public void SetDoc(Doc d)
	{
		Doc = d;
		tex.Clear();
		drag = Drag.None;
		Doc.Changed += Refresh;
		fitPending = true;
		Fit();
		Refresh();
	}

	/// <summary>Largest integer zoom that fits, centred.</summary>
	public void Fit()
	{
		if (Doc == null || Size.X < 1) return;
		fitPending = false;
		int z = (int)Mathf.Min((Size.X - 40) / Doc.W, (Size.Y - 40) / Doc.H);
		Zoom = Math.Clamp(z, 1, 64);
		Offset = ((Size - new Vector2(Doc.W, Doc.H) * Zoom) / 2).Floor();
		StatusChanged?.Invoke();
		QueueRedraw();
	}

	public void Refresh()
	{
		foreach (var gone in tex.Keys.Where(l => !Doc.Layers.Contains(l)).ToList()) tex.Remove(gone);
		foreach (var l in Doc.Layers)
		{
			if (tex.TryGetValue(l, out var t) && t.GetSize() == new Vector2(Doc.W, Doc.H)) t.Update(l.Img);
			else tex[l] = ImageTexture.CreateFromImage(l.Img);
		}
		StatusChanged?.Invoke();
		QueueRedraw();
	}

	void RefreshCurrent()
	{
		if (tex.TryGetValue(Doc.Cur, out var t)) t.Update(Doc.Cur.Img);
		QueueRedraw();
	}

	Vector2I ToPixel(Vector2 p) => (Vector2I)((p - Offset) / Zoom).Floor();

	void SetZoom(int z, Vector2 around)
	{
		z = Math.Clamp(z, 1, 64);
		if (z == Zoom) return;
		var px = (around - Offset) / Zoom;
		Zoom = z;
		Offset = (around - px * Zoom).Round();
		StatusChanged?.Invoke();
		QueueRedraw();
	}

	public void SetZoomCentered(int z) => SetZoom(z, Size / 2);

	public void ZoomStep(int dir, Vector2? around = null)
	{
		// doubling feels right above 8x, single steps below
		int z = dir > 0 ? (Zoom < 8 ? Zoom + 1 : Zoom * 2) : (Zoom <= 8 ? Zoom - 1 : Zoom / 2);
		SetZoom(z, around ?? Size / 2);
	}

	Color PaintColor() => Tool == Tool.Eraser ? new Color(0, 0, 0, 0) : dragButton == MouseButton.Right ? Bg : Fg;

	/// <summary>Opens a stroke if the colour and the layer allow it, otherwise says why not.</summary>
	bool Begin(bool mirrored = true)
	{
		var c = PaintColor();
		if (Tool is not (Tool.Eraser or Tool.Shade) && Doc.Palette.Strict && !Doc.Palette.Contains(c)) { Message?.Invoke("цвет не из палитры (режим «только палитра»)"); return false; }
		if (Doc.BeginStroke(mirrored)) return true;
		Message?.Invoke(Doc.Cur.Locked ? "слой заблокирован" : "слой скрыт");
		return false;
	}

	public override void _GuiInput(InputEvent e)
	{
		if (Doc == null) return;
		if (e is InputEventMouseButton mb) MouseButtonInput(mb);
		else if (e is InputEventMouseMotion mm) MouseMotion(mm);
	}

	void MouseButtonInput(InputEventMouseButton mb)
	{
		if (mb.Pressed && mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
		{
			ZoomStep(mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1, mb.Position);
			AcceptEvent();
			return;
		}
		var p = ToPixel(mb.Position);
		if (!mb.Pressed)
		{
			if (drag == Drag.None || mb.ButtonIndex != dragButton) return;
			if (drag is Drag.Paint or Drag.Shape) Doc.EndStroke();
			if (drag == Drag.Select && !moved) Doc.Sel = null;
			if (drag == Drag.Pan) MouseDefaultCursorShape = CursorShape.Cross;
			drag = Drag.None;
			StatusChanged?.Invoke();
			QueueRedraw();
			return;
		}
		if (drag != Drag.None) return; // one gesture at a time
		dragButton = mb.ButtonIndex;
		start = last = p;
		moved = false;

		if (mb.ButtonIndex == MouseButton.Middle || (mb.ButtonIndex == MouseButton.Left && (Tool == Tool.Hand || Input.IsKeyPressed(Key.Space))))
		{
			drag = Drag.Pan;
			MouseDefaultCursorShape = CursorShape.Drag;
			return;
		}
		if (mb.ButtonIndex is not (MouseButton.Left or MouseButton.Right)) return;
		// Alt picks a colour only with painting tools (Photoshop): with Move/Select it copies instead
		bool paints = Tool is Tool.Pencil or Tool.Eraser or Tool.Shade or Tool.Fill or Tool.Line or Tool.Rect or Tool.Ellipse;
		if (Tool == Tool.Picker || (paints && mb.AltPressed)) { drag = Drag.Pick; Pick(p); return; }

		switch (Tool)
		{
			case Tool.Pencil or Tool.Eraser or Tool.Shade:
				if (!Begin()) return;
				drag = Drag.Paint;
				Dab(p);
				RefreshCurrent();
				break;
			case Tool.Line or Tool.Rect or Tool.Ellipse:
				if (!Begin()) return;
				drag = Drag.Shape;
				Shape(p, mb.ShiftPressed);
				break;
			case Tool.Fill:
				if (!Begin(false)) return;
				Doc.Fill(p.X, p.Y, PaintColor(), Tolerance, Contiguous);
				Doc.EndStroke();
				break;
			case Tool.Anchor:
				Doc.SetPivot(p);
				break;
			case Tool.Move or Tool.Select:
				// Move drags the selection from anywhere (the whole layer without one); Select only from inside it
				if (mb.ButtonIndex == MouseButton.Left && (Tool == Tool.Move || (Doc.Sel is Rect2I s && s.HasPoint(p))))
				{
					if (!Doc.Lift(mb.CtrlPressed || mb.AltPressed)) { Message?.Invoke(Doc.Cur.Locked ? "слой заблокирован" : "слой скрыт"); return; }
					drag = Drag.Move;
					floatStart = Doc.FloatPos;
				}
				else
				{
					Doc.Deselect();
					drag = Drag.Select;
				}
				QueueRedraw();
				break;
		}
	}

	void MouseMotion(InputEventMouseMotion mm)
	{
		if (drag == Drag.Pan) { Offset += mm.Relative; QueueRedraw(); return; }
		var p = ToPixel(mm.Position);
		if (p != Hover)
		{
			Hover = p;
			StatusChanged?.Invoke();
			QueueRedraw();
			if (drag == Drag.None)
				MouseDefaultCursorShape = Tool == Tool.Move || (Tool == Tool.Select && Doc.Sel is Rect2I s && s.HasPoint(p)) ? CursorShape.Move : CursorShape.Cross;
		}
		if (p == last) return;
		switch (drag)
		{
			case Drag.Pick: Pick(p); break;
			case Drag.Paint:
				foreach (var q in Doc.LinePoints(last.X, last.Y, p.X, p.Y)) Dab(q);
				RefreshCurrent();
				break;
			case Drag.Shape:
				Doc.RestartStroke();
				Shape(p, mm.ShiftPressed);
				break;
			case Drag.Select:
				moved = true;
				var r = new Rect2I(new Vector2I(Math.Min(start.X, p.X), Math.Min(start.Y, p.Y)), (start - p).Abs() + Vector2I.One);
				Doc.Sel = r.Intersection(new Rect2I(0, 0, Doc.W, Doc.H));
				StatusChanged?.Invoke();
				QueueRedraw();
				break;
			case Drag.Move:
				Doc.MoveFloat(floatStart + p - start);
				StatusChanged?.Invoke();
				QueueRedraw();
				break;
		}
		last = p;
	}

	/// <summary>One brush dab: paint, erase, or shade (left button lighter, right darker along the ramp).</summary>
	void Dab(Vector2I p)
	{
		if (Tool == Tool.Shade) Doc.Shade(p.X, p.Y, BrushSize, dragButton == MouseButton.Right ? -1 : 1);
		else Doc.Stamp(p.X, p.Y, BrushSize, PaintColor());
	}

	/// <summary>Draws the line/rect/ellipse from the drag start to p. Shift: square shapes, isometric 2:1 lines.</summary>
	void Shape(Vector2I p, bool shift)
	{
		var c = PaintColor();
		var d = p - start;
		if (Tool == Tool.Rect && IsoMode)
		{
			if (IsoShape == IsoShape.Floor) Doc.IsoRect(start, p, BrushSize, c, FillShapes);
			else Doc.IsoWall(start, p, IsoShape == IsoShape.WallDown ? 1 : -1, BrushSize, c, FillShapes);
			RefreshCurrent();
			return;
		}
		if (Tool == Tool.Line)
		{
			if (!shift && !IsoMode) Doc.Line(start.X, start.Y, p.X, p.Y, BrushSize, c);
			else
			{
				// snap to horizontal, vertical or 2:1
				var dirs = new Vector2[] { new(1, 0), new(0, 1), new(2, 1), new(2, -1) };
				var v = new Vector2(d.X, d.Y);
				var best = dirs.OrderByDescending(u => v.LengthSquared() == 0 ? 0 : Mathf.Abs(u.Normalized().Dot(v.Normalized()))).First();
				if (best.Y == 0) Doc.Line(start.X, start.Y, p.X, start.Y, BrushSize, c);
				else if (best.X == 0) Doc.Line(start.X, start.Y, start.X, p.Y, BrushSize, c);
				else
				{
					int sx = Math.Sign(d.X) == 0 ? 1 : Math.Sign(d.X), sy = Math.Sign(d.Y) == 0 ? 1 : Math.Sign(d.Y);
					int n = Math.Max(Math.Abs(d.X), 2 * Math.Abs(d.Y));
					Doc.IsoLine(start.X, start.Y, sx, sy, n, BrushSize, c);
				}
			}
		}
		else
		{
			if (IsoMode && Tool == Tool.Ellipse)
			{
				// iso circle: twice as wide as tall
				int m = Math.Max(Math.Abs(d.X), 2 * Math.Abs(d.Y));
				p = start + new Vector2I(d.X < 0 ? -m : m, (d.Y < 0 ? -m : m) / 2);
			}
			else if (shift)
			{
				int m = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
				p = start + new Vector2I(d.X < 0 ? -m : m, d.Y < 0 ? -m : m);
			}
			if (Tool == Tool.Rect) Doc.Rect(start.X, start.Y, p.X, p.Y, BrushSize, c, FillShapes);
			else Doc.Ellipse(start.X, start.Y, p.X, p.Y, BrushSize, c, FillShapes);
		}
		RefreshCurrent();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationMouseExit && Hover.X >= 0) { Hover = new(-1, -1); StatusChanged?.Invoke(); QueueRedraw(); }
	}

	void Pick(Vector2I p)
	{
		if (p.X < 0 || p.Y < 0 || p.X >= Doc.W || p.Y >= Doc.H) return;
		var c = Doc.PixelAt(p.X, p.Y);
		if (dragButton == MouseButton.Right) Bg = c; else Fg = c;
		ColorsChanged?.Invoke();
	}

	/// <summary>The W×D footprint from its front corner (the anchor pixel's centre): 18×9-pixel cells, w to the upper left,
	/// d to the upper right, as rf-game's prefab sketches have it.</summary>
	void DrawFootprint()
	{
		if (!ShowIso && !ShowBase) return;
		int w = Doc.Turned ? Doc.BaseD : Doc.BaseW, d = Doc.Turned ? Doc.BaseW : Doc.BaseD;
		Vector2 S(Vector2 px) => Offset + px * Zoom;
		var f = (Vector2)Doc.Pivot + new Vector2(0.5f, 0.5f);
		var uw = new Vector2(-9, -4.5f); // one cell along w
		var ud = new Vector2(9, -4.5f);  // one cell along d
		if (ShowIso)
		{
			var gc = new Color(1, 0.85f, 0.4f, 0.45f);
			for (int i = 1; i < w; i++) DrawLine(S(f + uw * i), S(f + uw * i + ud * d), gc, 1);
			for (int j = 1; j < d; j++) DrawLine(S(f + ud * j), S(f + ud * j + uw * w), gc, 1);
		}
		var oc = new Color(1, 0.55f, 0.1f, 0.9f);
		DrawPolyline(new[] { S(f), S(f + uw * w), S(f + uw * w + ud * d), S(f + ud * d), S(f) }, oc, 2);
	}

	/// <summary>Memo for the light: from the upper left, top light, left face middle, right face dark.</summary>
	void DrawLightHint()
	{
		var o = new Vector2(Size.X - 150, 20);
		var top = new[] { o + new Vector2(40, 0), o + new Vector2(80, 20), o + new Vector2(40, 40), o + new Vector2(0, 20) };
		var left = new[] { o + new Vector2(0, 20), o + new Vector2(40, 40), o + new Vector2(40, 85), o + new Vector2(0, 65) };
		var right = new[] { o + new Vector2(40, 40), o + new Vector2(80, 20), o + new Vector2(80, 65), o + new Vector2(40, 85) };
		DrawColoredPolygon(top, new Color(0.93f, 0.86f, 0.62f));
		DrawColoredPolygon(left, new Color(0.66f, 0.55f, 0.38f));
		DrawColoredPolygon(right, new Color(0.36f, 0.29f, 0.22f));
		DrawLine(o + new Vector2(-30, -12), o + new Vector2(10, 8), Colors.White, 2);
		var font = ThemeDB.FallbackFont;
		DrawString(font, o + new Vector2(-30, 108), "свет сверху-слева", fontSize: 16);
		DrawString(font, o + new Vector2(-30, 128), "верх светлый, лево среднее,", fontSize: 14, modulate: new Color(1, 1, 1, 0.7f));
		DrawString(font, o + new Vector2(-30, 146), "право тёмное", fontSize: 14, modulate: new Color(1, 1, 1, 0.7f));
	}

	public override void _Draw()
	{
		if (Doc == null) return;
		var r = new Rect2(Offset, new Vector2(Doc.W, Doc.H) * Zoom);
		DrawTextureRect(checker, r, true);
		if (Doc.Float != floatSrc)
		{
			floatSrc = Doc.Float;
			floatTex = floatSrc == null ? null : ImageTexture.CreateFromImage(floatSrc);
		}
		foreach (var l in Doc.Layers)
		{
			if (!l.Visible) continue;
			if (tex.TryGetValue(l, out var t)) DrawTextureRect(t, r, false, new Color(1, 1, 1, l.Opacity));
			if (l == Doc.FloatLayer && floatTex != null)
				DrawTextureRect(floatTex, new Rect2(Offset + (Vector2)Doc.FloatPos * Zoom, floatSrc.GetSize() * Zoom), false, new Color(1, 1, 1, l.Opacity));
		}

		if (ShowGrid && Zoom >= 4)
		{
			var gc = new Color(0.08f, 0.08f, 0.1f, 0.45f);
			int x0 = Math.Max(0, (int)(-Offset.X / Zoom)), x1 = Math.Min(Doc.W, (int)((Size.X - Offset.X) / Zoom) + 1);
			int y0 = Math.Max(0, (int)(-Offset.Y / Zoom)), y1 = Math.Min(Doc.H, (int)((Size.Y - Offset.Y) / Zoom) + 1);
			for (int x = x0; x <= x1; x++) DrawLine(new(Offset.X + x * Zoom, r.Position.Y), new(Offset.X + x * Zoom, r.End.Y), gc);
			for (int y = y0; y <= y1; y++) DrawLine(new(r.Position.X, Offset.Y + y * Zoom), new(r.End.X, Offset.Y + y * Zoom), gc);
		}
		DrawRect(r.Grow(1), new Color(0, 0, 0, 0.6f), false);

		DrawFootprint();

		// building anchor: orange pixel outline
		var pv = new Rect2(Offset + (Vector2)Doc.Pivot * Zoom, Vector2.One * Zoom);
		DrawRect(pv.Grow(1), new Color(1, 0.55f, 0.1f), false, 2);

		var axis = new Color(0.2f, 0.85f, 1f, 0.8f);
		if (Doc.SymX) { float x = Offset.X + Doc.AxisX2 * Zoom / 2f; DrawLine(new(x, r.Position.Y), new(x, r.End.Y), axis, 2); }
		if (Doc.SymY) { float y = Offset.Y + Doc.AxisY2 * Zoom / 2f; DrawLine(new(r.Position.X, y), new(r.End.X, y), axis, 2); }

		if (Doc.Sel is Rect2I s)
		{
			var sr = new Rect2(Offset + (Vector2)s.Position * Zoom, (Vector2)s.Size * Zoom);
			DrawRect(sr, Colors.Black, false, 1);
			var pts = new[] { sr.Position, new Vector2(sr.End.X, sr.Position.Y), sr.End, new Vector2(sr.Position.X, sr.End.Y), sr.Position };
			for (int i = 0; i < 4; i++) DrawDashedLine(pts[i], pts[i + 1], Colors.White, 1, 4);
		}

		if (ShowLight) DrawLightHint();

		if (Hover.X >= 0 && Tool is Tool.Pencil or Tool.Eraser or Tool.Shade or Tool.Line or Tool.Rect or Tool.Ellipse && drag == Drag.None)
		{
			int o = (BrushSize - 1) / 2;
			var br = new Rect2(Offset + (Vector2)(Hover - new Vector2I(o, o)) * Zoom, Vector2.One * BrushSize * Zoom);
			DrawRect(br, Colors.White, false);
			DrawRect(br.Grow(-1), Colors.Black, false);
		}
	}
}
