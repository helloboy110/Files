// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.Parameters
{
	public sealed class NavigationParams
	{
		public string? NavPath { get; set; }

		public string? SelectItem { get; set; }

		/// <summary>
		/// Paths of the folder tabs to restore inside the pane, from oldest to newest; null when pane tabs are not used.
		/// </summary>
		public string[]? PaneTabPaths { get; set; }
	}
}
