// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Serialization.Implementation;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Files.App.Services.Settings
{
	[JsonSourceGenerationOptions(WriteIndented = true)]
	[JsonSerializable(typeof(object))]
	[JsonSerializable(typeof(ConcurrentDictionary<string, JsonElement>))]
	[JsonSerializable(typeof(List<NamedLayout>))]
	[JsonSerializable(typeof(NamedLayout))]
	internal sealed partial class LayoutProfilesJsonSerializationContext : JsonSerializerContext
	{
	}

	/// <summary>
	/// Persists named multi-pane workspace layouts to settings\layout_profiles.json.
	/// </summary>
	internal sealed class LayoutProfilesSettingsService : BaseJsonSettings, ILayoutProfilesSettingsService
	{
		public event EventHandler? OnLayoutsUpdated;

		public LayoutProfilesSettingsService()
		{
			var settingsSerializer = new DefaultSettingsSerializer();
			SettingsSerializer = settingsSerializer;

			Initialize(Path.Combine(AppDataCompat.LocalFolderPath,
				Constants.LocalSettings.SettingsFolderName, Constants.LocalSettings.LayoutProfilesSettingsFileName));

			var jsonSettingsSerializer = new DefaultJsonSettingsSerializer();
			JsonSettingsSerializer = jsonSettingsSerializer;
			JsonSettingsDatabase = new CachingJsonSettingsDatabase(
				settingsSerializer,
				jsonSettingsSerializer,
				LayoutProfilesJsonSerializationContext.Default);
		}

		public List<NamedLayout> Layouts
		{
			get => Get<List<NamedLayout>>([]) ?? [];
			set
			{
				Set(value);
				OnLayoutsUpdated?.Invoke(this, EventArgs.Empty);
			}
		}

		public void Save(NamedLayout layout)
		{
			var layouts = Layouts;
			var index = layouts.FindIndex(x => string.Equals(x.Name, layout.Name, StringComparison.OrdinalIgnoreCase));
			if (index >= 0)
				layouts[index] = layout;
			else
				layouts.Add(layout);

			Layouts = layouts;
		}

		public bool Delete(string name)
		{
			var layouts = Layouts;
			var removed = layouts.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
			if (removed > 0)
				Layouts = layouts;

			return removed > 0;
		}

		public NamedLayout? GetByName(string name)
			=> Layouts.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
	}
}
