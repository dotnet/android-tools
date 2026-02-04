using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Xamarin.Android.Tools {

	class MicrosoftOpenJdkLocations : JdkLocations {

		internal static IEnumerable<JdkInfo> GetMicrosoftOpenJdks (Action<TraceLevel, string> logger)
		{
			return GetMacOSSystemJdks ("microsoft-*.jdk", logger)
				.Concat (GetMacOSUserJdks (logger))
				.Concat (GetWindowsFileSystemJdks (Path.Combine ("Android", "openjdk", "jdk-*"), logger))
				.Concat (GetWindowsFileSystemJdks (Path.Combine ("Microsoft", "jdk-*"), logger))
				.Concat (GetWindowsUserFileSystemJdks ("jdk-*", logger))
				.Concat (GetWindowsRegistryJdks (logger, @"SOFTWARE\Microsoft\JDK", "*", @"hotspot\MSI", "Path"))
				.Concat (GetLinuxUserJdks ("jdk-*", logger))
				.OrderByDescending (jdk => jdk, JdkInfoVersionComparer.Default);
		}
	}
}
