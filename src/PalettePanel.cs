using Godot;
using System;
using System.Linq;

namespace PixelForge;

/// <summary>Ramps of swatches. The "selected" palette colour is simply the one equal to the primary colour.</summary>
public partial class PalettePanel : VBoxContainer
{
	enum Cmd { NewRamp, RenameRamp, DeleteRamp, RampUp, RampDown, Load, Save, FromImage, Strict }

	Doc doc;
	VBoxContainer ramps;
	LineEdit colorName;
	PopupMenu menu;
	Ramp activeRamp; // where "+" adds when the primary colour isn't in the palette

	public Func<Color> Fg;
	public event Action<Color, bool> ColorChosen; // colour, primary?

	Palette Pal => doc.Palette;

	public override void _Ready()
	{
		var head = new HBoxContainer();
		head.AddChild(new Label { Text = "Палитра", SizeFlagsHorizontal = SizeFlags.ExpandFill });
		var mb = new MenuButton { Text = "☰", FocusMode = FocusModeEnum.None, Flat = false };
		menu = mb.GetPopup();
		menu.AddItem("Новая рампа", (int)Cmd.NewRamp);
		menu.AddItem("Переименовать рампу", (int)Cmd.RenameRamp);
		menu.AddItem("Удалить рампу", (int)Cmd.DeleteRamp);
		menu.AddItem("Рампу выше", (int)Cmd.RampUp);
		menu.AddItem("Рампу ниже", (int)Cmd.RampDown);
		menu.AddSeparator();
		menu.AddItem("Загрузить / импорт…", (int)Cmd.Load);
		menu.AddItem("Сохранить палитру…", (int)Cmd.Save);
		menu.AddItem("Палитра из картинки…", (int)Cmd.FromImage);
		menu.AddSeparator();
		menu.AddCheckItem("Только палитра", (int)Cmd.Strict);
		menu.IdPressed += id => Run((Cmd)id);
		head.AddChild(mb);
		AddChild(head);

		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 160), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		ramps = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		scroll.AddChild(ramps);
		AddChild(scroll);

		var tools = new HFlowContainer();
		tools.AddChild(Ui.Btn("+", "Добавить основной цвет в рампу", AddColor));
		tools.AddChild(Ui.Btn("−", "Убрать цвет из палитры", () => Edit((r, i) => r.Colors.RemoveAt(i))));
		tools.AddChild(Ui.Btn("◀", "Сдвинуть цвет темнее по рампе", () => Edit((r, i) => Swap(r, i, i - 1))));
		tools.AddChild(Ui.Btn("▶", "Сдвинуть цвет светлее по рампе", () => Edit((r, i) => Swap(r, i, i + 1))));
		tools.AddChild(Ui.Btn("В рампу…", "Перенести цвет в другую рампу", MoveToRamp));
		AddChild(tools);

