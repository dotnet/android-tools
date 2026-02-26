using System;
using System.Diagnostics;
using System.Text.Json;

namespace Xamarin.Android.Tools
{
	/// <summary>
	/// Parser for Xamarin Android Manifest Feed JSON format.
	/// </summary>
	internal static class XamarinManifestParser
	{
		public static void Parse (string jsonContent, ManifestFeed manifest, Action<TraceLevel, string> logger)
		{
			try {
				using (var doc = JsonDocument.Parse (jsonContent)) {
					var root = doc.RootElement;

					// The exact format of Xamarin Android Manifest Feed is not publicly documented
					// This is a placeholder implementation that should be updated when the format is known
					
					// Try to parse common JSON structures
					if (root.TryGetProperty ("packages", out var packages) && packages.ValueKind == JsonValueKind.Array) {
						foreach (var packageElement in packages.EnumerateArray ()) {
							try {
								var package = ParsePackage (packageElement, logger);
								if (package is not null) {
									manifest.Packages.Add (package);
								}
							} catch (Exception ex) {
								logger (TraceLevel.Warning, $"Failed to parse package: {ex.Message}");
							}
						}
					}

					logger (TraceLevel.Verbose, $"Parsed {manifest.Packages.Count} packages from Xamarin manifest feed");
				}
			} catch (Exception ex) {
				logger (TraceLevel.Error, $"Failed to parse Xamarin manifest feed JSON: {ex.Message}");
				throw;
			}
		}

		static PackageInfo? ParsePackage (JsonElement packageElement, Action<TraceLevel, string> logger)
		{
			// Extract package information based on expected JSON structure
			string? path = null;
			if (packageElement.TryGetProperty ("id", out var idProp)) {
				path = idProp.GetString ();
			} else if (packageElement.TryGetProperty ("path", out var pathProp)) {
				path = pathProp.GetString ();
			}

			if (string.IsNullOrEmpty (path)) {
				return null;
			}

			var package = new PackageInfo {
				Path = path
			};

			if (packageElement.TryGetProperty ("displayName", out var displayName)) {
				package.DisplayName = displayName.GetString ();
			} else if (packageElement.TryGetProperty ("name", out var name)) {
				package.DisplayName = name.GetString ();
			}

			if (packageElement.TryGetProperty ("version", out var version)) {
				var versionStr = version.GetString ();
				if (!string.IsNullOrEmpty (versionStr) && Version.TryParse (versionStr, out var parsedVersion)) {
					package.Version = parsedVersion;
				}
			}

			if (packageElement.TryGetProperty ("license", out var license)) {
				package.License = license.GetString ();
			} else if (packageElement.TryGetProperty ("licenseId", out var licenseId)) {
				package.License = licenseId.GetString ();
			}

			if (packageElement.TryGetProperty ("description", out var description)) {
				package.Description = description.GetString ();
			}

			// Parse package type
			if (packageElement.TryGetProperty ("packageType", out var packageType)) {
				package.PackageType = packageType.GetString ();
			} else if (packageElement.TryGetProperty ("type", out var type)) {
				package.PackageType = type.GetString ();
			}

			// Parse obsolete flag
			if (packageElement.TryGetProperty ("obsolete", out var obsolete)) {
				if (obsolete.ValueKind == JsonValueKind.True) {
					package.Obsolete = true;
				} else if (obsolete.ValueKind == JsonValueKind.False) {
					package.Obsolete = false;
				}
			}

			// Parse archives
			if (packageElement.TryGetProperty ("archives", out var archives) && archives.ValueKind == JsonValueKind.Array) {
				foreach (var archiveElement in archives.EnumerateArray ()) {
					var archive = ParseArchive (archiveElement);
					if (archive is not null) {
						package.Archives.Add (archive);
					}
				}
			} else if (packageElement.TryGetProperty ("downloads", out var downloads) && downloads.ValueKind == JsonValueKind.Array) {
				foreach (var downloadElement in downloads.EnumerateArray ()) {
					var archive = ParseArchive (downloadElement);
					if (archive is not null) {
						package.Archives.Add (archive);
					}
				}
			}

			return package;
		}

		static ArchiveInfo? ParseArchive (JsonElement archiveElement)
		{
			string? url = null;
			if (archiveElement.TryGetProperty ("url", out var urlProp)) {
				url = urlProp.GetString ();
			}

			string? checksum = null;
			string checksumType = "sha1";

			if (archiveElement.TryGetProperty ("sha1", out var sha1Prop)) {
				checksum = sha1Prop.GetString ();
				checksumType = "sha1";
			} else if (archiveElement.TryGetProperty ("sha256", out var sha256Prop)) {
				checksum = sha256Prop.GetString ();
				checksumType = "sha256";
			} else if (archiveElement.TryGetProperty ("checksum", out var checksumProp)) {
				checksum = checksumProp.GetString ();
				// Check for checksumType property
				if (archiveElement.TryGetProperty ("checksumType", out var checksumTypeProp)) {
					checksumType = checksumTypeProp.GetString () ?? "sha1";
				}
			}

			if (string.IsNullOrEmpty (url) || string.IsNullOrEmpty (checksum)) {
				return null;
			}

			var archive = new ArchiveInfo {
				Url = url,
				Checksum = checksum,
				ChecksumType = checksumType
			};

			if (archiveElement.TryGetProperty ("size", out var size)) {
				if (size.ValueKind == JsonValueKind.Number) {
					archive.Size = size.GetInt64 ();
				}
			}

			if (archiveElement.TryGetProperty ("os", out var os)) {
				archive.HostOs = os.GetString ();
			} else if (archiveElement.TryGetProperty ("hostOs", out var hostOs)) {
				archive.HostOs = hostOs.GetString ();
			} else if (archiveElement.TryGetProperty ("platform", out var platform)) {
				archive.HostOs = platform.GetString ();
			}

			if (archiveElement.TryGetProperty ("arch", out var arch)) {
				archive.Arch = arch.GetString ();
			} else if (archiveElement.TryGetProperty ("architecture", out var architecture)) {
				archive.Arch = architecture.GetString ();
			} else if (archiveElement.TryGetProperty ("hostArch", out var hostArch)) {
				archive.Arch = hostArch.GetString ();
			}

			if (archiveElement.TryGetProperty ("hostBits", out var hostBits)) {
				if (hostBits.ValueKind == JsonValueKind.Number) {
					archive.HostBits = hostBits.GetUInt32 ();
				}
			} else if (archiveElement.TryGetProperty ("bits", out var bits)) {
				if (bits.ValueKind == JsonValueKind.Number) {
					archive.HostBits = bits.GetUInt32 ();
				}
			}

			return archive;
		}
	}
}
