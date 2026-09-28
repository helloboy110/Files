// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.Models
{
	/// <summary>
	/// A named multi-pane workspace layout: pane arrangement, per-pane paths, and color scheme.
	/// Persisted by <see cref="LayoutProfilesSettingsService"/> in layout_profiles.json.
	/// </summary>
	public sealed class NamedLayout
	{
		/// <summary>
		/// Unique name shown in the layout picker; doubles as the JSON key identity.
		/// </summary>
		public string Name { get; set; } = string.Empty;

		/// <summary>
		/// Pane arrangement (Horizontal / Vertical / Grid).
		/// </summary>
		public ShellPaneArrangement Arrangement { get; set; } = ShellPaneArrangement.Vertical;

		/// <summary>
		/// Paths of the panes in order: left/top pane, right/bottom pane, then grid panes 3 and 4.
		/// </summary>
		public List<string?> PanePaths { get; set; } = [];

		/// <summary>
		/// Hex background color applied when the layout is activated ("#AARRGGBB"); empty keeps the current color.
		/// </summary>
		public string? ColorSchemeHex { get; set; }

		/// <summary>
		/// Whether the layout also restores the theme background color on activation.
		/// </summary>
		public bool ApplyColorScheme { get; set; } = true;
	}
}
