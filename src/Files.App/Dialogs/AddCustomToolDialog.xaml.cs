// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT;

namespace Files.App.Dialogs
{
	public sealed partial class AddCustomToolDialog : ContentDialog
	{
		private FrameworkElement RootAppElement
		{
			[DynamicWindowsRuntimeCast(typeof(FrameworkElement))]
			get => (FrameworkElement)MainWindow.Instance.Content;
		}

		public AddCustomToolDialogViewModel ViewModel
		{
			get => (AddCustomToolDialogViewModel)DataContext;
			set => DataContext = value;
		}

		public AddCustomToolDialog()
		{
			DataContext = new AddCustomToolDialogViewModel();
			InitializeComponent();
		}

		public new async Task<DialogResult> ShowAsync() => (DialogResult)await base.ShowAsync();

		private async void BrowseButton_Click(object sender, RoutedEventArgs e)
		{
			var picker = new FileOpenPicker()
			{
				SuggestedStartLocation = PickerLocationId.ComputerFolder,
			};
			picker.FileTypeFilter.Add(".exe");
			picker.FileTypeFilter.Add(".bat");
			picker.FileTypeFilter.Add(".cmd");
			picker.FileTypeFilter.Add(".ps1");
			picker.FileTypeFilter.Add(".vbs");
			picker.FileTypeFilter.Add(".scr");
			picker.FileTypeFilter.Add(".com");

			WinRT.Interop.InitializeWithWindow.Initialize(picker, MainWindow.Instance.WindowHandle);

			var file = await picker.PickSingleFileAsync();
			if (file is not null)
				ViewModel.SelectExecutable(file.Path);
		}
	}
}
