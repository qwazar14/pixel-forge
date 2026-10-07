using Godot;

namespace PixelForge;

/// <summary>F1: every shortcut in one window (Photoshop-style keys).</summary>
public static class Help
{
	static readonly (string group, (string keys, string what)[] rows)[] Keys =
	{
		("Инструменты", new[]
		{
			("V", "перемещение: тянуть выделенное откуда угодно (без выделения — весь слой); Alt — копия"),
			("M", "выделение; тянуть внутри — перенести, Alt или Ctrl — копия"),
			("B", "карандаш (правая кнопка — фоновым цветом)"), ("E", "ластик"), ("G", "заливка"), ("I или Alt с кистью", "пипетка"),
			("U / Shift+U", "фигура / следующая: прямоугольник → эллипс → линия"),
			("Shift с фигурой", "линия 2:1, квадрат, круг"), ("A", "поставить якорь"), ("H, пробел или средняя кнопка", "рука"),
			("[ / ]", "кисть меньше / больше"),
		}),
		("Изо-режим (Ctrl+Shift+;)", new[]
		{
			("линия", "всегда по горизонтали, вертикали или 2:1"), ("прямоугольник", "ромб на земле со сторонами 2:1"), ("эллипс", "изо-круг: ширина вдвое больше высоты"),
		}),
		("Цвета", new[] { ("X", "поменять основной и фоновый"), ("D", "чёрный и белый"), ("Shift+[ / Shift+]", "светлее / темнее по рампе") }),
		("Вид", new[] { ("колесо, Ctrl+= / Ctrl+−", "зум"), ("Ctrl+1", "1×"), ("Ctrl+0", "вписать в окно"), ("Ctrl+'", "сетка пикселей"), ("F1", "эта справка") }),
		("Правка", new[]
		{
			("Ctrl+Z / Ctrl+Alt+Z", "отменить (у плавающего выделения — вернуть на место)"), ("Ctrl+Shift+Z, Ctrl+Y", "вернуть"),
			("Ctrl+X / C / V", "вырезать / копировать / вставить"), ("Delete, Backspace", "очистить выделенное"),
			("Alt+Backspace / Ctrl+Backspace", "залить выделенное основным / фоновым"),
			("Ctrl+A / Ctrl+D", "выделить всё / снять выделение"), ("стрелки, Shift+стрелки", "сдвинуть выделенное на 1 / 10 px"),
			("Enter / Esc", "опустить плавающее / снять выделение"),
			("Ctrl+J", "дубль выделенного на новый слой (без выделения — весь слой)"),
			("Ctrl+Alt+T", "массив: N копий выделенного с шагом"), ("Ctrl+Alt+Shift+T", "ещё одна копия с тем же шагом"),
			("Shift+H / Shift+V", "отразить по горизонтали / вертикали"), ("Shift+R", "заменить цвет"),
		}),
		("Слои", new[] { ("Ctrl+Shift+N", "новый слой"), ("Ctrl+] / Ctrl+[", "выше / ниже"), ("Ctrl+E", "объединить с нижним"), ("Ctrl+Shift+E", "объединить видимые") }),
		("Файл и холст", new[]
		{
			("Ctrl+N / O / S", "новый / открыть / сохранить"), ("Ctrl+Shift+S", "сохранить как"), ("Ctrl+Alt+Shift+S", "экспорт PNG"),
			("Ctrl+Alt+E", "экспорт для игры"), ("Ctrl+Alt+C", "размер холста"), ("Ctrl+Q", "выход"),
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
		var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(980, 700) };
		scroll.AddChild(grid);
		var a = new AcceptDialog { Title = "Горячие клавиши", OkButtonText = "Закрыть" };
		a.AddChild(scroll);
		parent.AddChild(a);
		a.Confirmed += a.QueueFree;
		a.Canceled += a.QueueFree;
		a.PopupCentered();
	}
}
