// Copyright (c) Files Community
// Licensed under the MIT License.

using Windows.ApplicationModel;

namespace Files.App.Helpers
{
	/// <summary>
	/// Provides access to package information that works in both packaged and unpackaged environments.
	/// </summary>
	public static class PackageCompat
	{
		/// <summary>
		/// Gets the package that provides the current app, or <see langword="null"/> when the process has no package identity (unpackaged).
		/// </summary>
		public static Package? CurrentPackage { get; } = TryGetPackage();

		/// <summary>
		/// Gets a value that indicates whether the process has package identity.
		/// </summary>
		public static bool HasIdentity => CurrentPackage is not null;

		/// <summary>
		/// Gets the path of the installed package or, when unpackaged, the directory containing the app executable.
		/// </summary>
		public static string InstalledPath => CurrentPackage?.InstalledLocation.Path ?? AppContext.BaseDirectory;

		/// <summary>
		/// Gets the package name or, when unpackaged, a stable fallback name.
		/// </summary>
		public static string Name => CurrentPackage?.Id.Name ?? "FilesDev";

		/// <summary>
		/// Gets the display name of the package or, when unpackaged, a fallback name.
		/// </summary>
		public static string DisplayName => CurrentPackage?.DisplayName ?? "Files";

		/// <summary>
		/// Gets the package family name or, when unpackaged, a fallback name.
		/// </summary>
		public static string FamilyName => CurrentPackage?.Id.FamilyName ?? "FilesDev";

		/// <summary>
		/// Gets the package version or, when unpackaged, the version of the app assembly.
		/// </summary>
		public static PackageVersion Version
		{
			get
			{
				if (CurrentPackage is not null)
					return CurrentPackage.Id.Version;

				var version = typeof(PackageCompat).Assembly.GetName().Version ?? new Version(4, 2, 37, 0);
				return new PackageVersion
				{
					Major = (ushort)version.Major,
					Minor = (ushort)version.Minor,
					Build = (ushort)version.Build,
					Revision = (ushort)version.Revision
				};
			}
		}

		private static Package? TryGetPackage()
		{
			try
			{
				return Package.Current;
			}
			catch
			{
				return null;
			}
		}
	}
}
