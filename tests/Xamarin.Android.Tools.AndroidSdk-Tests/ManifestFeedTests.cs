using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools.Tests
{
	[TestFixture]
	public class ManifestFeedTests
	{
		static void TestLogger (TraceLevel level, string message)
		{
			Console.WriteLine ($"[{level}] {message}");
		}

		[Test]
		public void PackageInfo_Properties ()
		{
			var package = new PackageInfo {
				Path = "cmdline-tools;20.0",
				DisplayName = "Android SDK Command-line Tools",
				Version = new Version (20, 0),
				License = "android-sdk-license",
				PackageType = "cmdline-tools",
				Obsolete = false
			};

			Assert.AreEqual ("cmdline-tools;20.0", package.Path);
			Assert.AreEqual ("Android SDK Command-line Tools", package.DisplayName);
			Assert.AreEqual (new Version (20, 0), package.Version);
			Assert.AreEqual ("android-sdk-license", package.License);
			Assert.AreEqual ("cmdline-tools", package.PackageType);
			Assert.IsFalse (package.Obsolete);
			Assert.IsNotNull (package.Archives);
		}

		[Test]
		public void ArchiveInfo_Properties ()
		{
			var archive = new ArchiveInfo {
				Url = "https://dl.google.com/android/repository/commandlinetools-linux-14742923_latest.zip",
				Checksum = "48833c34b761c10cb20bcd16582129395d121b27",
				ChecksumType = "sha1",
				Size = 172789259,
				HostOs = "linux",
				Arch = "x86_64",
				HostBits = 64
			};

			Assert.AreEqual ("https://dl.google.com/android/repository/commandlinetools-linux-14742923_latest.zip", archive.Url);
			Assert.AreEqual ("48833c34b761c10cb20bcd16582129395d121b27", archive.Checksum);
			Assert.AreEqual ("48833c34b761c10cb20bcd16582129395d121b27", archive.Sha1);
			Assert.AreEqual ("sha1", archive.ChecksumType);
			Assert.AreEqual (172789259, archive.Size);
			Assert.AreEqual ("linux", archive.HostOs);
			Assert.AreEqual ("x86_64", archive.Arch);
			Assert.AreEqual ((uint) 64, archive.HostBits);
		}

		[Test]
		public void ArchiveInfo_Sha1Property ()
		{
			var archive = new ArchiveInfo ();
			archive.Sha1 = "abc123";
			
			Assert.AreEqual ("abc123", archive.Sha1);
			Assert.AreEqual ("abc123", archive.Checksum);
			Assert.AreEqual ("sha1", archive.ChecksumType);
		}

		[Test]
		public void ArchiveInfo_IsValidForCurrentSystem ()
		{
			// Cross-platform archive (no HostOs specified) should be valid
			var crossPlatform = new ArchiveInfo { Url = "test.zip", Checksum = "abc" };
			Assert.IsTrue (crossPlatform.IsValidForCurrentSystem ());

			// Current OS archive should be valid
			string currentOs = OS.IsWindows ? "windows" : (OS.IsMac ? "macosx" : "linux");
			var sameOs = new ArchiveInfo { Url = "test.zip", Checksum = "abc", HostOs = currentOs };
			Assert.IsTrue (sameOs.IsValidForCurrentSystem ());

			// Different OS archive should not be valid
			string differentOs = OS.IsWindows ? "linux" : "windows";
			var diffOs = new ArchiveInfo { Url = "test.zip", Checksum = "abc", HostOs = differentOs };
			Assert.IsFalse (diffOs.IsValidForCurrentSystem ());
		}

		[Test]
		public void PackageInfo_GetBestArchiveForCurrentSystem ()
		{
			string currentOs = OS.IsWindows ? "windows" : (OS.IsMac ? "macosx" : "linux");
			
			var package = new PackageInfo {
				Path = "test-package",
				Archives = {
					new ArchiveInfo { Url = "linux.zip", Checksum = "abc", HostOs = "linux" },
					new ArchiveInfo { Url = "windows.zip", Checksum = "def", HostOs = "windows" },
					new ArchiveInfo { Url = "macosx.zip", Checksum = "ghi", HostOs = "macosx" }
				}
			};

			var best = package.GetBestArchiveForCurrentSystem ();
			Assert.IsNotNull (best);
			Assert.AreEqual (currentOs, best.HostOs);
		}

		[Test]
		public void JdkPackageInfo_GetMajorVersion ()
		{
			var jdk = new JdkPackageInfo {
				Path = "jdk-17",
				Version = new Version (17, 0, 1)
			};

			Assert.AreEqual (17, jdk.GetMajorVersion ());
		}

		[Test]
		public void JdkPackageInfo_ParseVendorFromPath ()
		{
			var microsoft = new JdkPackageInfo { Path = "microsoft-jdk-17" };
			microsoft.ParseVendorFromPath ();
			Assert.AreEqual ("microsoft", microsoft.VendorId);
			Assert.AreEqual ("Microsoft", microsoft.VendorDisplay);

			var adoptium = new JdkPackageInfo { Path = "temurin-17" };
			adoptium.ParseVendorFromPath ();
			Assert.AreEqual ("adoptium", adoptium.VendorId);
			Assert.AreEqual ("Eclipse Adoptium", adoptium.VendorDisplay);

			var azul = new JdkPackageInfo { Path = "zulu-jdk-21" };
			azul.ParseVendorFromPath ();
			Assert.AreEqual ("azul", azul.VendorId);
			Assert.AreEqual ("Azul", azul.VendorDisplay);

			var preview = new JdkPackageInfo { Path = "microsoft-jdk-22-preview" };
			preview.ParseVendorFromPath ();
			Assert.IsTrue (preview.Preview);
		}

		[Test]
		public void JdkPackageInfo_FromPackageInfo ()
		{
			var package = new PackageInfo {
				Path = "microsoft-jdk-17",
				DisplayName = "Microsoft JDK 17",
				Version = new Version (17, 0, 1),
				PackageType = "jdk"
			};

			var jdk = JdkPackageInfo.FromPackageInfo (package);
			Assert.AreEqual (package.Path, jdk.Path);
			Assert.AreEqual (package.DisplayName, jdk.DisplayName);
			Assert.AreEqual (package.Version, jdk.Version);
			Assert.AreEqual (package.PackageType, jdk.PackageType);
		}

		[Test]
		public void ManifestFeed_Constructor ()
		{
			var manifest = new ManifestFeed (TestLogger);
			Assert.IsNotNull (manifest.Packages);
			Assert.AreEqual (0, manifest.Packages.Count);
			Assert.IsNull (manifest.FeedUrl);
		}

		[Test]
		[Ignore ("Requires network access")]
		public async Task LoadAsync_GoogleRepository ()
		{
			// This test requires network access
			var manifest = await ManifestFeed.LoadAsync (
				"https://dl.google.com/android/repository/repository2-3.xml",
				TestLogger
			);

			Assert.IsNotNull (manifest);
			Assert.Greater (manifest.Packages.Count, 0);
			Assert.AreEqual ("https://dl.google.com/android/repository/repository2-3.xml", manifest.FeedUrl);

			// Check for cmdline-tools package
			var cmdlineTools = manifest.Packages.FirstOrDefault (p => p.Path.StartsWith ("cmdline-tools", StringComparison.Ordinal));
			Assert.IsNotNull (cmdlineTools, "cmdline-tools package should exist");
			Assert.Greater (cmdlineTools.Archives.Count, 0, "cmdline-tools should have archives");
		}

		[Test]
		public void LoadFromFile_GoogleRepository ()
		{
			// Create a minimal Google SDK repository XML
			var xml = @"<?xml version='1.0' encoding='utf-8'?>
<sdk:sdk-repository xmlns:sdk=""http://schemas.android.com/sdk/android/repo/repository2/03"">
  <remotePackage path=""cmdline-tools;20.0"">
    <revision>
      <major>20</major>
      <minor>0</minor>
    </revision>
    <display-name>Android SDK Command-line Tools</display-name>
    <uses-license ref=""android-sdk-license""/>
    <archives>
      <archive>
        <complete>
          <size>172789259</size>
          <checksum type=""sha1"">48833c34b761c10cb20bcd16582129395d121b27</checksum>
          <url>commandlinetools-linux-14742923_latest.zip</url>
        </complete>
        <host-os>linux</host-os>
      </archive>
    </archives>
  </remotePackage>
</sdk:sdk-repository>";

			var tempFile = Path.Combine (Path.GetTempPath (), $"test-manifest-{Guid.NewGuid ()}.xml");
			try {
				File.WriteAllText (tempFile, xml);

				var manifest = ManifestFeed.LoadFromFile (tempFile, TestLogger);

				Assert.IsNotNull (manifest);
				Assert.AreEqual (1, manifest.Packages.Count);

				var package = manifest.Packages [0];
				Assert.AreEqual ("cmdline-tools;20.0", package.Path);
				Assert.AreEqual ("Android SDK Command-line Tools", package.DisplayName);
				Assert.AreEqual (new Version (20, 0), package.Version);
				Assert.AreEqual ("android-sdk-license", package.License);
				Assert.AreEqual ("cmdline-tools", package.PackageType);
				Assert.AreEqual (1, package.Archives.Count);

				var archive = package.Archives [0];
				Assert.AreEqual ("https://dl.google.com/android/repository/commandlinetools-linux-14742923_latest.zip", archive.Url);
				Assert.AreEqual ("48833c34b761c10cb20bcd16582129395d121b27", archive.Checksum);
				Assert.AreEqual ("sha1", archive.ChecksumType);
				Assert.AreEqual (172789259, archive.Size);
				Assert.AreEqual ("linux", archive.HostOs);
			} finally {
				if (File.Exists (tempFile))
					File.Delete (tempFile);
			}
		}

		[Test]
		public void ResolvePackage_ByPath ()
		{
			var manifest = new ManifestFeed (TestLogger);
			manifest.Packages.Add (new PackageInfo {
				Path = "cmdline-tools;20.0",
				DisplayName = "Android SDK Command-line Tools",
				Archives = {
					new ArchiveInfo { Url = "https://example.com/linux.zip", Checksum = "abc123", ChecksumType = "sha1", HostOs = "linux" },
					new ArchiveInfo { Url = "https://example.com/windows.zip", Checksum = "def456", ChecksumType = "sha1", HostOs = "windows" }
				}
			});

			var package = manifest.ResolvePackage ("cmdline-tools;20.0");
			Assert.IsNotNull (package);
			Assert.AreEqual ("cmdline-tools;20.0", package.Path);
			Assert.AreEqual (2, package.Archives.Count);
		}

		[Test]
		public void ResolvePackage_FilterByOs ()
		{
			var manifest = new ManifestFeed (TestLogger);
			manifest.Packages.Add (new PackageInfo {
				Path = "cmdline-tools;20.0",
				DisplayName = "Android SDK Command-line Tools",
				Archives = {
					new ArchiveInfo { Url = "https://example.com/linux.zip", Checksum = "abc123", ChecksumType = "sha1", HostOs = "linux" },
					new ArchiveInfo { Url = "https://example.com/windows.zip", Checksum = "def456", ChecksumType = "sha1", HostOs = "windows" },
					new ArchiveInfo { Url = "https://example.com/macosx.zip", Checksum = "ghi789", ChecksumType = "sha1", HostOs = "macosx" }
				}
			});

			var linuxPackage = manifest.ResolvePackage ("cmdline-tools;20.0", "linux");
			Assert.IsNotNull (linuxPackage);
			Assert.AreEqual (1, linuxPackage.Archives.Count);
			Assert.AreEqual ("linux", linuxPackage.Archives [0].HostOs);

			var windowsPackage = manifest.ResolvePackage ("cmdline-tools;20.0", "windows");
			Assert.IsNotNull (windowsPackage);
			Assert.AreEqual (1, windowsPackage.Archives.Count);
			Assert.AreEqual ("windows", windowsPackage.Archives [0].HostOs);

			var macPackage = manifest.ResolvePackage ("cmdline-tools;20.0", "macosx");
			Assert.IsNotNull (macPackage);
			Assert.AreEqual (1, macPackage.Archives.Count);
			Assert.AreEqual ("macosx", macPackage.Archives [0].HostOs);
		}

		[Test]
		public void ResolvePackage_NotFound ()
		{
			var manifest = new ManifestFeed (TestLogger);

			var package = manifest.ResolvePackage ("nonexistent");
			Assert.IsNull (package);
		}

		[Test]
		public void GetJdkVersions ()
		{
			var manifest = new ManifestFeed (TestLogger);
			manifest.Packages.Add (new PackageInfo { Path = "jdk-17" });
			manifest.Packages.Add (new PackageInfo { Path = "openjdk-11" });
			manifest.Packages.Add (new PackageInfo { Path = "cmdline-tools;20.0" });
			manifest.Packages.Add (new PackageInfo { Path = "java-sdk-8" });

			var jdks = manifest.GetJdkVersions ();
			Assert.AreEqual (3, jdks.Count);
			Assert.IsTrue (jdks.Any (p => p.Path == "jdk-17"));
			Assert.IsTrue (jdks.Any (p => p.Path == "openjdk-11"));
			Assert.IsTrue (jdks.Any (p => p.Path == "java-sdk-8"));
			Assert.IsFalse (jdks.Any (p => p.Path == "cmdline-tools;20.0"));
		}

		[Test]
		public void GetJdkPackages ()
		{
			var manifest = new ManifestFeed (TestLogger);
			manifest.Packages.Add (new PackageInfo { Path = "microsoft-jdk-17", Version = new Version (17, 0, 1) });
			manifest.Packages.Add (new PackageInfo { Path = "temurin-jdk-11", Version = new Version (11, 0, 2) });
			manifest.Packages.Add (new PackageInfo { Path = "cmdline-tools;20.0" });

			var jdks = manifest.GetJdkPackages ();
			Assert.AreEqual (2, jdks.Count);
			
			var microsoft = jdks.FirstOrDefault (j => j.VendorId == "microsoft");
			Assert.IsNotNull (microsoft);
			Assert.AreEqual (17, microsoft.GetMajorVersion ());

			var adoptium = jdks.FirstOrDefault (j => j.VendorId == "adoptium");
			Assert.IsNotNull (adoptium);
			Assert.AreEqual (11, adoptium.GetMajorVersion ());
		}

		[Test]
		public void GetJdkPackagesByVendor ()
		{
			var manifest = new ManifestFeed (TestLogger);
			manifest.Packages.Add (new PackageInfo { Path = "microsoft-jdk-17" });
			manifest.Packages.Add (new PackageInfo { Path = "microsoft-jdk-21" });
			manifest.Packages.Add (new PackageInfo { Path = "temurin-jdk-11" });

			var msJdks = manifest.GetJdkPackagesByVendor ("microsoft");
			Assert.AreEqual (2, msJdks.Count);

			var adoptiumJdks = manifest.GetJdkPackagesByVendor ("adoptium");
			Assert.AreEqual (1, adoptiumJdks.Count);
		}

		[Test]
		public void GetJdkPackagesByMajorVersion ()
		{
			var manifest = new ManifestFeed (TestLogger);
			manifest.Packages.Add (new PackageInfo { Path = "microsoft-jdk-17", Version = new Version (17, 0, 1) });
			manifest.Packages.Add (new PackageInfo { Path = "temurin-jdk-17", Version = new Version (17, 0, 2) });
			manifest.Packages.Add (new PackageInfo { Path = "microsoft-jdk-21", Version = new Version (21, 0, 0) });

			var jdk17s = manifest.GetJdkPackagesByMajorVersion (17);
			Assert.AreEqual (2, jdk17s.Count);

			var jdk21s = manifest.GetJdkPackagesByMajorVersion (21);
			Assert.AreEqual (1, jdk21s.Count);
		}

		[Test]
		public void VerifyChecksum_ValidFile ()
		{
			var tempFile = Path.Combine (Path.GetTempPath (), $"test-checksum-{Guid.NewGuid ()}.txt");
			try {
				File.WriteAllText (tempFile, "Hello World");

				// SHA-1 of "Hello World" is 0a4d55a8d778e5022fab701977c5d840bbc486d0
				var result = ManifestFeed.VerifyChecksum (tempFile, "0a4d55a8d778e5022fab701977c5d840bbc486d0");
				Assert.IsTrue (result);

				// Test with wrong checksum
				result = ManifestFeed.VerifyChecksum (tempFile, "0000000000000000000000000000000000000000");
				Assert.IsFalse (result);
			} finally {
				if (File.Exists (tempFile))
					File.Delete (tempFile);
			}
		}

		[Test]
		public void VerifyChecksum_EmptyChecksum ()
		{
			var tempFile = Path.Combine (Path.GetTempPath (), $"test-checksum-{Guid.NewGuid ()}.txt");
			try {
				File.WriteAllText (tempFile, "Hello World");

				var result = ManifestFeed.VerifyChecksum (tempFile, "");
				Assert.IsFalse (result);

				result = ManifestFeed.VerifyChecksum (tempFile, null);
				Assert.IsFalse (result);
			} finally {
				if (File.Exists (tempFile))
					File.Delete (tempFile);
			}
		}

		[Test]
		public void LoadFromFile_XamarinManifest ()
		{
			// Create a minimal Xamarin manifest JSON
			var json = @"{
  ""packages"": [
    {
      ""id"": ""jdk-17"",
      ""displayName"": ""OpenJDK 17"",
      ""version"": ""17.0.0"",
      ""packageType"": ""jdk"",
      ""archives"": [
        {
          ""url"": ""https://example.com/jdk-17-linux.tar.gz"",
          ""sha1"": ""abc123def456"",
          ""size"": 123456789,
          ""os"": ""linux"",
          ""arch"": ""x86_64"",
          ""hostBits"": 64
        }
      ]
    }
  ]
}";

			var tempFile = Path.Combine (Path.GetTempPath (), $"test-manifest-{Guid.NewGuid ()}.json");
			try {
				File.WriteAllText (tempFile, json);

				var manifest = ManifestFeed.LoadFromFile (tempFile, TestLogger);

				Assert.IsNotNull (manifest);
				Assert.AreEqual (1, manifest.Packages.Count);

				var package = manifest.Packages [0];
				Assert.AreEqual ("jdk-17", package.Path);
				Assert.AreEqual ("OpenJDK 17", package.DisplayName);
				Assert.AreEqual (new Version (17, 0, 0), package.Version);
				Assert.AreEqual ("jdk", package.PackageType);
				Assert.AreEqual (1, package.Archives.Count);

				var archive = package.Archives [0];
				Assert.AreEqual ("https://example.com/jdk-17-linux.tar.gz", archive.Url);
				Assert.AreEqual ("abc123def456", archive.Checksum);
				Assert.AreEqual ("sha1", archive.ChecksumType);
				Assert.AreEqual (123456789, archive.Size);
				Assert.AreEqual ("linux", archive.HostOs);
				Assert.AreEqual ("x86_64", archive.Arch);
				Assert.AreEqual ((uint) 64, archive.HostBits);
			} finally {
				if (File.Exists (tempFile))
					File.Delete (tempFile);
			}
		}
	}
}
