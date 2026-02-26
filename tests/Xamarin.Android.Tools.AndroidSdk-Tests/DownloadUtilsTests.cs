// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using NUnit.Framework;

namespace Xamarin.Android.Tools.Tests
{
	[TestFixture]
	public class DownloadUtilsTests
	{
		[Test]
		public void ParseChecksumFile_Null_ReturnsNull ()
		{
			Assert.IsNull (DownloadUtils.ParseChecksumFile (null!));
		}

		[Test]
		public void ParseChecksumFile_Empty_ReturnsNull ()
		{
			Assert.IsNull (DownloadUtils.ParseChecksumFile (""));
		}

		[Test]
		public void ParseChecksumFile_WhitespaceOnly_ReturnsNull ()
		{
			Assert.IsNull (DownloadUtils.ParseChecksumFile ("   \n\t  "));
		}

		[Test]
		public void ParseChecksumFile_HashOnly ()
		{
			Assert.AreEqual ("abc123def456", DownloadUtils.ParseChecksumFile ("abc123def456"));
		}

		[Test]
		public void ParseChecksumFile_HashOnly_WithTrailingNewline ()
		{
			Assert.AreEqual ("abc123def456", DownloadUtils.ParseChecksumFile ("abc123def456\n"));
		}

		[Test]
		public void ParseChecksumFile_HashAndFilename ()
		{
			// Standard sha256sum format: "<hash>  <filename>"
			Assert.AreEqual ("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
				DownloadUtils.ParseChecksumFile ("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  microsoft-jdk-21-linux-x64.tar.gz"));
		}

		[Test]
		public void ParseChecksumFile_HashAndFilename_WithTab ()
		{
			Assert.AreEqual ("abc123", DownloadUtils.ParseChecksumFile ("abc123\tfilename.zip"));
		}

		[Test]
		public void ParseChecksumFile_MultipleLines_ReturnsFirstHash ()
		{
			var content = "abc123  file1.zip\ndef456  file2.zip\n";
			Assert.AreEqual ("abc123", DownloadUtils.ParseChecksumFile (content));
		}

		[Test]
		public void ParseChecksumFile_LeadingAndTrailingWhitespace ()
		{
			Assert.AreEqual ("abc123", DownloadUtils.ParseChecksumFile ("  abc123  filename.zip  \n"));
		}

		[TestCase ("abc123\r\n")]
		[TestCase ("abc123\r")]
		[TestCase ("abc123\n")]
		public void ParseChecksumFile_VariousLineEndings (string content)
		{
			Assert.AreEqual ("abc123", DownloadUtils.ParseChecksumFile (content));
		}
	}
}
