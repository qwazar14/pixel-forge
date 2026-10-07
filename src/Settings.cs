using Godot;

namespace PixelForge;

/// <summary>Editor settings in user://settings.cfg, saved on every change.</summary>
public static class Settings
{
	const string File = "user://settings.cfg";
	static ConfigFile cfg;

	static ConfigFile C
	{
		get
		{
			if (cfg != null) return cfg;
			cfg = new ConfigFile();
			cfg.Load(File); // missing on first run: stays empty
			return cfg;
		}
	}

	public static string Get(string key, string def = null) => C.GetValue("main", key, def ?? "").AsString() is var v && v != "" ? v : def;

	public static string[] GetList(string key) => C.GetValue("main", key, new string[0]).AsStringArray();

	public static void SetList(string key, string[] value)
	{
		C.SetValue("main", key, value);
		C.Save(File);
	}

	public static void Set(string key, string value)
	{
		C.SetValue("main", key, value);
		C.Save(File);
	}
}
