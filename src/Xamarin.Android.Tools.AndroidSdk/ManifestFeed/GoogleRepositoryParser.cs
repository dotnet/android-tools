using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Parser for Google SDK Repository XML format (repository2-3.xml).
	/// </summary>
	internal static class GoogleRepositoryParser
	{
		const string SdkNamespace = "http://schemas.android.com/sdk/android/repo/repository2/03";
		const string CommonNamespace = "http://schemas.android.com/repository/android/common/02";
		const string GenericNamespace = "http://schemas.android.com/repository/android/generic/02";

		public static void Parse (string xmlContent, ManifestFeed manifest, Action<TraceLevel, string> logger)
		{
			try {
				var doc = XDocument.Parse (xmlContent);
				
				if (doc.Root is null) {
					logger (TraceLevel.Warning, "Empty XML document");
					return;
				}

				// Get the namespace from the root element
				var ns = doc.Root.Name.Namespace;
				
				logger (TraceLevel.Verbose, $"Parsing Google repository with namespace: {ns}");

				// Parse all remotePackage elements - they may be in the default namespace (no prefix)
				// Try both with namespace and without
				var packages = doc.Root.Elements (ns + "remotePackage")
					.Concat (doc.Root.Elements ("remotePackage"));
				logger (TraceLevel.Verbose, $"Found {packages.Count ()} remotePackage elements");
				
				foreach (var packageElement in packages) {
					try {
						var package = ParsePackage (packageElement, ns, logger);
						if (package is not null) {
							manifest.Packages.Add (package);
						}
					} catch (Exception ex) {
						logger (TraceLevel.Warning, $"Failed to parse package: {ex.Message}");
					}
				}

				logger (TraceLevel.Verbose, $"Parsed {manifest.Packages.Count} packages from Google SDK repository");
			} catch (Exception ex) {
				logger (TraceLevel.Error, $"Failed to parse Google SDK repository XML: {ex.Message}");
				throw;
			}
		}

		static PackageInfo? ParsePackage (XElement packageElement, XNamespace ns, Action<TraceLevel, string> logger)
		{
			var path = packageElement.Attribute ("path")?.Value;
			if (string.IsNullOrEmpty (path)) {
				return null;
			}

			var package = new PackageInfo {
				Path = path,
				DisplayName = GetElementValue (packageElement, "display-name", ns),
				License = GetElementAttribute (packageElement, "uses-license", "ref", ns)
			};

			// Parse package type from path (e.g., "cmdline-tools;20.0" -> "cmdline-tools")
			var semiColonIndex = path.IndexOf (';');
			package.PackageType = semiColonIndex > 0 ? path.Substring (0, semiColonIndex) : path;

			// Check for obsolete flag
			var obsoleteElement = GetElement (packageElement, "obsolete", ns);
			package.Obsolete = obsoleteElement is not null;

			// Parse version
			var revisionElement = GetElement (packageElement, "revision", ns);
			if (revisionElement is not null) {
				package.Version = ParseVersion (revisionElement, ns);
			}

			// Parse archives
			var archivesElement = GetElement (packageElement, "archives", ns);
			if (archivesElement is not null) {
				foreach (var archiveElement in GetElements (archivesElement, "archive", ns)) {
					var archive = ParseArchive (archiveElement, ns, path, logger);
					if (archive is not null) {
						package.Archives.Add (archive);
					}
				}
			}

			return package;
		}

		static Version? ParseVersion (XElement revisionElement, XNamespace ns)
		{
			var major = ParseInt (GetElementValue (revisionElement, "major", ns));
			var minor = ParseInt (GetElementValue (revisionElement, "minor", ns));
			var build = ParseInt (GetElementValue (revisionElement, "micro", ns));
			var revision = ParseInt (GetElementValue (revisionElement, "preview", ns));

			if (major is null)
				return null;

			if (minor is null)
				return new Version (major.Value, 0);
			if (build is null)
				return new Version (major.Value, minor.Value);
			if (revision is null)
				return new Version (major.Value, minor.Value, build.Value);
			return new Version (major.Value, minor.Value, build.Value, revision.Value);
		}

		static ArchiveInfo? ParseArchive (XElement archiveElement, XNamespace ns, string packagePath, Action<TraceLevel, string> logger)
		{
			var completeElement = GetElement (archiveElement, "complete", ns);
			if (completeElement is null) {
				return null;
			}

			var url = GetElementValue (completeElement, "url", ns);
			var checksumElement = GetElement (completeElement, "checksum", ns);
			var checksum = checksumElement?.Value;
			var checksumType = checksumElement?.Attribute ("type")?.Value ?? "sha1";
			var size = ParseLong (GetElementValue (completeElement, "size", ns));

			if (string.IsNullOrEmpty (url) || string.IsNullOrEmpty (checksum)) {
				return null;
			}

			// Build full URL if it's relative
			if (!url.StartsWith ("http://", StringComparison.OrdinalIgnoreCase) &&
			    !url.StartsWith ("https://", StringComparison.OrdinalIgnoreCase)) {
				url = $"https://dl.google.com/android/repository/{url}";
			}

			var hostOs = GetElementValue (archiveElement, "host-os", ns);
			var hostArch = GetElementValue (archiveElement, "host-arch", ns);
			var hostBitsStr = GetElementValue (archiveElement, "host-bits", ns);

			var archive = new ArchiveInfo {
				Url = url,
				Checksum = checksum,
				ChecksumType = checksumType,
				Size = size ?? 0,
				HostOs = hostOs,
				Arch = hostArch,
				HostBits = ParseUInt (hostBitsStr) ?? 0
			};

			return archive;
		}

		// Helper methods to handle elements with or without namespace
		static XElement? GetElement (XElement parent, string name, XNamespace ns)
		{
			return parent.Element (name) ?? parent.Element (ns + name);
		}

		static IEnumerable<XElement> GetElements (XElement parent, string name, XNamespace ns)
		{
			var elements = parent.Elements (name);
			if (!elements.Any ())
				elements = parent.Elements (ns + name);
			return elements;
		}

		static string? GetElementValue (XElement parent, string name, XNamespace ns)
		{
			return GetElement (parent, name, ns)?.Value;
		}

		static string? GetElementAttribute (XElement parent, string elementName, string attributeName, XNamespace ns)
		{
			return GetElement (parent, elementName, ns)?.Attribute (attributeName)?.Value;
		}

		static int? ParseInt (string? value)
		{
			if (string.IsNullOrEmpty (value))
				return null;
			if (int.TryParse (value, out var result))
				return result;
			return null;
		}

		static uint? ParseUInt (string? value)
		{
			if (string.IsNullOrEmpty (value))
				return null;
			if (uint.TryParse (value, out var result))
				return result;
			return null;
		}

		static long? ParseLong (string? value)
		{
			if (string.IsNullOrEmpty (value))
				return null;
			if (long.TryParse (value, out var result))
				return result;
			return null;
		}
	}
}
