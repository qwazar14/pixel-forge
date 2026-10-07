using Godot;
using System;
using System.Linq;

namespace PixelForge;

/// <summary>The "Игра" menu's windows: views, export for the game, sketches from rf-game.</summary>
public static class GameDialogs
{
	/// <summary>Which layers make each view, which views get exported; "Показать" switches the canvas to a view.</summary>
	public static void Views(Node parent, Doc doc)
	{
		var grid = new GridContainer { Columns = 5 };
		grid.AddThemeConstantOverride("h_separation", 16);
		void Cell(Control c) { c.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; grid.AddChild(c); }

		grid.AddChild(new Label());
		foreach (var n in Doc.ViewNames) Cell(new Label { Text = n });

		grid.AddChild(new Label { Text = "Экспортировать" });
		for (int v = 0; v < 4; v++)
		{
			int vv = v;
			var on = new CheckBox { ButtonPressed = doc.ViewOn[v] };
			on.Toggled += t => { doc.ViewOn[vv] = t; doc.Touch(); };
			Cell(on);
		}
		grid.AddChild(new HSeparator()); for (int i = 0; i < 4; i++) grid.AddChild(new HSeparator());

		foreach (var l in Enumerable.Reverse(doc.Layers).Where(l => !l.Reference))
		{
			grid.AddChild(new Label { Text = l.Name });
			for (int v = 0; v < 4; v++)
			{
				int vv = v;
				var on = new CheckBox { ButtonPressed = l.Views[v] };
				on.Toggled += t => { l.Views[vv] = t; doc.Touch(); };
				Cell(on);
			}
		}

		grid.AddChild(new Label());
		for (int v = 0; v < 4; v++)
		{
			int vv = v;
			Cell(Ui.Btn("Показать", "Оставить видимыми только слои этого вида", () =>
			{
				foreach (var l in doc.Layers.Where(l => !l.Reference)) l.Visible = l.Views[vv];
				doc.Turned = vv >= 2;
				doc.Touch();
			}));
		}

		var box = new VBoxContainer();
		box.AddChild(new Label { Text = "Слои каждого вида (подложки в экспорт не идут):" });
		box.AddChild(grid);
		var a = new AcceptDialog { Title = "Виды", OkButtonText = "Готово" };
		a.AddChild(box);
		parent.AddChild(a);
		a.Confirmed += a.QueueFree;
		a.Canceled += a.QueueFree;
		a.PopupCentered(new Vector2I(640, 0));
	}

	public static void Export(Node parent, Doc doc, Action<string> say)
	{
		var grid = new GridContainer { Columns = 3 };
		var id = new LineEdit { Text = doc.GameId ?? (doc.Path == null ? "" : System.IO.Path.GetFileNameWithoutExtension(doc.Path)), PlaceholderText = "например izba", CustomMinimumSize = new Vector2(300, 0) };
		var dir = new LineEdit { Text = doc.ExportDir ?? Settings.Get("export_dir", ""), CustomMinimumSize = new Vector2(420, 0) };
		grid.AddChild(new Label { Text = "id" }); grid.AddChild(id); grid.AddChild(new Control());
		grid.AddChild(new Label { Text = "Папка" }); grid.AddChild(dir);
		grid.AddChild(Ui.Btn("…", "Выбрать папку", () => Ui.PickFile(parent, FileDialog.FileModeEnum.OpenDir, Array.Empty<string>(), p => dir.Text = p)));
		var box = new VBoxContainer();
		box.AddChild(grid);
		var views = Enumerable.Range(0, 4).Where(v => doc.ViewOn[v]).ToList();
		box.AddChild(new Label { Text = views.Count == 0 ? "Ни один вид не включён (Игра → Виды…)" : "Файлы: " + string.Join(", ", views.Select(v => "<id>" + Doc.ViewSuffix[v] + ".png")) });
		box.AddChild(new Label { Text = "Поля обрезаются, якорь остаётся нижним средним пикселем.", Modulate = new Color(1, 1, 1, 0.7f) });
		Ui.Dialog(parent, "Экспорт для игры", box, () =>
		{
			var name = id.Text.Trim();
			if (name == "" || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) { Ui.Alert(parent, "Нужен id: латиница, цифры, _"); return; }
			if (!System.IO.Directory.Exists(dir.Text)) { Ui.Alert(parent, $"Нет такой папки:\n{dir.Text}"); return; }
			Settings.Set("export_dir", dir.Text);
			var report = doc.ExportGame(dir.Text, name);
			say(report.Count == 0 ? "нечего экспортировать" : "экспорт: " + string.Join("; ", report));
		}, "Экспорт");
	}

