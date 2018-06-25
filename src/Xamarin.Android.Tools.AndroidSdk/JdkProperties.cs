using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Xamarin.Android.Tools
{
	public class JdkProperties
	{
		public string Vendor { get; set; }

		public string Version { get; set; }
	
		public static JdkProperties Get(string jsdkPath=null)
		{
			var props = new JdkProperties();
			
			var javaPath = GetJavaPath(jsdkPath);
			
			var processStartInfo = new ProcessStartInfo(javaPath, "-XshowSettings:properties -version");
			processStartInfo.CreateNoWindow = true;
			processStartInfo.RedirectStandardError = true;
			processStartInfo.RedirectStandardOutput = true;
			processStartInfo.UseShellExecute = false;

			var process = new Process();
			process.StartInfo = processStartInfo;

			process.OutputDataReceived += (sender, e) =>
			{
				ProcessData (props, e.Data);
			};

			process.ErrorDataReceived += (sender, e) =>
			{
				ProcessData (props, e.Data);
			};

			process.Start();

			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			process.WaitForExit(50);
			while (!process.HasExited)
				process.WaitForExit(50);

			return props;
		}

		static void ProcessData(JdkProperties props, string data)
		{
			try
			{
				if (data != null && data.Contains("java.vm.vendor"))
					props.Vendor = GetValue(data);
				else if (data != null && data.Contains("java.version"))
					props.Version = GetValue(data);

			} catch {
				// data was discarded
			}
		}

		static string GetValue(string data)
		{
			var start = data.IndexOf("= ") + 2;
			var end = data.Length;
			return data.Substring(start, end - start).Trim();
		}

		static string GetJavaPath(string javaSdkPath)
		{
			string java = "java";

			if (!string.IsNullOrEmpty(javaSdkPath))
			{
				java = Path.Combine(javaSdkPath, "bin");
				java = Path.Combine(java, "java");
				if (!File.Exists(java))
					java += ".exe";
				if (!File.Exists(java))
					java = "java";
			}

			return java;
		}
	}
}