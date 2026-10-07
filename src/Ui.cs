using Godot;
using System;

namespace PixelForge;

/// <summary>Small one-shot dialogs shared by the panels.</summary>
public static class Ui
{
	public static string LastDir = Settings.Get("last_dir");

	public static void Alert(Node parent, string text)
	{
		var a = new AcceptDialog { DialogText = text, Title = "PixelForge" };
		parent.AddChild(a);
		a.Confirmed += a.QueueFree;
		a.Canceled += a.QueueFree;
		a.PopupCentered();
	}

	public static void Confirm(Node parent, string text, Action then)
	{
		var d = new ConfirmationDialog { DialogText = text, Title = "PixelForge", OkButtonText = "Продолжить", CancelButtonText = "Отмена" };
		parent.AddChild(d);
		d.Confirmed += () => { d.QueueFree(); then(); };
		d.Canceled += d.QueueFree;
		d.PopupCentered();
	}

	/// <summary>Dialog with custom content; ok runs before it closes.</summary>
	public static ConfirmationDialog Dialog(Node parent, string title, Control content, Action ok, string okText = "OK")
	{
		var d = new ConfirmationDialog { Title = title, OkButtonText = okText, CancelButtonText = "Отмена" };
		d.AddChild(content);
		parent.AddChild(d);
		d.Confirmed += () => { d.QueueFree(); ok(); };
		d.Canceled += d.QueueFree;
		d.PopupCentered(new Vector2I(420, 0));
		return d;
	}

	public static void Prompt(Node parent, string title, string initial, Action<string> done)
	{
		var e = new LineEdit { Text = initial, SelectAllOnFocus = true };
		var d = Dialog(parent, title, e, () => done(e.Text));
		d.RegisterTextEnter(e);
		e.CallDeferred(Control.MethodName.GrabFocus);
	}

	public static void PickFile(Node parent, FileDialog.FileModeEnum mode, string[] filters, Action<string> done)
	{
		var fd = new FileDialog { FileMode = mode, Access = FileDialog.AccessEnum.Filesystem, Filters = filters, UseNativeDialog = true };
		if (LastDir != null) fd.CurrentDir = LastDir;
		parent.AddChild(fd);
		void Picked(string p)
		{
			fd.QueueFree();
			LastDir = mode == FileDialog.FileModeEnum.OpenDir ? p : System.IO.Path.GetDirectoryName(p);
			Settings.Set("last_dir", LastDir);
			done(p);
		}
		fd.FileSelected += Picked;
		fd.DirSelected += Picked;
		fd.Canceled += fd.QueueFree;
		fd.PopupCentered(new Vector2I(900, 600));
	}

	public static Button Btn(string text, string tip, Action pressed)
	{
		var b = new Button { Text = text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None };
		b.Pressed += pressed;
		return b;
	}
}
