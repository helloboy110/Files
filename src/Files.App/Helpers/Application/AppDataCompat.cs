// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace Files.App.Helpers
{
	/// <summary>
	/// Provides access to application data that works in both packaged and unpackaged environments.
	/// In unpackaged environments, data is redirected to folders under %LOCALAPPDATA%\Files\Unpackaged.
	/// </summary>
	public static class AppDataCompat
	{
		private static readonly ApplicationData? _packagedData = TryGetApplicationData();

		private static string? _localFolderPath;

		/// <summary>
		/// Gets the application data of the current package, or <see langword="null"/> when the process has no package identity (unpackaged).
		/// </summary>
		public static ApplicationData? CurrentData => _packagedData;

		/// <summary>
		/// Gets a value that indicates whether the process has package identity.
		/// </summary>
		public static bool HasPackageIdentity => _packagedData is not null;

		/// <summary>
		/// Gets the local settings values, backed by a JSON file when unpackaged.
		/// </summary>
		public static IDictionary<string, object?> LocalSettingsValues
		{
			get
			{
				if (_packagedData is not null)
					return _packagedData.LocalSettings.Values;

				return JsonBackedDictionary.GetShared(Path.Combine(LocalFolderPath, "localsettings.json"));
			}
		}

		/// <summary>
		/// Gets the local app data folder.
		/// </summary>
		public static string LocalFolderPath
			=> _localFolderPath ??= _packagedData?.LocalFolder.Path
				?? CreateDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Files", "Unpackaged"));

		/// <summary>
		/// Gets the local app data folder.
		/// </summary>
		public static StorageFolder LocalFolder => GetStorageFolder(LocalFolderPath);

		/// <summary>
		/// Gets the roaming app data folder.
		/// </summary>
		public static StorageFolder RoamingFolder
			=> _packagedData?.RoamingFolder ?? GetStorageFolder(Path.Combine(LocalFolderPath, "Roaming"));

		/// <summary>
		/// Gets the local cache folder.
		/// </summary>
		public static StorageFolder LocalCacheFolder
			=> _packagedData?.LocalCacheFolder ?? GetStorageFolder(Path.Combine(LocalFolderPath, "LocalCache"));

		/// <summary>
		/// Gets the temporary app data folder.
		/// </summary>
		public static StorageFolder TemporaryFolder
			=> _packagedData?.TemporaryFolder ?? GetStorageFolder(Path.Combine(LocalFolderPath, "Temporary"));

		/// <summary>
		/// Determines whether a named settings container exists.
		/// </summary>
		public static bool ContainerExists(string name)
			=> _packagedData?.LocalSettings.Containers.ContainsKey(name)
				?? File.Exists(GetContainerPath(name));

		/// <summary>
		/// Gets the values of a named settings container if it exists.
		/// </summary>
		public static bool TryGetContainerValues(string name, out IDictionary<string, object?> values)
		{
			if (_packagedData is not null)
			{
				if (_packagedData.LocalSettings.Containers.TryGetValue(name, out var container))
				{
					values = container.Values;
					return true;
				}

				values = null!;
				return false;
			}

			var path = GetContainerPath(name);
			if (File.Exists(path))
			{
				values = JsonBackedDictionary.GetShared(path);
				return true;
			}

			values = null!;
			return false;
		}

		/// <summary>
		/// Creates or opens a named settings container and returns its values.
		/// </summary>
		public static IDictionary<string, object?> CreateContainerValues(string name)
		{
			if (_packagedData is not null)
				return _packagedData.LocalSettings.CreateContainer(name, ApplicationDataCreateDisposition.Always).Values;

			var path = GetContainerPath(name);
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			return JsonBackedDictionary.GetShared(path);
		}

		/// <summary>
		/// Deletes a named settings container.
		/// </summary>
		public static void DeleteContainer(string name)
		{
			if (_packagedData is not null)
				_packagedData.LocalSettings.DeleteContainer(name);
			else
				File.Delete(GetContainerPath(name));
		}

		private static string GetContainerPath(string name)
			=> Path.Combine(LocalFolderPath, "containers", name + ".json");

		private static StorageFolder GetStorageFolder(string path)
		{
			Directory.CreateDirectory(path);
			return StorageFolder.GetFolderFromPathAsync(path).AsTask().GetAwaiter().GetResult();
		}

		private static string CreateDirectory(string path)
		{
			Directory.CreateDirectory(path);
			return path;
		}

		private static ApplicationData? TryGetApplicationData()
		{
			try
			{
				return ApplicationData.Current;
			}
			catch
			{
				return null;
			}
		}
	}

	/// <summary>
	/// Represents a dictionary that persists its contents to a JSON file.
	/// </summary>
	internal sealed class JsonBackedDictionary : IDictionary<string, object?>
	{
		private static readonly Dictionary<string, JsonBackedDictionary> _shared = [];
		private static readonly object _sharedLock = new();

		private static readonly JsonSerializerOptions _serializerOptions = new()
		{
			WriteIndented = true,
			Converters = { new ObjectToPrimitiveConverter() }
		};

		private readonly string _filePath;
		private readonly object _lock = new();
		private Dictionary<string, object?> _values;

		private JsonBackedDictionary(string filePath)
		{
			_filePath = filePath;

			try
			{
				if (File.Exists(filePath))
				{
					_values = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(filePath), _serializerOptions) ?? [];
					return;
				}
			}
			catch
			{
				// Ignore corrupted settings
			}

			_values = [];
		}

		public static JsonBackedDictionary GetShared(string filePath)
		{
			lock (_sharedLock)
			{
				if (!_shared.TryGetValue(filePath, out var instance))
				{
					instance = new JsonBackedDictionary(filePath);
					_shared[filePath] = instance;
				}

				return instance;
			}
		}

		public object? this[string key]
		{
			get
			{
				lock (_lock)
					return _values.TryGetValue(key, out var value) ? value : null;
			}
			set
			{
				lock (_lock)
				{
					_values[key] = value;
					Save();
				}
			}
		}

		public int Count { get { lock (_lock) return _values.Count; } }
		public bool IsReadOnly => false;
		public ICollection<string> Keys { get { lock (_lock) return _values.Keys.ToArray(); } }
		public ICollection<object?> Values { get { lock (_lock) return _values.Values.ToArray(); } }

		public void Add(string key, object? value)
		{
			lock (_lock)
			{
				_values.Add(key, value);
				Save();
			}
		}

		public bool ContainsKey(string key)
		{
			lock (_lock)
				return _values.ContainsKey(key);
		}

		public bool Remove(string key)
		{
			lock (_lock)
			{
				if (!_values.Remove(key))
					return false;

				Save();
				return true;
			}
		}

		public bool TryGetValue(string key, out object? value)
		{
			lock (_lock)
				return _values.TryGetValue(key, out value);
		}

		void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item) => Add(item.Key, item.Value);
		public void Clear()
		{
			lock (_lock)
			{
				_values.Clear();
				Save();
			}
		}
		bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item)
		{
			lock (_lock)
				return ((ICollection<KeyValuePair<string, object?>>)_values).Contains(item);
		}
		void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
		{
			lock (_lock)
				((ICollection<KeyValuePair<string, object?>>)_values).CopyTo(array, arrayIndex);
		}
		bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item) => Remove(item.Key);
		public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
		{
			lock (_lock)
				return _values.GetEnumerator();
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		private void Save()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
				File.WriteAllText(_filePath, JsonSerializer.Serialize(_values, _serializerOptions));
			}
			catch
			{
				// Ignore write failures (e.g., file contention during shutdown)
			}
		}
	}

	/// <summary>
	/// Converts JSON values to and from CLR primitives so that stored settings keep their original types.
	/// </summary>
	internal sealed class ObjectToPrimitiveConverter : JsonConverter<object?>
	{
		public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			return reader.TokenType switch
			{
				JsonTokenType.True => true,
				JsonTokenType.False => false,
				JsonTokenType.Number when reader.TryGetInt32(out var i) => i,
				JsonTokenType.Number => reader.GetDouble(),
				JsonTokenType.String => reader.GetString(),
				_ => null
			};
		}

		public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
		{
			switch (value)
			{
				case null: writer.WriteNullValue(); break;
				case bool b: writer.WriteBooleanValue(b); break;
				case string s: writer.WriteStringValue(s); break;
				case int i: writer.WriteNumberValue(i); break;
				case long l: writer.WriteNumberValue(l); break;
				case double d: writer.WriteNumberValue(d); break;
				default: writer.WriteStringValue(value.ToString()); break;
			}
		}
	}
}
