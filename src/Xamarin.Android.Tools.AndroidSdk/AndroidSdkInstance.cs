// 
// AndroidSdk.cs
//  
// Authors:
//       Jonathan Pobst <jpobst@xamarin.com>
//       Andreia Gaita <andreia@xamarin.com>
//       Michael Hutchinson <mhutch@xamarin.com>
// 
// Copyright 2012 Xamarin Inc. All rights reserved.
// 

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Xamarin.Android.Tools
{
	public class AndroidSdkInstance
	{
		public static AndroidSdkInfo Sdk { get; private set; }

		public static JdkInfo Jdk { get; private set; }

		public static AndroidVersions SupportedVersions;

		public const string AutoRefreshSwitch = "Xamarin.AndroidTools.AndroidSdk.AutoRefresh";

		static AndroidSdkInstance ()
		{
			// Return early if AutoRefreshSwitch is false
			if (AppContext.TryGetSwitch (AutoRefreshSwitch, out var enabled) && !enabled) {
				return;
			}

			// Run Refresh if AutoRefreshSwitch is not set or true
			Refresh ();
		}

		public static void Refresh ()
		{
			Refresh (null, null, null);
		}

		public static void Refresh (string androidSdkPath = null, string androidNdkPath = null, string javaSdkPath = null)
		{
			try {
				Sdk = new AndroidSdkInfo (Logger, androidSdkPath, androidNdkPath, javaSdkPath);
				Jdk = new JdkInfo (javaSdkPath);
			}
			catch (Exception ex) {
				Sdk = null;
				Jdk = null;

				if (ex is InvalidOperationException && ex.Message.Contains (" Android "))
					AndroidLogger.LogError (AndroidSdk.Properties.Resources.XA5300_Android_SDK);
				else if (ex is InvalidOperationException && ex.Message.Contains (" Java "))
					AndroidLogger.LogError (AndroidSdk.Properties.Resources.XA5300_Java_SDK);
				else
					AndroidLogger.LogError (AndroidSdk.	Properties.Resources.XA5300_AndroidSdk_Refresh_Exception, ex.ToString ());
			}
		}

		public static void Refresh (string androidSdkPath = null, string androidNdkPath = null, string javaSdkPath = null, string[] referenceAssemblyPaths = null)
		{
			Refresh (androidSdkPath, androidNdkPath, javaSdkPath);
			RefreshSupportedVersions (referenceAssemblyPaths);
		}

		public static void RefreshSupportedVersions (string[] referenceAssemblyPaths)
		{
			SupportedVersions = new AndroidVersions (referenceAssemblyPaths);
		}

		static void Logger (TraceLevel level, string value)
		{
			switch (level) {
			case TraceLevel.Error:
				AndroidLogger.LogError (null, "{0}", value);
				break;
			case TraceLevel.Info:
				AndroidLogger.LogInfo (null, "{0}", value);
				break;
			case TraceLevel.Warning:
				AndroidLogger.LogWarning (null, "{0}", value);
				break;
			case TraceLevel.Verbose:
			default:
				AndroidLogger.LogDebug (null, "{0}", value);
				break;
			}
		}

		public static string GetRevisionFromSdkPackageDirectory (string sdkPackageDirectory)
		{
			if (!Directory.Exists (sdkPackageDirectory))
				return null;

			return SdkBuildProperties.LoadProperties (Path.Combine (sdkPackageDirectory, "source.properties")).GetPropertyValue ("Pkg.Revision=");
		}
		
		public static void SetPreferredAndroidSdkPath (string path)
		{
			AndroidSdkInfo.SetPreferredAndroidSdkPath (path);
			
			// Update everything to use new path
			Refresh ();
		}

		public static void SetPreferredJavaSdkPath (string path)
		{
			AndroidSdkInfo.SetPreferredJavaSdkPath (path);

			// Update everything to use new path
			Refresh ();
		}

		public static void SetPreferredAndroidNdkPath (string path)
		{
			AndroidSdkInfo.SetPreferredAndroidNdkPath (path);

			// Update everything to use new path
			Refresh ();
		}
	}
}
