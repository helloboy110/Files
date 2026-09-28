// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Data.EventArguments;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Files.App.Helpers
{
	/// <summary>
	/// Activates and captures named multi-pane workspace layouts.
	/// </summary>
	public static class LayoutProfileHelpers
	{
		private static ILayoutProfilesSettingsService LayoutProfilesSettingsService { get; } = Ioc.Default.GetRequiredService<ILayoutProfilesSettingsService>();
		private static IAppearanceSettingsService AppearanceSettingsService { get; } = Ioc.Default.GetRequiredService<IAppearanceSettingsService>();
		private static IResourcesService ResourcesService { get; } = Ioc.Default.GetRequiredService<IResourcesService>();

		/// <summary>
		/// Opens the layout in a new tab: restores the pane arrangement, per-pane paths, and optionally the color scheme.
		/// </summary>
		public static Task ActivateAsync(NamedLayout layout)
		{
			var paths = layout.PanePaths ?? [];

			var paneArgs = new PaneNavigationArguments
			{
				LeftPaneNavPathParam = string.IsNullOrEmpty(paths.Count > 0 ? paths[0] : null) ? "Home" : paths[0],
				RightPaneNavPathParam = layout.Arrangement is ShellPaneArrangement.None ? null : paths.Count > 1 ? paths[1] : null,
				ThirdPaneNavPathParam = layout.Arrangement is ShellPaneArrangement.Grid ? paths.Count > 2 ? paths[2] : null : null,
				FourthPaneNavPathParam = layout.Arrangement is ShellPaneArrangement.Grid ? paths.Count > 3 ? paths[3] : null : null,
				ShellPaneArrangement = layout.Arrangement is ShellPaneArrangement.None ? ShellPaneArrangement.Vertical : layout.Arrangement,
			};

			if (layout.ApplyColorScheme && !string.IsNullOrEmpty(layout.ColorSchemeHex))
				ApplyColorScheme(layout.ColorSchemeHex);

			return NavigationHelpers.AddNewTabByParamAsync(typeof(ShellPanesPage), paneArgs);
		}

		/// <summary>
		/// Captures the current tab's pane arrangement, pane paths, and theme color as a named layout.
		/// </summary>
		public static NamedLayout? Capture(string name)
		{
			var shellPanesPage = GetCurrentShellPanesPage();
			var paneArgs = shellPanesPage?.TabBarItemParameter?.NavigationParameter as PaneNavigationArguments;
			if (paneArgs is null)
				return null;

			return new NamedLayout
			{
				Name = name,
				Arrangement = paneArgs.ShellPaneArrangement,
				PanePaths =
				[
					paneArgs.LeftPaneNavPathParam,
					paneArgs.RightPaneNavPathParam,
					paneArgs.ThirdPaneNavPathParam,
					paneArgs.FourthPaneNavPathParam,
				],
				ColorSchemeHex = AppearanceSettingsService.AppThemeBackgroundColor,
				ApplyColorScheme = true,
			};
		}

		private static void ApplyColorScheme(string hex)
		{
			try
			{
				var color = (Windows.UI.Color)XamlBindingHelper.ConvertValue(typeof(Windows.UI.Color), hex);
				AppearanceSettingsService.AppThemeBackgroundColor = hex;
				ResourcesService.SetAppThemeBackgroundColor(color);
				ResourcesService.ApplyResources();
			}
			catch
			{
				// Ignore invalid colors saved in the layout file
			}
		}

		private static ITabBarItemContent? GetCurrentTabContent()
		{
			var index = App.AppModel.TabStripSelectedIndex;
			return index >= 0 && index < MainPageViewModel.AppInstances.Count
				? MainPageViewModel.AppInstances[index].TabItemContent
				: null;
		}

		private static ShellPanesPage? GetCurrentShellPanesPage()
		{
			var content = GetCurrentTabContent();
			return content as ShellPanesPage;
		}
	}
}
