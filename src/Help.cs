using Godot;

namespace PixelForge;

/// <summary>F1: every shortcut in one window.</summary>
public static class Help
{
	static readonly (string group, (string keys, string what)[] rows)[] Keys =
	{
		("Инструменты", new[]
		{
			("B", "карандаш (правая кнопка — фоновым цветом)"), ("E", "ластик"), ("G", "заливка"), ("I или зажатый Alt", "пипетка"),
			("L", "линия; Shift — по горизонтали, вертикали или 2:1"), ("R / O", "прямоугольник / эллипс; Shift — квадрат / круг"),
			("M", "выделение; тянуть внутри — перенести, Ctrl — копия"), ("A", "поставить якорь"), ("H, пробел или средняя кнопка", "рука"),
		}),
		("Цвета", new[] { ("X", "поменять основной и фоновый"), ("[ / ]", "светлее / темнее по рампе") }),
		("Вид", new[] { ("колесо, + / −", "зум"), ("Ctrl+0", "вписать в окно"), ("#", "сетка пикселей"), ("F1", "эта справка") }),
		("Правка", new[]
		{
			("Ctrl+Z", "отменить (у плавающего выделения — вернуть на место)"), ("Ctrl+Y, Ctrl+Shift+Z", "вернуть"),
			("Ctrl+X / C / V", "вырезать / копировать / вставить"), ("Delete", "очистить выделенное"),
			("Ctrl+A / Ctrl+D", "выделить всё / снять выделение"), ("Enter / Esc", "опустить плавающее / снять выделение"),
			("Shift+H / Shift+V", "отразить по горизонтали / вертикали"), ("Shift+R", "заменить цвет"),
		}),
		("Слои", new[] { ("Shift+N", "новый слой"), ("Ctrl+J", "дублировать"), ("Ctrl+E", "слить вниз") }),
		("Файл и холст", new[]
		{
			("Ctrl+N / O / S", "новый / открыть / сохранить"), ("Ctrl+Shift+S", "сохранить как"), ("Ctrl+Shift+I", "импорт PNG слоем"),
			("Ctrl+Shift+E", "экспорт PNG"), ("Ctrl+Alt+E", "экспорт для игры"), ("Ctrl+Alt+C", "размер холста"), ("Ctrl+Q", "выход"),
		}),
	};

	public static void Show(Node parent)
	{
		var grid = new GridContainer { Columns = 2 };
		grid.AddThemeConstantOverride("h_separation", 24);
		foreach (var (group, rows) in Keys)
		{
			grid.AddChild(new Label { Text = group, Modulate = new Color(1, 0.75f, 0.4f) });
			grid.AddChild(new Control());
			foreach (var (keys, what) in rows)
			{
				grid.AddChild(new Label { Text = keys });
				grid.AddChild(new Label { Text = what });
			}
		}
		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(820, 640) };
		scroll.AddChild(grid);
		var a = new AcceptDialog { Title = "Горячие клавиши", OkButtonText = "Закрыть" };
		a.AddChild(scroll);
		parent.AddChild(a);
		a.Confirmed += a.QueueFree;
		a.Canceled += a.QueueFree;
		a.PopupCentered();
	}
}
