// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;

namespace Files.App.ViewModels.Dialogs
{
	public sealed partial class AddCustomToolDialogViewModel : ObservableObject
	{
		private string toolName = string.Empty;
		public string ToolName
		{
			get => toolName;
			set
			{
				if (SetProperty(ref toolName, value))
					OnPropertyChanged(nameof(IsToolValid));
			}
		}

		private string executablePath = string.Empty;
		public string ExecutablePath
		{
			get => executablePath;
			set
			{
				if (SetProperty(ref executablePath, value))
					OnPropertyChanged(nameof(IsToolValid));
			}
		}

		private string arguments = string.Empty;
		public string Arguments
		{
			get => arguments;
			set => SetProperty(ref arguments, value);
		}

		public bool IsToolValid
			=> !string.IsNullOrWhiteSpace(ToolName)
				&& !string.IsNullOrWhiteSpace(ExecutablePath)
				&& SystemIO.File.Exists(ExecutablePath);

		public void SelectExecutable(string path)
		{
			ExecutablePath = path;

			// Default the tool name to the executable file name when left blank
			if (string.IsNullOrWhiteSpace(ToolName))
				ToolName = Path.GetFileNameWithoutExtension(path);
		}
	}
}
