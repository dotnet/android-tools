﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace Xamarin.Android.Tools
{
	public class JdkInfo {

		public      string                              HomePath                    {get;}

		public      string                              Locator                     {get;}

		public      string                              JarPath                     {get;}
		public      string                              JavaPath                    {get;}
		public      string                              JavacPath                   {get;}
		public      string                              JdkJvmPath                  {get;}
		public      ReadOnlyCollection<string>          IncludePath                 {get;}

		public      Version                             Version                     => javaVersion.Value;
		public      string                              Vendor                      {
			get {
				if (GetJavaSettingsPropertyValue ("java.vendor", out string vendor))
					return vendor;
				return null;
			}
		}

		public      ReadOnlyDictionary<string, string>  ReleaseProperties           {get;}
		public      IEnumerable<string>                 JavaSettingsPropertyKeys    => javaProperties.Value.Keys;

		Lazy<Dictionary<string, List<string>>>      javaProperties;
		Lazy<Version>                               javaVersion;

		public JdkInfo (string homePath)
		{
			if (homePath == null)
				throw new ArgumentNullException (nameof (homePath));
			if (!Directory.Exists (homePath))
				throw new ArgumentException ("Not a directory", nameof (homePath));

			HomePath            = homePath;

			var binPath         = Path.Combine (HomePath, "bin");
			JarPath             = ProcessUtils.FindExecutablesInDirectory (binPath, "jar").FirstOrDefault ();
			JavaPath            = ProcessUtils.FindExecutablesInDirectory (binPath, "java").FirstOrDefault ();
			JavacPath           = ProcessUtils.FindExecutablesInDirectory (binPath, "javac").FirstOrDefault ();
			JdkJvmPath = OS.IsMac
				? FindLibrariesInDirectory (Path.Combine (HomePath, "jre"), "jli").FirstOrDefault ()
				: FindLibrariesInDirectory (Path.Combine (HomePath, "jre"), "jvm").FirstOrDefault ();

			ValidateFile ("jar",    JarPath);
			ValidateFile ("java",   JavaPath);
			ValidateFile ("javac",  JavacPath);
			ValidateFile ("jvm",    JdkJvmPath);

			var includes        = new List<string> ();
			var jdkInclude      = Path.Combine (HomePath, "include");

			if (Directory.Exists (jdkInclude)) {
				includes.Add (jdkInclude);
				includes.AddRange (Directory.GetDirectories (jdkInclude));
			}


			ReleaseProperties   = GetReleaseProperties();

			IncludePath         = new ReadOnlyCollection<string> (includes);

			javaProperties      = new Lazy<Dictionary<string, List<string>>> (GetJavaProperties, LazyThreadSafetyMode.ExecutionAndPublication);
			javaVersion         = new Lazy<Version> (GetJavaVersion, LazyThreadSafetyMode.ExecutionAndPublication);
		}

		public JdkInfo (string homePath, string locator)
			: this (homePath)
		{
			Locator             = locator;
		}

		public override string ToString()
		{
			return $"JdkInfo(Version={Version}, Vendor=\"{Vendor}\", HomePath=\"{HomePath}\", Locator=\"{Locator}\")";
		}

		public bool GetJavaSettingsPropertyValues (string key, out IEnumerable<string> value)
		{
			value       = null;
			var props   = javaProperties.Value;
			if (props.TryGetValue (key, out var v)) {
				value = v;
				return true;
			}
			return false;
		}

		public bool GetJavaSettingsPropertyValue (string key, out string value)
		{
			value       = null;
			var props   = javaProperties.Value;
			if (props.TryGetValue (key, out var v)) {
				if (v.Count > 1)
					throw new InvalidOperationException ($"Requested to get one string value when property `{key}` contains `{v.Count}` values.");
				value   = v [0];
				return true;
			}
			return false;
		}

		static IEnumerable<string> FindLibrariesInDirectory (string dir, string libraryName)
		{
			if (!Directory.Exists (dir))
				return Enumerable.Empty<string> ();
			var library = string.Format (OS.NativeLibraryFormat, libraryName);
			return Directory.EnumerateFiles (dir, library, SearchOption.AllDirectories);
		}

		void ValidateFile (string name, string path)
		{
			if (path == null || !File.Exists (path))
				throw new ArgumentException ($"Could not find required file `{name}` within `{HomePath}`; is this a valid JDK?", "homePath");
		}

		static  Regex   NonDigitMatcher     = new Regex (@"[^\d]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		Version GetJavaVersion ()
		{
			string version = null;
			if (ReleaseProperties.TryGetValue ("JAVA_VERSION", out version) && !string.IsNullOrEmpty (version)) {
				version = GetParsableVersion (version);
				if (ReleaseProperties.TryGetValue ("BUILD_NUMBER", out var build) && !string.IsNullOrEmpty (build))
					version += "." + build;
			}
			else if (GetJavaSettingsPropertyValue ("java.version", out version) && !string.IsNullOrEmpty (version)) {
				version = GetParsableVersion (version);
			}
			if (string.IsNullOrEmpty (version))
				throw new NotSupportedException ("Could not determine Java version.");
			var normalizedVersion   = NonDigitMatcher.Replace (version, ".");
			var versionParts        = normalizedVersion.Split (new[]{"."}, StringSplitOptions.RemoveEmptyEntries);

			try {
				if (versionParts.Length < 2)
					return null;
				if (versionParts.Length == 2)
					return new Version (major: int.Parse (versionParts [0]), minor: int.Parse (versionParts [1]));
				if (versionParts.Length == 3)
					return new Version (major: int.Parse (versionParts [0]), minor: int.Parse (versionParts [1]), build: int.Parse (versionParts [2]));
				// We just ignore elements 4+
				return new Version (major: int.Parse (versionParts [0]), minor: int.Parse (versionParts [1]), build: int.Parse (versionParts [2]), revision: int.Parse (versionParts [3]));
			}
			catch (Exception) {
				return null;
			}
		}

		static string GetParsableVersion (string version)
		{
			if (!version.Contains ("."))
				version += ".0";
			return version;
		}

		ReadOnlyDictionary<string, string> GetReleaseProperties ()
		{
			var props       = new Dictionary<string, string> ();
			var releasePath = Path.Combine (HomePath, "release");
			if (!File.Exists (releasePath))
				return new ReadOnlyDictionary<string, string>(props);

			using (var release = File.OpenText (releasePath)) {
				string line;
				while ((line = release.ReadLine ()) != null) {
					line            = line.Trim ();
					const string PropertyDelim  = "=";
					int delim = line.IndexOf (PropertyDelim, StringComparison.Ordinal);
					if (delim < 0) {
						props [line] = "";
						continue;
					}
					string  key     = line.Substring (0, delim).Trim ();
					string  value   = line.Substring (delim + PropertyDelim.Length).Trim ();
					if (value.StartsWith ("\"", StringComparison.Ordinal) && value.EndsWith ("\"", StringComparison.Ordinal)) {
						value       = value.Substring (1, value.Length - 2);
					}
					props [key] = value;
				}
			}
			return new ReadOnlyDictionary<string, string>(props);
		}

		Dictionary<string, List<string>> GetJavaProperties ()
		{
			return GetJavaProperties (ProcessUtils.FindExecutablesInDirectory (Path.Combine (HomePath, "bin"), "java").First ());
		}

		static Dictionary<string, List<string>> GetJavaProperties (string java)
		{
			var javaProps   = new ProcessStartInfo {
				FileName    = java,
				Arguments   = "-XshowSettings:properties -version",
			};

			var     props   = new Dictionary<string, List<string>> ();
			string  curKey  = null;
			ProcessUtils.Exec (javaProps, (o, e) => {
					const string ContinuedValuePrefix   = "        ";
					const string NewValuePrefix         = "    ";
					const string NameValueDelim         = " = ";
					if (string.IsNullOrEmpty (e.Data))
						return;
					if (e.Data.StartsWith (ContinuedValuePrefix, StringComparison.Ordinal)) {
						if (curKey == null)
							throw new InvalidOperationException ($"Unknown property key for value {e.Data}!");
						props [curKey].Add (e.Data.Substring (ContinuedValuePrefix.Length));
						return;
					}
					if (e.Data.StartsWith (NewValuePrefix, StringComparison.Ordinal)) {
						var delim = e.Data.IndexOf (NameValueDelim, StringComparison.Ordinal);
						if (delim <= 0)
							return;
						curKey      = e.Data.Substring (NewValuePrefix.Length, delim - NewValuePrefix.Length);
						var value   = e.Data.Substring (delim + NameValueDelim.Length);
						List<string> values;
						if (!props.TryGetValue (curKey, out values))
							props.Add (curKey, values = new List<string> ());
						values.Add (value);
					}
			});

			return props;
		}

		public static IEnumerable<JdkInfo> GetKnownSystemJdkInfos (Action<TraceLevel, string> logger = null)
		{
			logger  = logger ?? AndroidSdkInfo.DefaultConsoleLogger;

			return GetWindowsJdks (logger)
				.Concat (GetConfiguredJdks (logger))
				.Concat (GetMacOSMicrosoftJdks (logger))
				.Concat (GetJavaHomeEnvironmentJdks (logger))
				.Concat (GetPathEnvironmentJdks (logger))
				.Concat (GetLibexecJdks (logger))
				.Concat (GetJavaAlternativesJdks (logger))
				;
		}

		static IEnumerable<JdkInfo> GetConfiguredJdks (Action<TraceLevel, string> logger)
		{
			return GetConfiguredJdkPaths (logger)
				.Select (p => TryGetJdkInfo (p, logger, "monodroid-config.xml"))
				.Where (jdk => jdk != null)
				.OrderByDescending (jdk => jdk, JdkInfoVersionComparer.Default);
		}

		static IEnumerable<string> GetConfiguredJdkPaths (Action<TraceLevel, string> logger)
		{
			var config = AndroidSdkUnix.GetUnixConfigFile (logger);
			foreach (var java_sdk in config.Root.Elements ("java-sdk")) {
				var path    = (string) java_sdk.Attribute ("path");
				yield return path;
			}
		}

		internal static IEnumerable<JdkInfo> GetMacOSMicrosoftJdks (Action<TraceLevel, string> logger)
		{
			return GetMacOSMicrosoftJdkPaths ()
				.Select (p => TryGetJdkInfo (p, logger, "$HOME/Library/Developer/Xamarin/jdk"))
				.Where (jdk => jdk != null)
				.OrderByDescending (jdk => jdk, JdkInfoVersionComparer.Default);
		}

		static IEnumerable<string> GetMacOSMicrosoftJdkPaths ()
		{
			var jdks    = AppDomain.CurrentDomain.GetData ($"GetMacOSMicrosoftJdkPaths jdks override! {typeof (JdkInfo).AssemblyQualifiedName}")
				?.ToString ();
			if (jdks == null) {
				var home    = Environment.GetFolderPath (Environment.SpecialFolder.Personal);
				jdks        = Path.Combine (home, "Library", "Developer", "Xamarin", "jdk");
			}
			if (!Directory.Exists (jdks))
				return Enumerable.Empty <string> ();

			return Directory.EnumerateDirectories (jdks);
		}

		static JdkInfo TryGetJdkInfo (string path, Action<TraceLevel, string> logger, string locator)
		{
			JdkInfo jdk = null;
			try {
				jdk = new JdkInfo (path, locator);
			}
			catch (Exception e) {
				logger (TraceLevel.Warning, $"Not a valid JDK directory: `{path}`; via locator: {locator}");
				logger (TraceLevel.Verbose, e.ToString ());
			}
			return jdk;
		}

		static IEnumerable<JdkInfo> GetWindowsJdks (Action<TraceLevel, string> logger)
		{
			if (!OS.IsWindows)
				return Enumerable.Empty<JdkInfo> ();
			return AndroidSdkWindows.GetJdkInfos (logger);
		}

		static IEnumerable<JdkInfo> GetJavaHomeEnvironmentJdks (Action<TraceLevel, string> logger)
		{
			var java_home = Environment.GetEnvironmentVariable ("JAVA_HOME");
			if (string.IsNullOrEmpty (java_home))
				yield break;
			var jdk = TryGetJdkInfo (java_home, logger, "$JAVA_HOME");
			if (jdk != null)
				yield return jdk;
		}

		// macOS
		static IEnumerable<JdkInfo> GetLibexecJdks (Action<TraceLevel, string> logger)
		{
			return GetLibexecJdkPaths (logger)
				.Distinct ()
				.Select (p => TryGetJdkInfo (p, logger, "`/usr/libexec/java_home -X`"))
				.Where (jdk => jdk != null)
				.OrderByDescending (jdk => jdk, JdkInfoVersionComparer.Default);
		}

		static IEnumerable<string> GetLibexecJdkPaths (Action<TraceLevel, string> logger)
		{
			var java_home	= Path.GetFullPath ("/usr/libexec/java_home");
			if (!File.Exists (java_home)) {
				yield break;
			}
			var jhp = new ProcessStartInfo {
				FileName    = java_home,
				Arguments   = "-X",
			};
			var xml = new StringBuilder ();
			ProcessUtils.Exec (jhp, (o, e) => {
					if (string.IsNullOrEmpty (e.Data))
						return;
					xml.Append (e.Data);
			}, includeStderr: false);
			var plist   = XElement.Parse (xml.ToString ());
			foreach (var info in plist.Elements ("array").Elements ("dict")) {
				var JVMHomePath = (XNode) info.Elements ("key").FirstOrDefault (e => e.Value == "JVMHomePath");
				if (JVMHomePath == null)
					continue;
				while (JVMHomePath.NextNode.NodeType != XmlNodeType.Element)
					JVMHomePath = JVMHomePath.NextNode;
				var strElement  = (XElement) JVMHomePath.NextNode;
				var path        = strElement.Value;
				yield return path;
			}
		}

		// Linux; Ubuntu & Derivatives
		static IEnumerable<JdkInfo> GetJavaAlternativesJdks (Action<TraceLevel, string> logger)
		{
			return GetJavaAlternativesJdkPaths ()
				.Distinct ()
				.Select (p => TryGetJdkInfo (p, logger, "`/usr/sbin/update-java-alternatives -l`"))
				.Where (jdk => jdk != null);
		}

		static IEnumerable<string> GetJavaAlternativesJdkPaths ()
		{
			var alternatives  = Path.GetFullPath ("/usr/sbin/update-java-alternatives");
			if (!File.Exists (alternatives))
				return Enumerable.Empty<string> ();

			var psi     = new ProcessStartInfo {
				FileName    = alternatives,
				Arguments   = "-l",
			};
			var paths   = new List<string> ();
			ProcessUtils.Exec (psi, (o, e) => {
					if (string.IsNullOrWhiteSpace (e.Data))
						return;
					// Example line:
					//  java-1.8.0-openjdk-amd64       1081       /usr/lib/jvm/java-1.8.0-openjdk-amd64
					var columns = e.Data.Split (new[]{ ' ' }, StringSplitOptions.RemoveEmptyEntries);
					if (columns.Length <= 2)
						return;
					paths.Add (columns [2]);
			});
			return paths;
		}

		// Linux; Fedora
		static IEnumerable<JdkInfo> GetLibJvmJdks (Action<TraceLevel, string> logger)
		{
			return GetLibJvmJdkPaths ()
				.Distinct ()
				.Select (p => TryGetJdkInfo (p, logger, "`ls /usr/lib/jvm/*`"))
				.Where (jdk => jdk != null)
				.OrderByDescending (jdk => jdk, JdkInfoVersionComparer.Default);
		}

		static IEnumerable<string> GetLibJvmJdkPaths ()
		{
			var jvm = "/usr/lib/jvm";
			if (!Directory.Exists (jvm))
				yield break;

			foreach (var jdk in Directory.EnumerateDirectories (jvm)) {
				var release = Path.Combine (jdk, "release");
				if (File.Exists (release))
					yield return jdk;
			}
		}

		// Last-ditch fallback!
		static IEnumerable<JdkInfo> GetPathEnvironmentJdks (Action<TraceLevel, string> logger)
		{
			return GetPathEnvironmentJdkPaths ()
				.Select (p => TryGetJdkInfo (p, logger, "$PATH"))
				.Where (jdk => jdk != null);
		}

		static IEnumerable<string> GetPathEnvironmentJdkPaths ()
		{
			foreach (var java in ProcessUtils.FindExecutablesInPath ("java")) {
				var props   = GetJavaProperties (java);
				if (props.TryGetValue ("java.home", out var java_homes)) {
					var java_home   = java_homes [0];
					// `java -XshowSettings:properties -version 2>&1 | grep java.home` ends with `/jre` on macOS.
					// We need the parent dir so we can properly lookup the `include` directories
					if (java_home.EndsWith ("jre", StringComparison.OrdinalIgnoreCase)) {
						java_home = Path.GetDirectoryName (java_home);
					}
					yield return java_home;
				}
			}
		}
	}

	class JdkInfoVersionComparer : IComparer<JdkInfo>
	{
		public  static  readonly    IComparer<JdkInfo> Default = new JdkInfoVersionComparer ();

		public int Compare (JdkInfo x, JdkInfo y)
		{
			if (x.Version != null && y.Version != null)
				return x.Version.CompareTo (y.Version);
			return 0;
		}
	}
}