	/// <summary>Файл → Настройки: the sketch folder (relative to PixelForge's folder or absolute) and the default export folder.</summary>
	public static void Preferences(Node parent)
	{
		var grid = new GridContainer { Columns = 3 };
		var sketch = new LineEdit { Text = Settings.Get("sketch_dir", Sketch.DefaultRel), CustomMinimumSize = new Vector2(520, 0) };
		var export = new LineEdit { Text = Settings.Get("export_dir", ""), PlaceholderText = "не задана" };
		var found = new Label { Modulate = new Color(1, 1, 1, 0.7f) };
		void Check()
		{
			var p = Sketch.Resolve(sketch.Text);
			found.Text = (System.IO.Directory.Exists(p) ? "найдена: " : "нет такой папки: ") + p;
		}
		sketch.TextChanged += _ => Check();
		Check();
		var sketchBtns = new HBoxContainer();
		sketchBtns.AddChild(Ui.Btn("…", "Выбрать папку", () => Ui.PickFile(parent, FileDialog.FileModeEnum.OpenDir, Array.Empty<string>(), p => { sketch.Text = p; Check(); })));
		sketchBtns.AddChild(Ui.Btn("По умолчанию", Sketch.DefaultRel, () => { sketch.Text = Sketch.DefaultRel; Check(); }));
		grid.AddChild(new Label { Text = "Эскизы rf-game" }); grid.AddChild(sketch); grid.AddChild(sketchBtns);
		grid.AddChild(new Control()); grid.AddChild(found); grid.AddChild(new Control());
		grid.AddChild(new Label { Text = "Экспорт для игры" }); grid.AddChild(export);
		grid.AddChild(Ui.Btn("…", "Выбрать папку", () => Ui.PickFile(parent, FileDialog.FileModeEnum.OpenDir, Array.Empty<string>(), p => export.Text = p)));
		var box = new VBoxContainer();
		box.AddChild(grid);
		box.AddChild(new Label { Text = "Относительный путь считается от папки PixelForge (или выше, если там нет).", Modulate = new Color(1, 1, 1, 0.7f) });
		Ui.Dialog(parent, "Настройки", box, () =>
		{
			Settings.Set("sketch_dir", sketch.Text.Trim());
			Settings.Set("export_dir", export.Text.Trim());
		}, "Сохранить");
	}

	/// <summary>Picks a sketch; opens it as a new project or adds it as a reference layer.</summary>
	public static void OpenSketch(Node parent, Doc doc, Action<Doc> opened, Action<string> say)
	{
		var box = new VBoxContainer { CustomMinimumSize = new Vector2(620, 560) };
		var row = new HBoxContainer();
		var dir = new LineEdit { Text = Settings.Get("sketch_dir", Sketch.DefaultRel), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		row.AddChild(dir);
		var filter = new LineEdit { PlaceholderText = "поиск" };
		var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
		var all = new System.Collections.Generic.List<Sketch>();
		void Fill()
		{
			list.Clear();
			var f = filter.Text.Trim().ToLowerInvariant();
			foreach (var s in all.Where(s => f == "" || s.Title.ToLowerInvariant().Contains(f)))
				list.SetItemMetadata(list.AddItem(s.Title), all.IndexOf(s));
		}
		void Reload()
		{
			all = Sketch.List(Sketch.Resolve(dir.Text));
			Settings.Set("sketch_dir", dir.Text);
			Fill();
		}
		row.AddChild(Ui.Btn("…", "Выбрать папку", () => Ui.PickFile(parent, FileDialog.FileModeEnum.OpenDir, Array.Empty<string>(), p => { dir.Text = p; Reload(); })));
		row.AddChild(Ui.Btn("Обновить", "Перечитать папку", Reload));
		box.AddChild(row);
		box.AddChild(filter);
		box.AddChild(list);
		var fresh = new CheckBox { Text = "Открыть в новом проекте (иначе подложкой в текущий, по якорю)", ButtonPressed = true };
		box.AddChild(fresh);
		filter.TextChanged += _ => Fill();
		Reload();

		void Go()
		{
			var sel = list.GetSelectedItems();
			if (sel.Length == 0) return;
			var s = all[list.GetItemMetadata(sel[0]).AsInt32()];
			if (fresh.ButtonPressed)
			{
				var d = s.NewDoc();
				if (d == null) { Ui.Alert(parent, $"Не удалось прочитать {s.File}"); return; }
				opened(d);
			}
			else if (!s.AddTo(doc)) { Ui.Alert(parent, $"Не удалось прочитать {s.File}"); return; }
			say($"эскиз {s.Name}: основание {s.W}×{s.D}" + (s.Fx < 0 ? " (нет в index.json — поставлен низом-серединой на якорь)" : ""));
		}
		var dlg = Ui.Dialog(parent, "Эскиз из rf-game", box, Go, "Открыть");
		list.ItemActivated += _ => { dlg.Hide(); dlg.QueueFree(); Go(); };
	}
}
