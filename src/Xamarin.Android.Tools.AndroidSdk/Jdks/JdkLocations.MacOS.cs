using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Xamarin.Android.Tools {

	partial class JdkLocations {

		static IEnumerable<JdkInfo> GetUnixPreferredJdks (Action<TraceLevel, string> logger)
		{
			return GetUnixConfiguredJdkPaths (logger)
				.Select (p => JdkInfo.TryGetJdkInfo (p, logger, "monodroid-config.xml"))
				.Where (jdk => jdk != null)
				.Select (jdk => jdk!)
				.OrderByDescending (jdk => jdk, JdkInfoVersionComparer.Default);
		}

		static IEnumerable<string> GetUnixConfiguredJdkPaths (Action<TraceLevel, string> logger)
		{
			var config = AndroidSdkUnix.GetUnixConfigFile (logger);

			foreach (var java_sdk in config.Elements ("java-sdk")) {
				var path    = (string?) java_sdk.Attribute ("path");
				if (path != null && !string.IsNullOrEmpty (path)) {
					yield return path;
				}
			}
		}

		const   string  MacOSJavaVirtualMachinesRoot    = "/Library/Java/JavaVirtualMachines";

		protected static IEnumerable<JdkInfo> GetMacOSUserJdks (string pattern, Action<TraceLevel, string> logger, string? locator = null)
		{
			if (!OS.IsMac) {
				return Array.Empty<JdkInfo> ();
			}

			var root = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.UserProfile), "Android", "jdk");
			if (!Directory.Exists (root)) {
				return Array.Empty<JdkInfo> ();
			}

			return FromPaths (GetMacOSUserJdkPaths (root, pattern), logger, locator ?? $"~/Android/jdk/{pattern}");
		}

		static IEnumerable<string> GetMacOSUserJdkPaths (string root, string pattern)
		{
			IEnumerable<string> jdks;
			try {
				jdks = Directory.EnumerateDirectories (root, pattern);
			}
			catch (IOException) {
				yield break;
			}

			foreach (var jdk in jdks) {
				var release = Path.Combine (jdk, "release");
				if (File.Exists (release))
					yield return jdk;
			}
		}

		protected static IEnumerable<JdkInfo> GetMacOSUserLibraryJdks (string pattern, Action<TraceLevel, string> logger, string? locator = null)
		{
			if (!OS.IsMac) {
				return Array.Empty<JdkInfo> ();
			}

			var root = Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.UserProfile), "Library", "Java", "JavaVirtualMachines");
			if (!Directory.Exists (root)) {
				return Array.Empty<JdkInfo> ();
			}

			locator = locator ?? $"~/Library/Java/JavaVirtualMachines/{pattern}";
			return FromPaths (GetMacOSUserLibraryJdkPaths (root, pattern), logger, locator);
		}

		static IEnumerable<string> GetMacOSUserLibraryJdkPaths (string root, string pattern)
		{
			var toHome = Path.Combine ("Contents", "Home");
			IEnumerable<string> jdks;
			try {
				jdks = Directory.EnumerateDirectories (root, pattern);
			}
			catch (IOException) {
				yield break;
			}

			foreach (var dir in jdks) {
				var home = Path.Combine (dir, toHome);
				if (!Directory.Exists (home))
					continue;
				yield return home;
			}
		}

		protected static IEnumerable<JdkInfo> GetMacOSSystemJdks (string pattern, Action<TraceLevel, string> logger, string? locator = null)
		{
			if (!OS.IsMac) {
				return Array.Empty<JdkInfo>();
			}
			locator = locator ?? Path.Combine (MacOSJavaVirtualMachinesRoot, pattern);
			return FromPaths (GetMacOSSystemJvmPaths (pattern), logger, locator);
		}

		static IEnumerable<string> GetMacOSSystemJvmPaths (string pattern)
		{
			var root    = MacOSJavaVirtualMachinesRoot;
			var toHome  = Path.Combine ("Contents", "Home");
			var jdks    = AppDomain.CurrentDomain.GetData ($"GetMacOSMicrosoftJdkPaths jdks override! {typeof (JdkInfo).AssemblyQualifiedName}")
				?.ToString ();
			if (jdks != null) {
				root    = jdks;
				toHome  = "";
				pattern = "*";
			}
			if (!Directory.Exists (root)) {
				yield break;
			}
			foreach (var dir in Directory.EnumerateDirectories (root, pattern)) {
				var home = Path.Combine (dir, toHome);
				if (!Directory.Exists (home))
					continue;
				yield return home;
			}
		}
	}
}
