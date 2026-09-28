// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

namespace Files.App.Actions
{
	[GeneratedRichCommand]
	internal sealed partial class ArrangePanesGridAction : ObservableObject, IToggleAction
	{
		private readonly IContentPageContext ContentPageContext = Ioc.Default.GetRequiredService<IContentPageContext>();
		private readonly IMultiPanesContext MultiPanesContext = Ioc.Default.GetRequiredService<IMultiPanesContext>();

		public string Label
			=> Strings.ArrangePanesGrid.GetLocalizedResource();

		public string Description
			=> Strings.ArrangePanesGridDescription.GetLocalizedResource();

		public ActionCategory Category
			=> ActionCategory.DualPane;

		public RichGlyph Glyph
			=> new(themedIconStyle: "App.ThemedIcons.Panes.Grid");

		public bool IsOn
			=> MultiPanesContext.ShellPaneArrangement is ShellPaneArrangement.Grid;

		public bool IsExecutable
			=> ContentPageContext.IsMultiPaneAvailable;

		public ArrangePanesGridAction()
		{
			ContentPageContext.PropertyChanged += ContentPageContext_PropertyChanged;
			MultiPanesContext.ShellPaneArrangementChanged += MultiPanesContext_ShellPaneArrangementChanged;
		}

		public Task ExecuteAsync(object? parameter = null)
		{
			var paneHolder = ContentPageContext.ShellPage.GetRequiredPaneHolder();
			paneHolder.ArrangePanes(ShellPaneArrangement.Grid);

			return Task.CompletedTask;
		}

		private void ContentPageContext_PropertyChanged(object? sender, PropertyChangedEventArgs e)
		{
			switch (e.PropertyName)
			{
				case nameof(IContentPageContext.IsMultiPaneAvailable):
					OnPropertyChanged(nameof(IsExecutable));
					break;
			}
		}

		private void MultiPanesContext_ShellPaneArrangementChanged(object? sender, EventArgs e)
		{
			OnPropertyChanged(nameof(IsOn));
		}
	}
}
