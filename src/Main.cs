using Godot;
using System;
using System.Linq;

namespace PixelForge;

public partial class Main : Control
{
	enum Cmd { ShiftRamp, SkewDown, SkewUp, LockAlpha, FillFg, FillBg, ViaCopy, Array, StepRepeat, NewLayer, MergeDown, MergeVisible, LayerUp, LayerDown, DeleteLayer, Actual, IsoMode, New, Open, Save, SaveAs, Export, ExportLayers, Help, Prefs, Import, Resize, MirrorAll, Iso, Base, Light, Turned, Views, GameExport, Sketch, Quit, Undo, Redo, ReplaceColor, Copy, Cut, Paste, Delete, SelectAll, Deselect, FlipH, FlipV, RotCw, RotCcw, Grid, ZoomIn, ZoomOut, Fit }

	CanvasView view;
	Label status;
	ColorPickerButton fgBtn, bgBtn;
	SpinBox brush, symX, symY;
	CheckBox symXBox, symYBox, isoBox;
	Tool lastShape = Tool.Rect;
	PopupMenu viewMenu, gameMenu, recentMenu;
	SpinBox baseW, baseD;
	Label pivotLabel;
	LayersPanel layers;
	PalettePanel palette;
	string message;
	ulong messageUntil;
	readonly System.Collections.Generic.Dictionary<Tool, Button> toolBtns = new();
	Doc doc;

