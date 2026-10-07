using Godot;

namespace PixelForge;

/// <summary>Layer list (top layer first) plus the selected layer's properties.</summary>
public partial class LayersPanel : VBoxContainer
{
	Doc doc;
	VBoxContainer list;
	LineEdit name;
	HSlider opacity;
	Label opLabel;
	CheckBox lockBox, refBox;

	public override void _Ready()
	{
		AddChild(new Label { Text = "Слои" });
		var tools = new HFlowContainer();
		tools.AddChild(Ui.Btn("+", "Новый слой (Shift+N)", () => doc.AddLayer()));
		tools.AddChild(Ui.Btn("⧉", "Дублировать слой (Ctrl+J)", () => doc.DuplicateLayer()));
		tools.AddChild(Ui.Btn("✕", "Удалить слой", () => doc.DeleteLayer()));
		tools.AddChild(Ui.Btn("▲", "Выше", () => doc.MoveLayer(1)));
		tools.AddChild(Ui.Btn("▼", "Ниже", () => doc.MoveLayer(-1)));
		tools.AddChild(Ui.Btn("Слить ↓", "Объединить с нижним (Ctrl+E)", () => doc.MergeDown()));
		tools.AddChild(Ui.Btn("Свести", "Свести все видимые слои (подложки не трогает)", () => doc.FlattenVisible()));
		AddChild(tools);

		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 200), SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		scroll.AddChild(list);
		AddChild(scroll);

		name = new LineEdit { PlaceholderText = "Имя слоя" };
		name.TextSubmitted += _ => { ApplyName(); name.ReleaseFocus(); };
		name.FocusExited += ApplyName;
		AddChild(name);

		var row = new HBoxContainer();
		row.AddChild(new Label { Text = "Непрозрачность" });
		opacity = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, FocusMode = FocusModeEnum.None };
		opacity.ValueChanged += v => { doc.Cur.Opacity = (float)v / 100; opLabel.Text = $"{v}"; doc.Touch(); };
		opLabel = new Label { CustomMinimumSize = new Vector2(40, 0) };
		row.AddChild(opacity);
		row.AddChild(opLabel);
		AddChild(row);

		lockBox = new CheckBox { Text = "Заблокирован", FocusMode = FocusModeEnum.None };
		lockBox.Toggled += on => { doc.Cur.Locked = on; doc.Touch(); };
		refBox = new CheckBox { Text = "Подложка (не экспортируется)", FocusMode = FocusModeEnum.None };
		refBox.Toggled += on => { doc.Cur.Reference = on; doc.Touch(); };
		AddChild(lockBox);
		AddChild(refBox);
	}

	void ApplyName()
	{
		if (doc == null || name.Text == doc.Cur.Name || name.Text.Trim() == "") return;
		doc.Cur.Name = name.Text.Trim();
		doc.Touch();
	}

	public void SetDoc(Doc d)
	{
		doc = d;
		d.Changed += () => pending = true; // rebuilt once per frame, never inside a button's own signal
		Rebuild();
	}

	bool pending;
	static readonly StyleBoxFlat Selected = new() { BgColor = new Color(0.22f, 0.36f, 0.55f), ContentMarginLeft = 6, ContentMarginRight = 6 };

	public override void _Process(double delta)
	{
		if (pending) { pending = false; Rebuild(); }
	}

	void Rebuild()
	{
		foreach (var c in list.GetChildren()) { list.RemoveChild(c); c.QueueFree(); }
		for (int i = doc.Layers.Count - 1; i >= 0; i--)
		{
			var l = doc.Layers[i];
			int idx = i;
			var row = new HBoxContainer();
			var eye = Ui.Btn(l.Visible ? "👁" : "   ", "Показать / скрыть", () => { l.Visible = !l.Visible; doc.Touch(); });
			eye.CustomMinimumSize = new Vector2(40, 0);
			var tag = (l.Reference ? "  [подложка]" : "") + (l.Locked ? "  🔒" : "") + (l.Opacity < 1 ? $"  {Mathf.RoundToInt(l.Opacity * 100)}%" : "");
			var sel = Ui.Btn(l.Name + tag, "", () => doc.Select(idx));
			sel.ToggleMode = true;
			sel.ButtonPressed = i == doc.Current;
			if (i == doc.Current)
				foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed" })
					sel.AddThemeStyleboxOverride(st, Selected);
			sel.Alignment = HorizontalAlignment.Left;
			sel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			sel.ClipText = true;
			if (!l.Visible) sel.Modulate = new Color(1, 1, 1, 0.5f);
			row.AddChild(eye);
			row.AddChild(sel);
			list.AddChild(row);
		}
		var cur = doc.Cur;
		if (!name.HasFocus()) name.Text = cur.Name;
		opacity.SetValueNoSignal(Mathf.RoundToInt(cur.Opacity * 100));
		opLabel.Text = $"{Mathf.RoundToInt(cur.Opacity * 100)}";
		lockBox.SetPressedNoSignal(cur.Locked);
		refBox.SetPressedNoSignal(cur.Reference);
	}
}
