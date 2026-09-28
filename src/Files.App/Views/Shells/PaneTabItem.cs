// Copyright (c) Files Community
// Licensed under the MIT License.

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Files.App.Views.Shells
{
	/// <summary>
	/// Represents a folder tab inside a single pane (QDir-style pane tabs).
	/// The tab remembers its path; activating it re-navigates the pane.
	/// </summary>
	public sealed class PaneTabItem : INotifyPropertyChanged
	{
		private string _path;
		private string _name;
		private bool _isSelected;

		public string Path
		{
			get => _path;
			set
			{
				if (_path != value)
				{
					_path = value;
					Name = TabNameFromPath(value);
					NotifyPropertyChanged(nameof(Path));
				}
			}
		}

		public string Name
		{
			get => _name;
			private set
			{
				if (_name != value)
				{
					_name = value;
					NotifyPropertyChanged(nameof(Name));
				}
			}
		}

		public bool IsSelected
		{
			get => _isSelected;
			set
			{
				if (_isSelected != value)
				{
					_isSelected = value;
					NotifyPropertyChanged(nameof(IsSelected));
				}
			}
		}

		public PaneTabItem(string path)
		{
			_path = path;
			_name = TabNameFromPath(path);
		}

		private static string TabNameFromPath(string path)
		{
			if (string.IsNullOrEmpty(path) || path == "Home")
				return Strings.Home.GetLocalizedResource();

			try
			{
				var name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
				return string.IsNullOrEmpty(name) ? path : name;
			}
			catch
			{
				return path;
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