	public override void _Ready()
	{
		if (OS.GetCmdlineUserArgs().Contains("--selftest"))
		{
			int code = SelfTest.Run(this);
			GetTree().Quit(code);
			return;
		}

		GetTree().AutoAcceptQuit = false;
		Theme = new Theme { DefaultFontSize = 18 };
		AddChild(new ColorRect { Color = new Color(0.13f, 0.13f, 0.14f), MouseFilter = MouseFilterEnum.Ignore });
		GetChild<ColorRect>(0).SetAnchorsPreset(LayoutPreset.FullRect);

		var root = new VBoxContainer();
		root.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(root);

		root.AddChild(BuildMenu());

		var mid = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		root.AddChild(mid);
		// the tool panel scrolls rather than stretching the window past a 1080p screen
		var toolScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(210, 0) };
		toolScroll.AddChild(BuildTools());
		mid.AddChild(toolScroll);
		view = new CanvasView { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
		mid.AddChild(view);
		mid.AddChild(BuildRight());

		status = new Label();
		var sb = new PanelContainer();
		sb.AddChild(status);
		root.AddChild(sb);

		view.StatusChanged += UpdateStatus;
		view.ColorsChanged += ColorsChanged;
		view.Message += Say;
		SetDoc(new Doc(64, 64));
		if (OS.GetCmdlineUserArgs().Contains("--keytest")) { Callable.From(KeyTest).CallDeferred(); return; }
		var timer = new Timer { WaitTime = 60, Autostart = true };
		timer.Timeout += Autosave;
		AddChild(timer);
		GetWindow().MinSize = new Vector2I(1100, 700);
		if (System.IO.File.Exists(UntitledAutosave))
			Ui.Dialog(this, "Автосохранение", new Label { Text = "Найдено автосохранение несохранённого проекта. Открыть его?" }, () =>
			{
				var a = Doc.Load(UntitledAutosave);
				if (a == null) return;
				a.Path = null;
				a.Dirty = true;
				SetDoc(a);
			}, "Открыть").Canceled += () => TryDelete(UntitledAutosave);
		var file = OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).LastOrDefault(a => a.EndsWith(".pforge", StringComparison.OrdinalIgnoreCase) || a.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
		if (file != null) OpenPath(file);
		GetWindow().FilesDropped += files =>
		{
			foreach (var f in files)
				if (f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ImportPng(f);
				else if (f.EndsWith(".pforge", StringComparison.OrdinalIgnoreCase)) Guard(() => OpenPath(f));
		};
	}

	static Key Shift(Key k) => (Key)((long)k | (long)KeyModifierMask.MaskShift);
	static Key Alt(Key k) => (Key)((long)k | (long)KeyModifierMask.MaskAlt);
	static Key CtrlAlt(Key k, bool shift = false) => (Key)((long)Ctrl(k, shift) | (long)KeyModifierMask.MaskAlt);
	static Key Ctrl(Key k, bool shift = false) => (Key)((long)k | (long)KeyModifierMask.MaskCtrl | (shift ? (long)KeyModifierMask.MaskShift : 0));

	Control BuildMenu()
	{
		var bar = new MenuBar();
		PopupMenu M(string title)
		{
			var m = new PopupMenu { Name = title };
			m.IdPressed += id => Run((Cmd)id);
			bar.AddChild(m);
			return m;
		}
		void I(PopupMenu m, string text, Cmd c, Key accel = Key.None) => m.AddItem(text, (int)c, accel);

		var f = M("Файл");
		I(f, "Новый…", Cmd.New, Ctrl(Key.N));
		I(f, "Открыть…", Cmd.Open, Ctrl(Key.O));
		recentMenu = new PopupMenu();
		recentMenu.IndexPressed += i => { var p = Settings.GetList("recent")[i]; Guard(() => OpenPath(p)); };
		f.AddSubmenuNodeItem("Последние проекты", recentMenu);
		f.AboutToPopup += () =>
		{
			recentMenu.Clear();
			foreach (var p in Settings.GetList("recent")) recentMenu.AddItem(p);
			if (recentMenu.ItemCount == 0) { recentMenu.AddItem("пусто"); recentMenu.SetItemDisabled(0, true); }
		};
		I(f, "Сохранить", Cmd.Save, Ctrl(Key.S));
		I(f, "Сохранить как…", Cmd.SaveAs, Ctrl(Key.S, true));
		f.AddSeparator();
		I(f, "Импорт PNG как слой…", Cmd.Import);
		I(f, "Экспорт PNG…", Cmd.Export, CtrlAlt(Key.S, true));
		I(f, "Экспорт слоёв (PNG на слой)…", Cmd.ExportLayers);
		f.AddSeparator();
		I(f, "Настройки…", Cmd.Prefs);
		f.AddSeparator();
		I(f, "Выход", Cmd.Quit, Ctrl(Key.Q));

		var e = M("Правка");
		I(e, "Отменить", Cmd.Undo, Ctrl(Key.Z));
		I(e, "Вернуть", Cmd.Redo, Ctrl(Key.Z, true));
		e.AddSeparator();
		I(e, "Вырезать", Cmd.Cut, Ctrl(Key.X));
		I(e, "Копировать", Cmd.Copy, Ctrl(Key.C));
		I(e, "Вставить", Cmd.Paste, Ctrl(Key.V));
		I(e, "Очистить", Cmd.Delete, Key.Delete);
		I(e, "Залить основным цветом", Cmd.FillFg, Alt(Key.Backspace));
		I(e, "Залить фоновым цветом", Cmd.FillBg, Ctrl(Key.Backspace));
		e.AddSeparator();
		I(e, "Дубль на новый слой", Cmd.ViaCopy, Ctrl(Key.J));
		I(e, "Массив…", Cmd.Array, CtrlAlt(Key.T));
		I(e, "Ещё одна копия с тем же шагом", Cmd.StepRepeat, CtrlAlt(Key.T, true));
		e.AddSeparator();
		I(e, "Сдвиг по рампе / в другую рампу…", Cmd.ShiftRamp);
		I(e, "Изо-скос ↘ (стена вниз вправо)", Cmd.SkewDown);
		I(e, "Изо-скос ↗ (стена вверх вправо)", Cmd.SkewUp);
		e.AddSeparator();
		I(e, "Выделить всё", Cmd.SelectAll, Ctrl(Key.A));
		I(e, "Снять выделение", Cmd.Deselect, Ctrl(Key.D));
		e.AddSeparator();
		I(e, "Отразить по горизонтали", Cmd.FlipH, Shift(Key.H));
		I(e, "Отразить по вертикали", Cmd.FlipV, Shift(Key.V));
		I(e, "Повернуть на 90° по часовой", Cmd.RotCw);
		I(e, "Повернуть на 90° против часовой", Cmd.RotCcw);
		e.AddSeparator();
		I(e, "Заменить цвет…", Cmd.ReplaceColor, Shift(Key.R));

		var l = M("Слой");
		I(l, "Новый слой", Cmd.NewLayer, Ctrl(Key.N, true));
		I(l, "Дубль на новый слой (выделенное или весь слой)", Cmd.ViaCopy);
		I(l, "Удалить слой", Cmd.DeleteLayer);
		I(l, "Защитить прозрачность   /", Cmd.LockAlpha);
		l.AddSeparator();
		I(l, "Выше", Cmd.LayerUp, Ctrl(Key.Bracketright));
		I(l, "Ниже", Cmd.LayerDown, Ctrl(Key.Bracketleft));
		l.AddSeparator();
		I(l, "Объединить с нижним", Cmd.MergeDown, Ctrl(Key.E));
		I(l, "Объединить видимые", Cmd.MergeVisible, Ctrl(Key.E, true));

		viewMenu = M("Вид");
		viewMenu.AddCheckItem("Сетка пикселей", (int)Cmd.Grid, Ctrl(Key.Apostrophe));
		viewMenu.SetItemChecked(0, true);
		viewMenu.AddCheckItem("Изо-режим: линии 2:1, ромбы, изо-круги", (int)Cmd.IsoMode, Ctrl(Key.Semicolon, true));
		I(viewMenu, "Приблизить", Cmd.ZoomIn, Ctrl(Key.Equal));
		I(viewMenu, "Отдалить", Cmd.ZoomOut, Ctrl(Key.Minus));
		I(viewMenu, "Реальный размер (1×)", Cmd.Actual, Ctrl(Key.Key1));
		I(viewMenu, "Вписать в окно", Cmd.Fit, Ctrl(Key.Key0));
		viewMenu.AddSeparator();
		I(viewMenu, "Горячие клавиши…", Cmd.Help, Key.F1);

		var c = M("Холст");
		I(c, "Размер холста…", Cmd.Resize, (Key)((long)Key.C | (long)KeyModifierMask.MaskCtrl | (long)KeyModifierMask.MaskAlt));
		I(c, "Отразить весь холст по горизонтали (вокруг якоря)", Cmd.MirrorAll);

		gameMenu = M("Игра");
		gameMenu.AddCheckItem("Изометрическая сетка W×D", (int)Cmd.Iso);
		gameMenu.AddCheckItem("Контур основания", (int)Cmd.Base);
		gameMenu.AddCheckItem("Основание повёрнутого вида (D×W)", (int)Cmd.Turned);
		gameMenu.AddCheckItem("Памятка: свет сверху-слева", (int)Cmd.Light);
		gameMenu.AddSeparator();
		I(gameMenu, "Виды…", Cmd.Views);
		I(gameMenu, "Экспорт для игры…", Cmd.GameExport, (Key)((long)Key.E | (long)KeyModifierMask.MaskCtrl | (long)KeyModifierMask.MaskAlt));
		I(gameMenu, "Открыть эскиз из rf-game…", Cmd.Sketch);
		gameMenu.AboutToPopup += SyncGameMenu;
		return bar;
	}

	Control BuildTools()
	{
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0) };
		var group = new ButtonGroup();
		foreach (var (t, label) in new[] {
			(Tool.Move, "Перемещение  V"), (Tool.Select, "Выделение  M"),
			(Tool.Pencil, "Карандаш  B"), (Tool.Eraser, "Ластик  E"), (Tool.Shade, "Тон  O"), (Tool.Fill, "Заливка  G"), (Tool.Picker, "Пипетка  I"),
			(Tool.Line, "Линия  U"), (Tool.Rect, "Прямоугольник  U"), (Tool.Ellipse, "Эллипс  U"),
			(Tool.Anchor, "Якорь  A"), (Tool.Hand, "Рука  H") })
		{
			var b = new Button { Text = label, ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
			b.Pressed += () => SetTool(t);
			toolBtns[t] = b;
			box.AddChild(b);
		}
		toolBtns[Tool.Pencil].ButtonPressed = true;

		box.AddChild(new HSeparator());
		box.AddChild(new Label { Text = "Размер кисти" });
		brush = new SpinBox { MinValue = 1, MaxValue = 8, Value = 1 };
		brush.ValueChanged += v => { view.BrushSize = (int)v; view.QueueRedraw(); };
		box.AddChild(brush);
		box.AddChild(Check("Заливать фигуры", false, on => view.FillShapes = on));
		box.AddChild(Check("Изо-режим 2:1", false, on => SetIso(on), out isoBox));
		var isoShape = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "Что рисует прямоугольник в изо-режиме" };
		isoShape.AddItem("◇ ромб на земле");
		isoShape.AddItem("▱ стена ↘");
		isoShape.AddItem("▱ стена ↗");
		isoShape.ItemSelected += i => view.IsoShape = (IsoShape)(int)i;
		box.AddChild(isoShape);