		colorName = new LineEdit { PlaceholderText = "Имя цвета" };
		colorName.TextSubmitted += _ => { ApplyName(); colorName.ReleaseFocus(); };
		colorName.FocusExited += ApplyName;
		AddChild(colorName);
	}

	public void SetDoc(Doc d)
	{
		doc = d;
		activeRamp = Pal.Ramps[0];
		d.Changed += () => pending = true; // rebuilt once per frame, never inside a button's own signal
		Rebuild();
	}

	/// <summary>Primary colour changed: move the highlight.</summary>
	public void FgChanged()
	{
		var (r, i) = Pal.Find(Fg());
		if (r != null) activeRamp = r;
		foreach (var s in ramps.GetChildren().OfType<HFlowContainer>().SelectMany(f => f.GetChildren().OfType<Swatch>())) s.QueueRedraw();
		foreach (var l in ramps.GetChildren().OfType<Label>()) l.Modulate = Pal.Ramps.IndexOf(activeRamp) == l.GetMeta("ramp").AsInt32() ? Colors.White : new Color(1, 1, 1, 0.6f);
		if (!colorName.HasFocus()) colorName.Text = r == null ? "" : r.Colors[i].Name;
		colorName.Editable = r != null;
	}

	bool pending;

	public override void _Process(double delta)
	{
		if (pending) { pending = false; Rebuild(); }
	}

	void Rebuild()
	{
		if (!Pal.Ramps.Contains(activeRamp)) activeRamp = Pal.Ramps[0];
		foreach (var c in ramps.GetChildren()) { ramps.RemoveChild(c); c.QueueFree(); }
		foreach (var r in Pal.Ramps)
		{
			var title = new Label { Text = r.Name, MouseFilter = MouseFilterEnum.Stop, TooltipText = "Клик: сделать рампу активной" };
			title.SetMeta("ramp", Pal.Ramps.IndexOf(r));
			title.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) { activeRamp = r; FgChanged(); } };
			ramps.AddChild(title);
			var flow = new HFlowContainer();
			foreach (var pc in r.Colors)
			{
				var sw = new Swatch { C = pc.C, IsFg = () => Fg().ToRgba32() == pc.C.ToRgba32(), TooltipText = (pc.Name == "" ? "" : pc.Name + "  ") + "#" + pc.C.ToHtml(pc.C.A < 1) };
				sw.Clicked += b => ColorChosen?.Invoke(pc.C, b == MouseButton.Left);
				flow.AddChild(sw);
			}
			ramps.AddChild(flow);
		}
		menu.SetItemChecked(menu.GetItemIndex((int)Cmd.Strict), Pal.Strict);
		FgChanged();
	}

	static void Swap(Ramp r, int i, int j)
	{
		if (j >= 0 && j < r.Colors.Count) (r.Colors[i], r.Colors[j]) = (r.Colors[j], r.Colors[i]);
	}

	/// <summary>Edits the palette entry equal to the primary colour.</summary>
	void Edit(Action<Ramp, int> change)
	{
		var (r, i) = Pal.Find(Fg());
		if (r == null) return;
		change(r, i);
		doc.Touch();
	}

	void AddColor()
	{
		var c = Fg();
		if (c.A == 0 || Pal.Contains(c)) return;
		activeRamp.Colors.Add(new PalColor { C = c });
		doc.Touch();
	}

	void ApplyName()
	{
		var (r, i) = Pal.Find(Fg());
		if (r == null || r.Colors[i].Name == colorName.Text) return;
		r.Colors[i].Name = colorName.Text;
		doc.Touch();
	}

	void MoveToRamp()
	{
		var (from, i) = Pal.Find(Fg());
		if (from == null || Pal.Ramps.Count < 2) return;
		var pick = new OptionButton();
		foreach (var r in Pal.Ramps) pick.AddItem(r.Name);
		pick.Selected = Pal.Ramps.IndexOf(from);
		Ui.Dialog(this, "Перенести цвет в рампу", pick, () =>
		{
			var to = Pal.Ramps[pick.Selected];
			if (to == from) return;
			var pc = from.Colors[i];
			from.Colors.RemoveAt(i);
			to.Colors.Add(pc);
			activeRamp = to;
			doc.Touch();
		});
	}

	void Run(Cmd c)
	{
		int ri = Pal.Ramps.IndexOf(activeRamp);
		switch (c)
		{
			case Cmd.NewRamp:
				Ui.Prompt(this, "Новая рампа", "Рампа " + (Pal.Ramps.Count + 1), n =>
				{
					activeRamp = new Ramp { Name = n };
					Pal.Ramps.Add(activeRamp);
					doc.Touch();
				});
				break;
			case Cmd.RenameRamp:
				Ui.Prompt(this, "Имя рампы", activeRamp.Name, n => { activeRamp.Name = n; doc.Touch(); });
				break;
			case Cmd.DeleteRamp:
				Ui.Confirm(this, $"Удалить рампу «{activeRamp.Name}» вместе с её цветами?", () =>
				{
					Pal.Ramps.Remove(activeRamp);
					if (Pal.Ramps.Count == 0) Pal.Ramps.Add(new Ramp { Name = "Основная" });
					doc.Touch();
				});
				break;
			case Cmd.RampUp or Cmd.RampDown:
				int j = ri + (c == Cmd.RampUp ? -1 : 1);
				if (j < 0 || j >= Pal.Ramps.Count) return;
				(Pal.Ramps[ri], Pal.Ramps[j]) = (Pal.Ramps[j], Pal.Ramps[ri]);
				doc.Touch();
				break;
			case Cmd.Load:
				Ui.PickFile(this, FileDialog.FileModeEnum.OpenFile, new[] { "*.pal.json ; Палитра PixelForge", "*.gpl ; GIMP", "*.hex ; HEX", "*.png ; Картинка" }, Load);
				break;
			case Cmd.Save:
				Ui.PickFile(this, FileDialog.FileModeEnum.SaveFile, new[] { "*.pal.json ; Палитра PixelForge" }, p =>
				{
					if (!p.EndsWith(".pal.json")) p = System.IO.Path.ChangeExtension(p, null) + ".pal.json";
					try { Pal.SaveJson(p); } catch (Exception e) { Ui.Alert(this, e.Message); }
				});
				break;
			case Cmd.FromImage:
				var limit = new SpinBox { MinValue = 1, MaxValue = 256, Value = 32, Prefix = "не больше", Suffix = "цветов" };
				Ui.Dialog(this, "Палитра из картинки (видимые слои)", limit, () =>
				{
					var cols = Palette.UniqueColors(doc.Composite(), (int)limit.Value);
					if (cols.Count == 0) return;
					activeRamp = new Ramp { Name = "Из картинки", Colors = cols.Select(x => new PalColor { C = x }).ToList() };
					Pal.Ramps.Add(activeRamp);
					doc.Touch();
				});
				break;
			case Cmd.Strict:
				Pal.Strict = !Pal.Strict;
				doc.Touch();
				break;
		}
	}

	void Load(string path)
	{
		try
		{
			if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			{
				var strict = Pal.Strict;
				doc.Palette = Palette.LoadJson(path);
				doc.Palette.Strict = strict;
			}
			else
			{
				var r = Palette.ImportRamp(path) ?? throw new Exception("В файле нет цветов");
				Pal.Ramps.Add(r);
				activeRamp = r;
			}
			doc.Touch();
		}
		catch (Exception e) { Ui.Alert(this, $"Не удалось загрузить палитру:\n{e.Message}"); }
	}
}

/// <summary>One palette colour; outlined when it is the primary colour.</summary>
public partial class Swatch : Control
{
	public Color C;
	public Func<bool> IsFg;
	public event Action<MouseButton> Clicked;

	public override void _Ready() => CustomMinimumSize = new Vector2(30, 30);

	public override void _GuiInput(InputEvent e)
	{
		if (e is InputEventMouseButton { Pressed: true } mb && mb.ButtonIndex is MouseButton.Left or MouseButton.Right)
		{
			Clicked?.Invoke(mb.ButtonIndex);
			AcceptEvent();
		}
	}

	public override void _Draw()
	{
		var r = new Rect2(Vector2.Zero, Size);
		DrawRect(r, C);
		if (IsFg())
		{
			DrawRect(r, Colors.White, false, 3);
			DrawRect(r.Grow(-3), Colors.Black, false, 1);
		}
	}
}
