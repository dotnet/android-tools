﻿using System;
using System.IO;
using System.Text;

using NUnit.Framework;

namespace Xamarin.Android.Tools.Tests
{
	[TestFixture]
	public class AndroidVersionTests
	{
		[Test]
		public void Constructor_Exceptions ()
		{
			Assert.Throws<ArgumentNullException> (() => new AndroidVersion (0, null));
			Assert.Throws<ArgumentException> (() => new AndroidVersion (0, "not a number"));
		}

		[Test]
		public void Constructor ()
		{
			var v   = new AndroidVersion (apiLevel: 1, osVersion: "2.3", codeName: "Four", id: "E", stable: false);
			Assert.AreEqual (1,                     v.ApiLevel);
			Assert.AreEqual ("E",                   v.Id);
			Assert.AreEqual ("Four",                v.CodeName);
			Assert.AreEqual ("2.3",                 v.OSVersion);
			Assert.AreEqual (new Version (2, 3),    v.TargetFrameworkVersion);
			Assert.AreEqual ("v2.3",                v.FrameworkVersion);
			Assert.AreEqual (false,                 v.Stable);
		}

		[Test]
		public void Load_NoFile ()
		{
			Assert.Throws<ArgumentNullException> (() => AndroidVersion.Load ((string) null));

			var p   = Path.GetTempFileName ();
			File.Delete (p);
			Assert.Throws<FileNotFoundException> (() => AndroidVersion.Load (p));
		}

		[Test]
		public void Load_NoStream ()
		{
			Assert.Throws<ArgumentNullException> (() => AndroidVersion.Load ((Stream) null));
		}

		[Test]
		public void Load ()
		{
			var xml = @"<AndroidApiInfo>
  <Id>O</Id>
  <Level>26</Level>
  <Name>Android O</Name>
  <Version>v7.99.0</Version>
  <Stable>False</Stable>
</AndroidApiInfo>";
			var v   = AndroidVersion.Load (new MemoryStream (Encoding.UTF8.GetBytes (xml)));
			Assert.AreEqual (26,                        v.ApiLevel);
			Assert.AreEqual ("O",                       v.Id);
			Assert.AreEqual ("Android O",               v.CodeName);
			Assert.AreEqual ("7.99.0",                  v.OSVersion);
			Assert.AreEqual (new Version (7, 99, 0),    v.TargetFrameworkVersion);
			Assert.AreEqual ("v7.99.0",                 v.FrameworkVersion);
			Assert.AreEqual (false,                     v.Stable);
		}
	}
}
