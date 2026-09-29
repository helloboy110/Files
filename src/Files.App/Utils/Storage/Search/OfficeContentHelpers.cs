// Copyright (c) Files Community
// Licensed under the MIT License.

using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Files.App.Utils.Storage
{
	/// <summary>
	/// Searches inside modern Office documents for a query string. The docx/pptx/xlsx
	/// formats (and their macro-enabled variants) are zip packages of XML parts whose
	/// user-visible text lives in predictable entries, so only those entries are
	/// streamed and decoded instead of loading whole documents into memory.
	/// </summary>
	public static class OfficeContentHelpers
	{
		// Macro-enabled variants share the same package layout; the legacy binary formats (doc/ppt/xls) are not covered
		private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".docx", ".docm", ".pptx", ".pptm", ".xlsx", ".xlsm",
		};

		// Text buffers flush after this many characters so large parts such as Excel
		// shared strings never materialize fully in memory
		private const int FlushThreshold = 8 * 1024;

		// External resolution is disabled outright: document parts are untrusted input
		private static readonly XmlReaderSettings SafeXmlSettings = new()
		{
			DtdProcessing = DtdProcessing.Ignore,
			XmlResolver = null,
			IgnoreComments = true,
			IgnoreProcessingInstructions = true,
			IgnoreWhitespace = true,
			CheckCharacters = false,
			CloseInput = true,
		};

		public static bool IsOfficeExtension(string? extension)
			=> extension is not null && OfficeExtensions.Contains(extension);

		/// <summary>
		/// Returns whether the document contains the query text, comparing case-insensitively.
		/// Corrupt, encrypted or unreadable documents simply report no match instead of failing the scan.
		/// </summary>
		public static bool OfficeFileContainsText(string filePath, string queryText, CancellationToken token)
		{
			if (string.IsNullOrEmpty(queryText))
				return false;

			try
			{
				token.ThrowIfCancellationRequested();

				// Match the plain-text scanner's sharing so documents open in Office stay readable
				using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
				using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

				foreach (var entry in archive.Entries)
				{
					if (IsSearchablePart(entry.FullName) && ScanXmlPart(entry, queryText))
						return true;
				}

				return false;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or OperationCanceledException)
			{
				return false;
			}
		}

		// Word scans the document body plus headers, footers, notes and comments; PowerPoint scans
		// slides, speaker notes and comments; Excel scans shared strings and worksheet cells.
		// Template parts (styles, masters, layouts) are excluded because they only hold placeholder text.
		private static bool IsSearchablePart(string entryName)
		{
			if (!entryName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
				return false;

			if (entryName.StartsWith("word/", StringComparison.OrdinalIgnoreCase))
			{
				return entryName.StartsWith("word/document", StringComparison.OrdinalIgnoreCase) ||
					entryName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) ||
					entryName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase) ||
					entryName.StartsWith("word/footnotes", StringComparison.OrdinalIgnoreCase) ||
					entryName.StartsWith("word/endnotes", StringComparison.OrdinalIgnoreCase) ||
					entryName.StartsWith("word/comments", StringComparison.OrdinalIgnoreCase) ||
					entryName.StartsWith("word/glossary/", StringComparison.OrdinalIgnoreCase);
			}

			if (entryName.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase))
			{
				return entryName.StartsWith("ppt/slides/", StringComparison.OrdinalIgnoreCase) ||
					entryName.StartsWith("ppt/notesSlides/", StringComparison.OrdinalIgnoreCase) ||
					entryName.StartsWith("ppt/comments/", StringComparison.OrdinalIgnoreCase);
			}

			if (entryName.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
			{
				return entryName.StartsWith("xl/sharedStrings", StringComparison.OrdinalIgnoreCase) ||
					entryName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase);
			}

			return false;
		}

		/// <summary>
		/// Streams the XML part and searches the text it contains. Adjacent text runs are
		/// concatenated without separators because Word splits sentences across runs mid-word.
		/// </summary>
		private static bool ScanXmlPart(ZipArchiveEntry entry, string queryText)
		{
			try
			{
				using var partStream = entry.Open();
				using var reader = XmlReader.Create(partStream, SafeXmlSettings);

				// Retaining a tail of query length - 1 characters across flushes keeps
				// matches that span two flushes findable
				var overlap = queryText.Length - 1;
				var builder = new StringBuilder(FlushThreshold);

				while (reader.Read())
				{
					if (reader.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace))
						continue;

					builder.Append(reader.Value);

					if (builder.Length < FlushThreshold)
						continue;

					if (builder.ToString().Contains(queryText, StringComparison.OrdinalIgnoreCase))
						return true;

					builder.Remove(0, builder.Length - overlap);
				}

				return builder.ToString().Contains(queryText, StringComparison.OrdinalIgnoreCase);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or XmlException or OperationCanceledException)
			{
				// Malformed part: skip it rather than failing the whole document scan
				return false;
			}
		}
	}
}
