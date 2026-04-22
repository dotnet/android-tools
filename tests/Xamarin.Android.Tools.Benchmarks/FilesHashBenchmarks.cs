using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Microsoft.Android.Build.Tasks;

namespace Xamarin.Android.Tools.Benchmarks;

[MemoryDiagnoser]
public class FilesHashBenchmarks
{
	const int OneMB = 1024 * 1024;

	byte [] data = Array.Empty<byte> ();
	MemoryStream stream = new MemoryStream ();
	string readmeFile = string.Empty;
	string tempFile1 = string.Empty;
	string tempFile2 = string.Empty;

	[GlobalSetup]
	public void Setup ()
	{
		// 1MB byte array with reproducible random data
		data = new byte [OneMB];
		new Random (42).NextBytes (data);
		stream = new MemoryStream (data);

		// Walk up from output directory to find README.md
		var dir = AppContext.BaseDirectory;
		while (dir is not null) {
			var candidate = Path.Combine (dir, "README.md");
			if (File.Exists (candidate)) {
				readmeFile = candidate;
				break;
			}
			dir = Path.GetDirectoryName (dir);
		}
		if (string.IsNullOrEmpty (readmeFile))
			throw new FileNotFoundException ("Could not find README.md in any parent directory.");

		// Two identical temp files for HasFileChanged benchmark
		tempFile1 = Path.GetTempFileName ();
		tempFile2 = Path.GetTempFileName ();
		File.WriteAllBytes (tempFile1, data);
		File.WriteAllBytes (tempFile2, data);
	}

	[GlobalCleanup]
	public void Cleanup ()
	{
		stream.Dispose ();
		if (File.Exists (tempFile1))
			File.Delete (tempFile1);
		if (File.Exists (tempFile2))
			File.Delete (tempFile2);
	}

	[Benchmark]
	public string HashBytes () => Files.HashBytes (data);

	[Benchmark]
	public string HashStream ()
	{
		stream.Position = 0;
		return Files.HashStream (stream);
	}

	[Benchmark]
	public string HashFile () => Files.HashFile (readmeFile);

	[Benchmark]
	public bool HasFileChanged () => Files.HasFileChanged (tempFile1, tempFile2);
}
