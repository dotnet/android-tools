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
	string tempFile1 = string.Empty;
	string tempFile2 = string.Empty;

	[GlobalSetup]
	public void Setup ()
	{
		// 1MB byte array with reproducible random data
		data = new byte [OneMB];
		new Random (42).NextBytes (data);
		stream = new MemoryStream (data);

		// Two identical 1MB temp files
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
	public string HashFile () => Files.HashFile (tempFile1);

	[Benchmark]
	public bool HasFileChanged () => Files.HasFileChanged (tempFile1, tempFile2);
}
