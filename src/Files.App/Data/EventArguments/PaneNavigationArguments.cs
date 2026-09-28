// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.EventArguments
{
	internal sealed class PaneNavigationArguments
	{
		public string? LeftPaneNavPathParam { get; set; }

		public string? LeftPaneSelectItemParam { get; set; }

		public string? RightPaneNavPathParam { get; set; }

		public string? RightPaneSelectItemParam { get; set; }

		/// <summary>
		/// Navigation path of the third pane, used by the 2x2 grid (quad) layout.
		/// </summary>
		public string? ThirdPaneNavPathParam { get; set; }

		/// <summary>
		/// Navigation path of the fourth pane, used by the 2x2 grid (quad) layout.
		/// </summary>
		public string? FourthPaneNavPathParam { get; set; }

		public ShellPaneArrangement ShellPaneArrangement { get; set; }

		/// <summary>
		/// Paths of the folder tabs inside pane 0, persisted for QDir-style pane tabs; null when pane tabs are not used.
		/// </summary>
		public string[]? PaneTabPaths { get; set; }

		/// <summary>
		/// Paths of the folder tabs inside pane 1, persisted for QDir-style pane tabs; null when pane tabs are not used.
		/// </summary>
		public string[]? SecondPaneTabPaths { get; set; }

		/// <summary>
		/// Paths of the folder tabs inside pane 2, persisted for QDir-style pane tabs; null when pane tabs are not used.
		/// </summary>
		public string[]? ThirdPaneTabPaths { get; set; }

		/// <summary>
		/// Paths of the folder tabs inside pane 3, persisted for QDir-style pane tabs; null when pane tabs are not used.
		/// </summary>
		public string[]? FourthPaneTabPaths { get; set; }

		public static bool operator ==(PaneNavigationArguments? a1, PaneNavigationArguments? a2)
		{
			if (a1 is null && a2 is null)
				return true;

			if (a1 is null || a2 is null)
				return false;

			return a1.LeftPaneNavPathParam == a2.LeftPaneNavPathParam &&
				a1.LeftPaneSelectItemParam == a2.LeftPaneSelectItemParam &&
				a1.RightPaneNavPathParam == a2.RightPaneNavPathParam &&
				a1.RightPaneSelectItemParam == a2.RightPaneSelectItemParam &&
				a1.ThirdPaneNavPathParam == a2.ThirdPaneNavPathParam &&
				a1.FourthPaneNavPathParam == a2.FourthPaneNavPathParam &&
				a1.ShellPaneArrangement == a2.ShellPaneArrangement;
		}

		public static bool operator !=(PaneNavigationArguments? a1, PaneNavigationArguments? a2)
		{
			return !(a1 == a2);
		}

		public override bool Equals(object? obj)
		{
			return obj is PaneNavigationArguments args && this == args;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(LeftPaneNavPathParam, LeftPaneSelectItemParam, RightPaneNavPathParam, RightPaneSelectItemParam, ThirdPaneNavPathParam, FourthPaneNavPathParam, ShellPaneArrangement);
		}
	}
}
