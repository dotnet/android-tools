using System;
using System.IO;
using System.Text;

namespace Xamarin.Android.Tools;

class ProcessOutputFilterImpl
{
	public Func<string?, bool>? OnLineReceived { get; set; }

	public bool ProcessLine (string? line)
	{
		if (OnLineReceived == null) {
			return true;
		}

		return OnLineReceived (line);
	}
}

public class ProcessStringWriter : StringWriter
{
	ProcessOutputFilterImpl filter = new ProcessOutputFilterImpl ();

	/// <summary>
	/// A line filtering function.  Line is passed as a parameter and the return value of `true` means
	/// the line should be actually written to the writer, while `false` means the line will be discarded.
	/// </summary>
	public Func<string?, bool>? OnLineReceived {
		get => filter.OnLineReceived;
		set => filter.OnLineReceived = value;
	}

	public ProcessStringWriter ()
		: base ()
	{}

	public ProcessStringWriter (IFormatProvider? formatProvider)
		: base (formatProvider)
	{}

	public ProcessStringWriter (StringBuilder sb)
		: base (sb)
	{}

	public ProcessStringWriter (StringBuilder sb, IFormatProvider? formatProvider)
		: base (sb, formatProvider)
	{}

	public override void WriteLine (string? value)
	{
		if (!filter.ProcessLine (value)) {
			return;
		}

		base.WriteLine (value);
	}
}

public class ProcessStreamWriter : StringWriter
{
	ProcessOutputFilterImpl filter = new ProcessOutputFilterImpl ();

	/// <summary>
	/// A line filtering function.  Line is passed as a parameter and the return value of `true` means
	/// the line should be actually written to the writer, while `false` means the line will be discarded.
	/// </summary>
	public Func<string?, bool>? OnLineReceived {
		get => filter.OnLineReceived;
		set => filter.OnLineReceived = value;
	}

	public ProcessStreamWriter (IFormatProvider? formatProvider)
		: base (formatProvider)
	{}

	public ProcessStreamWriter (StringBuilder sb)
		: base (sb)
	{}

	public ProcessStreamWriter (StringBuilder sb, IFormatProvider? formatProvider)
		: base (sb, formatProvider)
	{}

	public override void WriteLine (string? value)
	{
		if (!filter.ProcessLine (value)) {
			return;
		}

		base.WriteLine (value);
	}
}

public class NoProcesssOutputWriter : StringWriter
{
	ProcessOutputFilterImpl filter = new ProcessOutputFilterImpl ();

	/// <summary>
	/// A line filtering function.  Line is passed as a parameter and the return value of `true` means
	/// the line should be actually written to the writer, while `false` means the line will be discarded.
	/// </summary>
	public Func<string?, bool>? OnLineReceived {
		get => filter.OnLineReceived;
		set => filter.OnLineReceived = value;
	}

	public NoProcesssOutputWriter ()
		: base ()
	{}

	public override void WriteLine (string? value)
	{
		filter.ProcessLine (value);
	}
}