		box.AddChild(new Label { Text = "Допуск заливки" });
		var tol = new SpinBox { MinValue = 0, MaxValue = 255, Value = 0 };
		tol.ValueChanged += v => view.Tolerance = (int)v;
		box.AddChild(tol);
		box.AddChild(Check("Только смежные", true, on => view.Contiguous = on));

		box.AddChild(new HSeparator());
		box.AddChild(new Label { Text = "Симметрия" });
		symX = new SpinBox { Step = 0.5, Prefix = "x" };
		symY = new SpinBox { Step = 0.5, Prefix = "y" };
		symX.ValueChanged += v => { doc.AxisX2 = (int)Math.Round(v * 2); view.QueueRedraw(); };
		symY.ValueChanged += v => { doc.AxisY2 = (int)Math.Round(v * 2); view.QueueRedraw(); };
		box.AddChild(Check("↔ зеркально", false, on => { doc.SymX = on; view.QueueRedraw(); }, out symXBox));
		box.AddChild(symX);
		box.AddChild(Check("↕ зеркально", false, on => { doc.SymY = on; view.QueueRedraw(); }, out symYBox));
		box.AddChild(symY);

		box.AddChild(new HSeparator());
		box.AddChild(new Label { Text = "Постройка, клеток W × D" });
		var wd = new HBoxContainer();
		baseW = new SpinBox { MinValue = 1, MaxValue = 20, TooltipText = "W: вдоль левой нижней грани" };
		baseD = new SpinBox { MinValue = 1, MaxValue = 20, TooltipText = "D: вдоль правой нижней грани" };
		baseW.ValueChanged += v => { doc.BaseW = (int)v; doc.Touch(); };
		baseD.ValueChanged += v => { doc.BaseD = (int)v; doc.Touch(); };
		wd.AddChild(baseW); wd.AddChild(baseD);
		box.AddChild(wd);
		pivotLabel = new Label();
		box.AddChild(pivotLabel);
		return box;
	}

	static CheckBox Check(string text, bool on, Action<bool> toggled) => Check(text, on, toggled, out _);

	static CheckBox Check(string text, bool on, Action<bool> toggled, out CheckBox box)
	{
		box = new CheckBox { Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None };
		box.Toggled += on => toggled(on);
		return box;
	}

	Control BuildRight()
	{
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(320, 0) };
		box.AddChild(new Label { Text = "Основной / фоновый" });
		var row = new HBoxContainer();
		fgBtn = new ColorPickerButton { Color = Colors.Black, CustomMinimumSize = new Vector2(70, 44), FocusMode = FocusModeEnum.None };
		bgBtn = new ColorPickerButton { Color = Colors.White, CustomMinimumSize = new Vector2(70, 44), FocusMode = FocusModeEnum.None };
		fgBtn.ColorChanged += c => { view.Fg = c; ColorsChanged(); };
		bgBtn.ColorChanged += c => { view.Bg = c; ColorsChanged(); };
		row.AddChild(fgBtn); row.AddChild(bgBtn);
		row.AddChild(Ui.Btn("⇄", "Поменять местами (X)", SwapColors));
		row.AddChild(Ui.Btn("☀", "Светлее по рампе (Shift+[)", () => StepColor(1)));
		row.AddChild(Ui.Btn("☾", "Темнее по рампе (Shift+])", () => StepColor(-1)));
		box.AddChild(row);

		layers = new LayersPanel { SizeFlagsVertical = SizeFlags.ExpandFill };
		box.AddChild(layers);
		box.AddChild(new HSeparator());
		palette = new PalettePanel { SizeFlagsVertical = SizeFlags.ExpandFill, Fg = () => view.Fg };
		palette.ColorChosen += (c, primary) => { if (primary) view.Fg = c; else view.Bg = c; ColorsChanged(); };
		box.AddChild(palette);
		return box;
	}

	/// <summary>Primary or background colour changed somewhere: sync the buttons, palette highlight, status.</summary>
	void ColorsChanged()
	{
		fgBtn.Color = view.Fg;
		bgBtn.Color = view.Bg;
		palette.FgChanged();
		UpdateStatus();
	}

	void Say(string m)
	{
		message = m;
		messageUntil = Time.GetTicksMsec() + 3000;
		UpdateStatus();
	}

	void StepColor(int dir)
	{
		if (doc.Palette.Step(view.Fg, dir) is Color c) { view.Fg = c; ColorsChanged(); }
	}

	void SetIso(bool on)
	{
		view.IsoMode = on;
		isoBox.SetPressedNoSignal(on);
		viewMenu.SetItemChecked(viewMenu.GetItemIndex((int)Cmd.IsoMode), on);
		Say(on ? "изо-режим: линии 2:1, прямоугольник — ромб на земле, эллипс — изо-круг" : "изо-режим выключен");
	}

	void SetTool(Tool t)
	{
		if (t is Tool.Line or Tool.Rect or Tool.Ellipse) lastShape = t;
		view.Tool = t;
		toolBtns[t].ButtonPressed = true;
		view.QueueRedraw();
	}

	void SwapColors()
	{
		(view.Fg, view.Bg) = (view.Bg, view.Fg);
		ColorsChanged();
	}

	void SetDoc(Doc d)
	{
		doc = d;
		doc.Changed += () => { UpdateTitle(); UpdateStatus(); SyncSymmetry(); };
		view.SetDoc(d);
		SyncSymmetry();
		layers.SetDoc(d);
		palette.SetDoc(d);
		UpdateTitle();
		UpdateStatus();
	}

	/// <summary>--keytest (headless): presses the Photoshop-style keys through the real window and checks what they did.</summary>
	void KeyTest()
	{
		int fails = 0;
		void Key(Key key, bool ctrl = false, bool shift = false, bool alt = false) =>
			GetViewport().PushInput(new InputEventKey { Keycode = key, Pressed = true, CtrlPressed = ctrl, ShiftPressed = shift, AltPressed = alt });
		void Check(bool ok, string what) { GD.Print((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }

		Key(Godot.Key.V); Check(view.Tool == Tool.Move, "V move");
		Key(Godot.Key.M); Check(view.Tool == Tool.Select, "M select");
		Key(Godot.Key.U); Check(view.Tool == Tool.Rect, "U shape");
		Key(Godot.Key.U, shift: true); Check(view.Tool == Tool.Ellipse, "Shift+U next shape");
		Key(Godot.Key.B); Check(view.Tool == Tool.Pencil, "B pencil");
		Key(Godot.Key.O); Check(view.Tool == Tool.Shade, "O shade");
		Key(Godot.Key.Slash); Check(doc.Cur.LockAlpha, "/ lock transparency");
		Key(Godot.Key.Slash); Check(!doc.Cur.LockAlpha, "/ again unlocks");
		Key(Godot.Key.B);
		Key(Godot.Key.Bracketright); Key(Godot.Key.Bracketright); Check(view.BrushSize == 3, "] bigger brush");
		Key(Godot.Key.Bracketleft); Check(view.BrushSize == 2, "[ smaller brush");
		view.Fg = Colors.Red; Key(Godot.Key.D); Check(view.Fg == Colors.Black && view.Bg == Colors.White, "D default colours");
		Key(Godot.Key.X); Check(view.Fg == Colors.White, "X swap");
		Key(Godot.Key.N, ctrl: true, shift: true); Check(doc.Layers.Count == 2, "Ctrl+Shift+N new layer");
		Key(Godot.Key.Bracketleft, ctrl: true); Check(doc.Current == 0, "Ctrl+[ layer down");
		Key(Godot.Key.Bracketright, ctrl: true); Check(doc.Current == 1, "Ctrl+] layer up");
		Key(Godot.Key.E, ctrl: true); Check(doc.Layers.Count == 1, "Ctrl+E merge down");
		Key(Godot.Key.Apostrophe, ctrl: true); Check(!view.ShowGrid, "Ctrl+' grid off");
		Key(Godot.Key.Semicolon, ctrl: true, shift: true); Check(view.IsoMode, "Ctrl+Shift+; iso mode");
		Key(Godot.Key.Key1, ctrl: true); Check(view.Zoom == 1, "Ctrl+1 actual size");
		Key(Godot.Key.Equal, ctrl: true); Check(view.Zoom == 2, "Ctrl+= zoom in");
		Key(Godot.Key.A, ctrl: true); Check(doc.Sel != null, "Ctrl+A select all");
		Key(Godot.Key.Backspace, alt: true); Check(doc.Cur.Img.GetPixel(5, 5) == Colors.White, "Alt+Backspace fill with the primary colour");
		Key(Godot.Key.J, ctrl: true); Check(doc.Layers.Count == 2, "Ctrl+J layer via copy");
		Key(Godot.Key.Z, ctrl: true); Check(doc.Layers.Count == 1, "Ctrl+Z");
		Key(Godot.Key.Z, ctrl: true, shift: true); Check(doc.Layers.Count == 2, "Ctrl+Shift+Z redo");
		doc.Sel = new Rect2I(0, 0, 2, 2);
		Key(Godot.Key.Right, shift: true); Check(doc.Float != null && doc.FloatPos == new Vector2I(10, 0), "Shift+→ nudges 10 px");
		Key(Godot.Key.Enter); Check(doc.Float == null, "Enter drops it");
		Key(Godot.Key.T, ctrl: true, alt: true); Check(GetChildren().OfType<ConfirmationDialog>().Any(d => d.Title == "Массив"), "Ctrl+Alt+T array dialog");
		foreach (var dlg in GetChildren().OfType<ConfirmationDialog>()) { dlg.Hide(); dlg.QueueFree(); }
		Key(Godot.Key.D, ctrl: true); Check(doc.Sel == null, "Ctrl+D deselect");
		GD.Print(fails == 0 ? "KEYTEST PASSED" : $"KEYTEST FAILED: {fails}");
		GetTree().Quit(fails == 0 ? 0 : 1);
	}

	void SyncGameMenu()
	{
		void C(Cmd c, bool on) => gameMenu.SetItemChecked(gameMenu.GetItemIndex((int)c), on);
		C(Cmd.Iso, view.ShowIso); C(Cmd.Base, view.ShowBase); C(Cmd.Light, view.ShowLight); C(Cmd.Turned, doc.Turned);
	}

	void SyncSymmetry()
	{
		baseW.SetValueNoSignal(doc.BaseW); baseD.SetValueNoSignal(doc.BaseD);
		pivotLabel.Text = $"якорь {doc.Pivot.X}, {doc.Pivot.Y}" + (doc.Turned ? "  (повёрнут)" : "");
		int ax = doc.AxisX2, ay = doc.AxisY2; // a smaller max clamps the spin box and fires its signal
		symX.MaxValue = doc.W; symY.MaxValue = doc.H;
		doc.AxisX2 = ax; doc.AxisY2 = ay;
		symX.SetValueNoSignal(ax / 2.0); symY.SetValueNoSignal(ay / 2.0);
		symXBox.SetPressedNoSignal(doc.SymX); symYBox.SetPressedNoSignal(doc.SymY);
	}

	void UpdateTitle()
	{
		var name = doc.Path == null ? "без имени" : System.IO.Path.GetFileName(doc.Path);
		GetWindow().Title = $"PixelForge — {name}{(doc.Dirty ? " *" : "")}";
	}

	void UpdateStatus()
	{
		var pos = view.Hover.X >= 0 && view.Hover.X < doc.W && view.Hover.Y >= 0 && view.Hover.Y < doc.H
			? $"X {view.Hover.X}, Y {view.Hover.Y}" : "—";
		status.Text = $"  Зум {view.Zoom}×   |   {pos}   |   {doc.W}×{doc.H}   |   цвет #{view.Fg.ToHtml(view.Fg.A < 1)}"
			+ $"   |   слой «{doc.Cur.Name}»"
			+ (doc.Sel is Rect2I s ? $"   |   выделение {s.Size.X}×{s.Size.Y} от {s.Position.X},{s.Position.Y}" + (doc.Float != null ? " (плавает, Enter — опустить)" : "") : "") + (Time.GetTicksMsec() < messageUntil ? $"   |   {message}" : "");
	}

	/// <summary>Keys the menus don't carry, Photoshop-style. Menu accelerators come first (MenuBar shortcuts).</summary>
	public override void _UnhandledKeyInput(InputEvent e)
	{
		if (e is not InputEventKey k || !k.Pressed) return;
		bool arrows = k.Keycode is Key.Left or Key.Right or Key.Up or Key.Down;
		if (k.Echo && !arrows && k.Keycode is not (Key.Bracketleft or Key.Bracketright)) return;
		if (k.CtrlPressed && !k.ShiftPressed && !k.AltPressed && k.Keycode == Key.Y) { doc.Redo(); AcceptEvent(); return; }
		if (k.CtrlPressed && k.AltPressed && !k.ShiftPressed && k.Keycode == Key.Z) { doc.Undo(); AcceptEvent(); return; }
		if (k.CtrlPressed || k.AltPressed) return;
		if (arrows)
		{
			// nudge the selection (or, with the Move tool, the whole layer): 1 px, Shift 10 px
			if (doc.Sel == null && doc.Float == null && view.Tool != Tool.Move) return;
			int n = k.ShiftPressed ? 10 : 1;
			var d = k.Keycode switch { Key.Left => new Vector2I(-n, 0), Key.Right => new Vector2I(n, 0), Key.Up => new Vector2I(0, -n), _ => new Vector2I(0, n) };
			if (!doc.Nudge(d)) Say(doc.Cur.Locked ? "слой заблокирован" : "слой скрыт");
			view.QueueRedraw();
			UpdateStatus();
			AcceptEvent();
			return;
		}
		if (k.ShiftPressed)
		{
			switch (k.Keycode)
			{
				case Key.U: // cycle the shape tools
					SetTool(lastShape switch { Tool.Rect => Tool.Ellipse, Tool.Ellipse => Tool.Line, _ => Tool.Rect });
					break;
				case Key.Bracketleft: StepColor(1); break;
				case Key.Bracketright: StepColor(-1); break;
				default: return;
			}
			AcceptEvent();
			return;
		}
		switch (k.Keycode)
		{
			case Key.V: SetTool(Tool.Move); break;
			case Key.M: SetTool(Tool.Select); break;
			case Key.B: SetTool(Tool.Pencil); break;
			case Key.O: SetTool(Tool.Shade); break;
			case Key.Slash: Run(Cmd.LockAlpha); break;
			case Key.E: SetTool(Tool.Eraser); break;
			case Key.G: SetTool(Tool.Fill); break;
			case Key.I: SetTool(Tool.Picker); break;
			case Key.U: SetTool(lastShape); break;
			case Key.A: SetTool(Tool.Anchor); break;
			case Key.H: SetTool(Tool.Hand); break;
			case Key.X: SwapColors(); break;
			case Key.D: view.Fg = Colors.Black; view.Bg = Colors.White; ColorsChanged(); break;
			case Key.Bracketleft: brush.Value -= 1; break;
			case Key.Bracketright: brush.Value += 1; break;
			case Key.Enter or Key.KpEnter: doc.Anchor(); break;
			case Key.Escape: doc.Deselect(); view.QueueRedraw(); UpdateStatus(); break;
			case Key.Backspace: Run(Cmd.Delete); break;
			default: return;
		}
		AcceptEvent();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest) Guard(() => GetTree().Quit());
	}

	void Run(Cmd c)
	{
		switch (c)
		{
			case Cmd.New: Guard(ShowNewDialog); break;
			case Cmd.Open: Guard(() => Ui.PickFile(this, FileDialog.FileModeEnum.OpenFile, new[] { "*.pforge ; Проект PixelForge", "*.png ; PNG" }, OpenPath)); break;
			case Cmd.Save: if (doc.Path != null) SaveTo(doc.Path); else Run(Cmd.SaveAs); break;
			case Cmd.SaveAs: Ui.PickFile(this, FileDialog.FileModeEnum.SaveFile, new[] { "*.pforge ; Проект PixelForge" }, SaveTo); break;
			case Cmd.Export: Ui.PickFile(this, FileDialog.FileModeEnum.SaveFile, new[] { "*.png ; PNG" }, p => Report(doc.ExportPng(p), "Экспорт")); break;
			case Cmd.Quit: Guard(() => GetTree().Quit()); break;
			case Cmd.Help: Help.Show(this); break;
			case Cmd.FillFg or Cmd.FillBg:
				var fc = c == Cmd.FillFg ? view.Fg : view.Bg;
				if (doc.Palette.Strict && !doc.Palette.Contains(fc)) Say("цвет не из палитры (режим «только палитра»)");
				else doc.FillSelection(fc);
				break;
			case Cmd.ViaCopy: doc.LayerViaCopy(); break;
			case Cmd.ShiftRamp: ShowShiftRampDialog(); break;
			case Cmd.SkewDown: doc.Skew(1); break;
			case Cmd.SkewUp: doc.Skew(-1); break;
			case Cmd.LockAlpha:
				doc.Cur.LockAlpha = !doc.Cur.LockAlpha;
				doc.Touch();
				Say(doc.Cur.LockAlpha ? "прозрачность слоя защищена: рисуется только поверх закрашенного" : "защита прозрачности снята");
				break;
			case Cmd.Array: ShowArrayDialog(); break;
			case Cmd.StepRepeat: if (!doc.StepRepeat()) Say("сначала сделайте массив (Ctrl+Alt+T) с выделением"); view.QueueRedraw(); break;
			case Cmd.NewLayer: doc.AddLayer(); break;
			case Cmd.DeleteLayer: doc.DeleteLayer(); break;
			case Cmd.LayerUp: doc.MoveLayer(1); break;
			case Cmd.LayerDown: doc.MoveLayer(-1); break;
			case Cmd.MergeDown: doc.MergeDown(); break;
			case Cmd.MergeVisible: doc.FlattenVisible(); break;
			case Cmd.Actual: view.SetZoomCentered(1); break;
			case Cmd.IsoMode: SetIso(!view.IsoMode); break;
			case Cmd.Prefs: GameDialogs.Preferences(this); break;
			case Cmd.ExportLayers:
				Ui.PickFile(this, FileDialog.FileModeEnum.OpenDir, Array.Empty<string>(), dir =>
					Say($"слоёв сохранено: {doc.ExportLayers(dir, doc.Path == null ? "layers" : System.IO.Path.GetFileNameWithoutExtension(doc.Path))}"));
				break;
			case Cmd.Import: Ui.PickFile(this, FileDialog.FileModeEnum.OpenFile, new[] { "*.png ; PNG" }, ImportPng); break;
			case Cmd.Resize: ShowResizeDialog(); break;
			case Cmd.MirrorAll: doc.MirrorAll(); break;
			case Cmd.Iso: view.ShowIso = !view.ShowIso; view.QueueRedraw(); break;
			case Cmd.Base: view.ShowBase = !view.ShowBase; view.QueueRedraw(); break;
			case Cmd.Light: view.ShowLight = !view.ShowLight; view.QueueRedraw(); break;
			case Cmd.Turned: doc.Turned = !doc.Turned; doc.Touch(); break;
			case Cmd.Views: GameDialogs.Views(this, doc); break;
			case Cmd.GameExport: GameDialogs.Export(this, doc, Say); break;
			case Cmd.Sketch: GameDialogs.OpenSketch(this, doc, d => Guard(() => SetDoc(d)), Say); break;
			case Cmd.Undo: doc.Undo(); break;
			case Cmd.Redo: doc.Redo(); break;
			case Cmd.ReplaceColor: ShowReplaceDialog(); break;
			case Cmd.Copy: doc.Copy(); break;
			case Cmd.Cut: doc.Cut(); break;
			case Cmd.Paste: if (doc.Paste()) SetTool(Tool.Select); break;
			case Cmd.Delete: doc.DeleteSelection(); break;
			case Cmd.SelectAll: doc.SelectAll(); view.QueueRedraw(); UpdateStatus(); break;
			case Cmd.Deselect: doc.Deselect(); view.QueueRedraw(); UpdateStatus(); break;
			case Cmd.FlipH: doc.FlipH(); break;
			case Cmd.FlipV: doc.FlipV(); break;
			case Cmd.RotCw: doc.Rotate(true); break;
			case Cmd.RotCcw: doc.Rotate(false); break;
			case Cmd.Grid:
				view.ShowGrid = !view.ShowGrid;
				viewMenu.SetItemChecked(viewMenu.GetItemIndex((int)Cmd.Grid), view.ShowGrid);
				view.QueueRedraw();
				break;
			case Cmd.ZoomIn: view.ZoomStep(1); break;
			case Cmd.ZoomOut: view.ZoomStep(-1); break;
			case Cmd.Fit: view.Fit(); break;
		}
	}

	void SaveTo(string path)
	{
		if (!path.EndsWith(".pforge", StringComparison.OrdinalIgnoreCase)) path += ".pforge";
		var oldAutosave = AutosavePath;
		var err = doc.Save(path);
		Report(err, "Сохранение");
		if (err != Error.Ok) return;
		TryDelete(oldAutosave);
		TryDelete(AutosavePath);
		Remember(path);
		Say("сохранено");
		UpdateTitle();
	}

	void OpenPath(string path)
	{
		Doc d;
		if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
		{
			var img = Image.LoadFromFile(path);
			d = img == null ? null : Doc.FromImage(img, System.IO.Path.GetFileNameWithoutExtension(path));
		}
		else
		{
			var auto = path + ".autosave";
			if (System.IO.File.Exists(auto) && System.IO.File.GetLastWriteTimeUtc(auto) > System.IO.File.GetLastWriteTimeUtc(path))
			{
				var d0 = Doc.Load(path);
				Ui.Dialog(this, "Автосохранение", new Label { Text = $"Есть автосохранение новее файла:\n{auto}\nОткрыть его? (Отмена — открыть сам файл)" }, () =>
				{
					var a = Doc.Load(auto);
					if (a == null) { Alert("Автосохранение не читается"); return; }
					a.Path = path;
					a.Dirty = true;
					SetDoc(a);
					Say("открыто автосохранение — сохраните, чтобы оставить его");
				}, "Открыть автосохранение");
				d = d0;
			}
			else d = Doc.Load(path);
			if (d != null) Remember(path);
		}
		if (d == null) { Alert($"Не удалось открыть:\n{path}"); return; }
		SetDoc(d);
	}

	static readonly string UntitledAutosave = ProjectSettings.GlobalizePath("user://untitled.pforge.autosave");
	string AutosavePath => doc.Path != null ? doc.Path + ".autosave" : UntitledAutosave;

	static void TryDelete(string path)
	{
		try { System.IO.File.Delete(path); } catch (Exception e) { GD.PrintErr(e.Message); }
	}

	static void Remember(string path)
	{
		var list = Settings.GetList("recent").Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).Prepend(path).Take(10).ToArray();
		Settings.SetList("recent", list);
	}

	/// <summary>Every 60 s: unsaved work goes to &lt;file&gt;.autosave (or user://untitled… for a new one), never mid-stroke.</summary>
	void Autosave()
	{
		if (!doc.Dirty || doc.Painting || doc.Float != null) return;
		if (doc.Write(AutosavePath) == Error.Ok) Say("автосохранение");
	}

	void Report(Error err, string what)
	{
		if (err != Error.Ok) Alert($"{what}: ошибка {err}");
	}

	void Alert(string text) => Ui.Alert(this, text);

	/// <summary>Runs the action now, or after asking when unsaved changes would be lost.</summary>
	void Guard(Action then)
	{
		// discarding the work discards its autosave too
		if (doc.Dirty) Ui.Confirm(this, "Есть несохранённые изменения. Продолжить без сохранения?", () => { TryDelete(AutosavePath); then(); });
		else then();
	}

	void ShowReplaceDialog()
	{
		var grid = new GridContainer { Columns = 2 };
		var from = new ColorPickerButton { Color = view.Fg, CustomMinimumSize = new Vector2(120, 40) };
		var to = new ColorPickerButton { Color = view.Bg, CustomMinimumSize = new Vector2(120, 40) };
		var scope = new OptionButton();
		scope.AddItem("Текущий слой");
		scope.AddItem("Все слои проекта");
		grid.AddChild(new Label { Text = "Заменить" }); grid.AddChild(from);
		grid.AddChild(new Label { Text = "на" }); grid.AddChild(to);
		grid.AddChild(new Label { Text = "Где" }); grid.AddChild(scope);
		Ui.Dialog(this, "Замена цвета", grid, () =>
		{
			int n = doc.ReplaceColor(from.Color, to.Color, scope.Selected == 1);
			Say($"заменено пикселей: {n}");
		}, "Заменить");
	}

	/// <summary>Asks where the picture goes, then adds it as a layer (growing the canvas if asked).</summary>
	void ImportPng(string path)
	{
		var img = Image.LoadFromFile(path);
		if (img == null) { Alert($"Не удалось прочитать картинку:\n{path}"); return; }
		var name = System.IO.Path.GetFileNameWithoutExtension(path);
		var box = new VBoxContainer();
		box.AddChild(new Label { Text = $"{name}: {img.GetWidth()}×{img.GetHeight()}" });
		var place = new OptionButton();
		place.AddItem("По центру");
		place.AddItem("По левому верхнему углу");
		place.AddItem("По якорю (низ-середина на якорь)");
		box.AddChild(place);
		var asRef = new CheckBox { Text = "Как подложку (не экспортируется)" };
		box.AddChild(asRef);
		bool bigger = img.GetWidth() > doc.W || img.GetHeight() > doc.H;
		var grow = new CheckBox { Text = $"Увеличить холст до {Math.Max(doc.W, img.GetWidth())}×{Math.Max(doc.H, img.GetHeight())}", ButtonPressed = true, Visible = bigger };
		box.AddChild(grow);
		Ui.Dialog(this, "Импорт PNG как слой", box, () =>
		{
			var p = (Place)place.Selected;
			if (bigger && grow.ButtonPressed)
			{
				int w = Math.Max(doc.W, img.GetWidth()), h = Math.Max(doc.H, img.GetHeight());
				// anchored imports grow upward from the bottom-middle so the anchor keeps its spot relative to the art
				doc.Resize(w, h, p == Place.Anchor ? doc.ResizeOffset(w, h, 1, 2) : p == Place.TopLeft ? Vector2I.Zero : doc.ResizeOffset(w, h, 1, 1));
				view.Fit();
			}
			doc.ImportLayer(img, name, p, asRef.ButtonPressed);
		}, "Импорт");
	}

	void ShowResizeDialog()
	{
		var box = new VBoxContainer();
		var grid = new GridContainer { Columns = 2 };
		var w = new SpinBox { MinValue = 8, MaxValue = 1024, Value = doc.W };
		var h = new SpinBox { MinValue = 8, MaxValue = 1024, Value = doc.H };
		grid.AddChild(new Label { Text = "Ширина" }); grid.AddChild(w);
		grid.AddChild(new Label { Text = "Высота" }); grid.AddChild(h);
		box.AddChild(grid);
		box.AddChild(new Label { Text = "Где остаётся рисунок" });
		var anchors = new GridContainer { Columns = 3 };
		var group = new ButtonGroup();
		int ax = 1, ay = 1;
		for (int y = 0; y < 3; y++)
			for (int x = 0; x < 3; x++)
			{
				int bx = x, by = y;
				var b = new Button { ToggleMode = true, ButtonGroup = group, CustomMinimumSize = new Vector2(44, 44), Text = x == 1 && y == 1 ? "●" : "", ButtonPressed = x == 1 && y == 1 };
				b.Pressed += () => { ax = bx; ay = by; };
				anchors.AddChild(b);
			}
		box.AddChild(anchors);
		Ui.Dialog(this, "Размер холста", box, () =>
		{
			int nw = (int)w.Value, nh = (int)h.Value;
			doc.Resize(nw, nh, doc.ResizeOffset(nw, nh, ax, ay));
			view.Fit();
		}, "Изменить");
	}

	/// <summary>Every palette colour in the selection (or layer) some steps along its ramp, or onto another ramp.</summary>
	void ShowShiftRampDialog()
	{
		var steps = new SpinBox { MinValue = -7, MaxValue = 7, Value = -1, Prefix = "шагов" };
		var target = new OptionButton();
		target.AddItem("в своей рампе");
		foreach (var r in doc.Palette.Ramps) target.AddItem("в рампу «" + r.Name + "»");
		var grid = new GridContainer { Columns = 2 };
		grid.AddChild(new Label { Text = "Сдвиг (− темнее, + светлее)" }); grid.AddChild(steps);
		grid.AddChild(new Label { Text = "Куда" }); grid.AddChild(target);
		var box = new VBoxContainer();
		box.AddChild(grid);
		box.AddChild(new Label
		{
			Text = (doc.Sel == null ? "Весь слой. " : "Только выделенное. ") + "Цвет из другой рампы встаёт на то же место новой (по доле длины), потом сдвигается. Цвета не из палитры не трогаются.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(440, 0),
		});
		Ui.Dialog(this, "Сдвиг по рампе", box, () =>
		{
			var t = target.Selected == 0 ? null : doc.Palette.Ramps[target.Selected - 1];
			Say($"перекрашено пикселей: {doc.ShiftRamp((int)steps.Value, t)}");
		}, "Сдвинуть");
	}

	/// <summary>Ctrl+Alt+T: the selection repeated count times with a step; quick steps along the iso axes.</summary>
	void ShowArrayDialog()
	{
		if (doc.Sel == null && doc.Float == null) { Say("сначала выделите объект (M)"); return; }
		var size = doc.Float?.GetSize() ?? doc.Sel.Value.Size;
		int even = size.X + (size.X & 1); // 2:1 steps need an even width
		var count = new SpinBox { MinValue = 2, MaxValue = 200, Value = 3 };
		var dx = new SpinBox { MinValue = -1024, MaxValue = 1024, Value = doc.LastStep != Vector2I.Zero ? doc.LastStep.X : size.X, Prefix = "Δx" };
		var dy = new SpinBox { MinValue = -1024, MaxValue = 1024, Value = doc.LastStep.Y, Prefix = "Δy" };
		var grid = new GridContainer { Columns = 2 };
		grid.AddChild(new Label { Text = "Копий всего" }); grid.AddChild(count);
		grid.AddChild(new Label { Text = "Шаг, px" });
		var step = new HBoxContainer();
		step.AddChild(dx); step.AddChild(dy);
		grid.AddChild(step);
		var quick = new HFlowContainer();
		foreach (var (label, v) in new (string, Vector2I)[] {
			("→", new(size.X, 0)), ("↓", new(0, size.Y)), ("←", new(-size.X, 0)), ("↑", new(0, -size.Y)),
			("↘ 2:1", new(even, even / 2)), ("↗ 2:1", new(even, -even / 2)), ("↙ 2:1", new(-even, even / 2)), ("↖ 2:1", new(-even, -even / 2)) })
			quick.AddChild(Ui.Btn(label, "Шаг на размер выделения в эту сторону", () => { dx.Value = v.X; dy.Value = v.Y; }));
		var box = new VBoxContainer();
		box.AddChild(grid);
		box.AddChild(quick);
		box.AddChild(new Label { Text = $"Выделение {size.X}×{size.Y}. Прозрачное не закрывает то, что под ним. Ещё копию с тем же шагом — Ctrl+Alt+Shift+T.", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(420, 0) });
		Ui.Dialog(this, "Массив", box, () =>
		{
			if (!doc.ArrayCopies((int)count.Value, new Vector2I((int)dx.Value, (int)dy.Value))) Say("массив не получился: нужен шаг и незаблокированный видимый слой");
			view.QueueRedraw();
		}, "Размножить");
	}

	static readonly (string name, int w, int h)[] Presets = { ("64×64", 64, 64), ("128×128", 128, 128), ("256×256", 256, 256), ("320×240", 320, 240) };

	void ShowNewDialog()
	{
		var d = new ConfirmationDialog { Title = "Новый холст", OkButtonText = "Создать", CancelButtonText = "Отмена" };
		var grid = new GridContainer { Columns = 2 };
		var preset = new OptionButton();
		preset.AddItem("Свой");
		foreach (var p in Presets) preset.AddItem(p.name);
		preset.AddItem("По размеру постройки");
		int building = Presets.Length + 1;
		var w = new SpinBox { MinValue = 8, MaxValue = 1024, Value = doc.W };
		var h = new SpinBox { MinValue = 8, MaxValue = 1024, Value = doc.H };
		var bw = new SpinBox { MinValue = 1, MaxValue = 20, Value = doc.BaseW, Prefix = "W" };
		var bd = new SpinBox { MinValue = 1, MaxValue = 20, Value = doc.BaseD, Prefix = "D" };
		var bh = new SpinBox { MinValue = 0, MaxValue = 600, Value = 80, Suffix = "px" };
		var bRow = new HBoxContainer();
		bRow.AddChild(bw); bRow.AddChild(bd);
		var bLabels = new Control[] { new Label { Text = "Основание" }, bRow, new Label { Text = "Здание над основанием" }, bh };
		void Building()
		{
			var nd = Doc.ForBuilding((int)bw.Value, (int)bd.Value, (int)bh.Value);
			w.SetValueNoSignal(nd.W); h.SetValueNoSignal(nd.H);
		}
		preset.ItemSelected += i =>
		{
			foreach (var c in bLabels) c.Visible = i == building;
			if (i == building) Building();
			else if (i > 0) { w.Value = Presets[i - 1].w; h.Value = Presets[i - 1].h; }
		};
		bw.ValueChanged += _ => Building(); bd.ValueChanged += _ => Building(); bh.ValueChanged += _ => Building();
		grid.AddChild(new Label { Text = "Пресет" }); grid.AddChild(preset);
		foreach (var c in bLabels) { c.Visible = false; grid.AddChild(c); }
		grid.AddChild(new Label { Text = "Ширина" }); grid.AddChild(w);
		grid.AddChild(new Label { Text = "Высота" }); grid.AddChild(h);
		d.AddChild(grid);
		AddChild(d);
		d.Confirmed += () =>
		{
			d.QueueFree();
			SetDoc(preset.Selected == building ? Doc.ForBuilding((int)bw.Value, (int)bd.Value, (int)bh.Value) : new Doc((int)w.Value, (int)h.Value));
		};
		d.Canceled += d.QueueFree;
		d.PopupCentered(new Vector2I(380, 0));
	}
}
