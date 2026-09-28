// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.Contracts
{
	/// <summary>
	/// Provides access to named multi-pane workspace layouts.
	/// </summary>
	public interface ILayoutProfilesSettingsService
	{
		/// <summary>
		/// Raised whenever the layout list is modified.
		/// </summary>
		event EventHandler? OnLayoutsUpdated;

		/// <summary>
		/// Gets or sets all saved layouts.
		/// </summary>
		List<NamedLayout> Layouts { get; set; }

		/// <summary>
		/// Adds or updates a layout by name.
		/// </summary>
		void Save(NamedLayout layout);

		/// <summary>
		/// Deletes the layout with the given name, returning whether anything was removed.
		/// </summary>
		bool Delete(string name);

		/// <summary>
		/// Gets the layout with the given name, or null when absent.
		/// </summary>
		NamedLayout? GetByName(string name);
	}
}
