// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   0x23 (Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace WaveDudeNext.Export;

/// <summary>
/// JPEG through the Windows Imaging Component (WIC), the codec that ships with Windows, called natively
/// (hand-written COM interfaces in the vtable order of <c>wincodec.h</c>, unused slots as placeholders) so
/// that its <c>JpegYCrCbSubsampling</c> option can be set to 4:4:4. WPF's <c>JpegBitmapEncoder</c> never
/// sets it, so it wrote 4:2:0 at every quality: the colour averaged over 2 × 2 pixels, which on a wave
/// picture, whose information is in the colour, showed as blocky fringes even at quality 100 (2026-10-18;
/// a 4:4:4 file at quality 90 beat the old quality 100 in every channel at half the size). The quality
/// 1..100 goes over as <c>ImageQuality</c> 0.01..1, the scale WPF used, so the number means the same as
/// before; 4:4:4 costs about half again per frame. The frame is written to the sink's stream through an
/// <see cref="IStream"/> adapter. Windows only: <see cref="ExportFormats"/> prefers <see cref="Encoder"/>
/// there, the in-house <see cref="JpegEncoder"/> is the fallback and the encoder elsewhere.
/// Reusable for frames of one size; one thread at a time.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WicJpegEncoder
{
	private const uint ClsctxInprocServer = 1;
	private const uint EncoderNoCache = 2; // WICBitmapEncoderNoCache
	private const byte Subsampling444 = 3; // WICJpegYCrCbSubsampling444
	private const ushort VtR4 = 4;
	private const ushort VtUi1 = 17;
	private const uint Propbag2TypeData = 1;

	private static readonly Guid ClsidImagingFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
	private static readonly Guid IidImagingFactory = new("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
	private static readonly Guid ContainerFormatJpeg = new("19e4a5aa-5662-4fc5-a0c0-1758028e1057");
	private static readonly Guid PixelFormat32bppBgr = new("6fddc324-4e03-4bfe-b185-3d77768dc90e");
	private static readonly Guid PixelFormat24bppBgr = new("6fddc324-4e03-4bfe-b185-3d77768dc90c");

	private readonly IWICImagingFactory _factory;
	private readonly float _imageQuality;
	private byte[]? _rgb;

	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="quality">1 (smallest file) to 100 (best picture).</param>
	/// <exception cref="COMException">WIC is not available.</exception>
	public WicJpegEncoder(int width, int height, int quality)
	{
		Width = width;
		Height = height;
		Quality = Math.Clamp(quality, 1, 100);
		_imageQuality = Quality / 100f;
		MediaFoundation.Check(CoCreateInstance(ClsidImagingFactory, IntPtr.Zero, ClsctxInprocServer, IidImagingFactory, out IntPtr factory));
		try
		{
			_factory = (IWICImagingFactory)Marshal.GetObjectForIUnknown(factory);
		}
		finally
		{
			Marshal.Release(factory);
		}
	}

	/// <summary>Frame width in pixels.</summary>
	public int Width { get; }

	/// <summary>Frame height in pixels.</summary>
	public int Height { get; }

	/// <summary>The quality 1..100 handed to the codec.</summary>
	public int Quality { get; }

	/// <summary>
	/// The writer <see cref="ExportFormats"/> prefers on Windows: an encoder for frames of one size and
	/// quality, made on the calling thread and used there. Throws when WIC is not available, which the sink
	/// logs before it falls back to the in-house encoder.
	/// </summary>
	/// <param name="width">Frame width in pixels.</param>
	/// <param name="height">Frame height in pixels.</param>
	/// <param name="quality">1..100.</param>
	public static Action<Stream, ReadOnlyMemory<int>> Encoder(int width, int height, int quality) => new WicJpegEncoder(width, height, quality).Write;

	/// <summary>Writes one frame as a 4:4:4 JPEG to <paramref name="stream"/> (left open), from its current position.</summary>
	/// <param name="stream">Destination; seeking is relative to where it stands.</param>
	/// <param name="pixels">Packed BGRA32 pixels, row-major, at least width × height.</param>
	public unsafe void Write(Stream stream, ReadOnlyMemory<int> pixels)
	{
		if (pixels.Length < Width * Height)
		{
			throw new ArgumentException("The frame is smaller than the picture.", nameof(pixels));
		}
		MediaFoundation.Check(_factory.CreateEncoder(ContainerFormatJpeg, IntPtr.Zero, out IWICBitmapEncoder encoder));
		IWICBitmapFrameEncode? frame = null;
		IPropertyBag2? options = null;
		try
		{
			MediaFoundation.Check(encoder.Initialize(new StreamAdapter(stream), EncoderNoCache));
			MediaFoundation.Check(encoder.CreateNewFrame(out frame, ref options));
			PROPBAG2[] names =
			[
				new() { dwType = Propbag2TypeData, vt = VtR4, pstrName = "ImageQuality" },
				new() { dwType = Propbag2TypeData, vt = VtUi1, pstrName = "JpegYCrCbSubsampling" },
			];
			Variant[] values =
			[
				new() { vt = VtR4, data = BitConverter.SingleToUInt32Bits(_imageQuality) },
				new() { vt = VtUi1, data = Subsampling444 },
			];
			MediaFoundation.Check(options!.Write((uint)names.Length, names, values));
			MediaFoundation.Check(frame.Initialize(options));
			MediaFoundation.Check(frame.SetSize((uint)Width, (uint)Height));
			Guid format = PixelFormat32bppBgr;
			MediaFoundation.Check(frame.SetPixelFormat(ref format));
			if (format == PixelFormat32bppBgr)
			{
				using MemoryHandle handle = pixels.Pin();
				MediaFoundation.Check(frame.WritePixels((uint)Height, (uint)(Width * 4), (uint)(Width * Height * 4), (IntPtr)handle.Pointer));
			}
			else if (format == PixelFormat24bppBgr)
			{
				// the codec asked for 24 bits per pixel: the alpha byte is dropped
				_rgb ??= new byte[Width * Height * 3];
				ReadOnlySpan<int> source = pixels.Span[..(Width * Height)];
				for (int i = 0; i < source.Length; i++)
				{
					_rgb[i * 3] = (byte)source[i];
					_rgb[i * 3 + 1] = (byte)(source[i] >> 8);
					_rgb[i * 3 + 2] = (byte)(source[i] >> 16);
				}
				fixed (byte* rgb = _rgb)
				{
					MediaFoundation.Check(frame.WritePixels((uint)Height, (uint)(Width * 3), (uint)_rgb.Length, (IntPtr)rgb));
				}
			}
			else
			{
				throw new NotSupportedException("The JPEG codec accepts neither 32- nor 24-bit BGR pixels.");
			}
			MediaFoundation.Check(frame.Commit());
			MediaFoundation.Check(encoder.Commit());
		}
		finally
		{
			if (frame != null)
			{
				Marshal.ReleaseComObject(frame);
			}
			if (options != null)
			{
				Marshal.ReleaseComObject(options);
			}
			Marshal.ReleaseComObject(encoder);
		}
	}

	/// <summary>Encodes one frame to a byte array.</summary>
	public byte[] Encode(ReadOnlyMemory<int> pixels)
	{
		using var stream = new MemoryStream();
		Write(stream, pixels);
		return stream.ToArray();
	}

	[DllImport("ole32.dll", ExactSpelling = true)]
	private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr obj);

	/// <summary>
	/// The sink's stream as the <see cref="IStream"/> the encoder writes to: positions are relative to where the
	/// stream stood when the adapter was made; the stream need not be readable (the sinks open theirs for
	/// writing only). Reading, cloning, copying and locking are not supported.
	/// </summary>
	private sealed class StreamAdapter(Stream stream) : IStream
	{
		private readonly long _start = stream.CanSeek ? stream.Position : 0;

		public void Read(byte[] pv, int cb, IntPtr pcbRead) => throw new NotSupportedException();

		public void Write(byte[] pv, int cb, IntPtr pcbWritten)
		{
			stream.Write(pv, 0, cb);
			if (pcbWritten != IntPtr.Zero)
			{
				Marshal.WriteInt32(pcbWritten, cb);
			}
		}

		public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
		{
			long position = dwOrigin switch
			{
				0 => _start + dlibMove, // STREAM_SEEK_SET
				1 => stream.Position + dlibMove, // STREAM_SEEK_CUR
				2 => stream.Length + dlibMove, // STREAM_SEEK_END
				_ => throw new ArgumentOutOfRangeException(nameof(dwOrigin)),
			};
			if (position != stream.Position)
			{
				stream.Position = position;
			}
			if (plibNewPosition != IntPtr.Zero)
			{
				Marshal.WriteInt64(plibNewPosition, position - _start);
			}
		}

		public void SetSize(long libNewSize) => stream.SetLength(_start + libNewSize);

		public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => throw new NotSupportedException();

		public void Commit(int grfCommitFlags) => stream.Flush();

		public void Revert() => throw new NotSupportedException();

		public void LockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();

		public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new NotSupportedException();

		public void Stat(out STATSTG pstatstg, int grfStatFlag) => pstatstg = new STATSTG { type = 2 /* STGTY_STREAM */, cbSize = stream.CanSeek ? stream.Length - _start : 0 };

		public void Clone(out IStream ppstm) => throw new NotSupportedException();
	}

	/// <summary><c>PROPBAG2</c>: the name and type of one encoder option.</summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct PROPBAG2
	{
		public uint dwType;
		public ushort vt;
		public ushort cfType;
		public uint dwHint;
		[MarshalAs(UnmanagedType.LPWStr)] public string pstrName;
		public Guid clsid;
	}

	/// <summary>A <c>VARIANT</c> as 24 bytes: the type, three reserved words, the value in the first bytes of the union.</summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct Variant
	{
		public ushort vt;
		public ushort reserved1;
		public ushort reserved2;
		public ushort reserved3;
		public ulong data;
		public ulong data2;
	}

	[ComImport]
	[Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IWICImagingFactory
	{
		[PreserveSig] int CreateDecoderFromFilename();
		[PreserveSig] int CreateDecoderFromStream();
		[PreserveSig] int CreateDecoderFromFileHandle();
		[PreserveSig] int CreateComponentInfo();
		[PreserveSig] int CreateDecoder();
		[PreserveSig] int CreateEncoder(in Guid containerFormat, IntPtr vendor, out IWICBitmapEncoder encoder);
	}

	[ComImport]
	[Guid("00000103-a8f2-4877-ba0a-fd2b6645fb94")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IWICBitmapEncoder
	{
		[PreserveSig] int Initialize(IStream stream, uint cacheOption);
		[PreserveSig] int GetContainerFormat();
		[PreserveSig] int GetEncoderInfo();
		[PreserveSig] int SetColorContexts();
		[PreserveSig] int SetPalette();
		[PreserveSig] int SetThumbnail();
		[PreserveSig] int SetPreview();
		[PreserveSig] int CreateNewFrame(out IWICBitmapFrameEncode frame, ref IPropertyBag2? options);
		[PreserveSig] int Commit();
	}

	[ComImport]
	[Guid("00000105-a8f2-4877-ba0a-fd2b6645fb94")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IWICBitmapFrameEncode
	{
		[PreserveSig] int Initialize(IPropertyBag2? options);
		[PreserveSig] int SetSize(uint width, uint height);
		[PreserveSig] int SetResolution();
		[PreserveSig] int SetPixelFormat(ref Guid format);
		[PreserveSig] int SetColorContexts();
		[PreserveSig] int SetPalette();
		[PreserveSig] int SetThumbnail();
		[PreserveSig] int WritePixels(uint lineCount, uint stride, uint bufferSize, IntPtr pixels);
		[PreserveSig] int WriteSource();
		[PreserveSig] int Commit();
	}

	[ComImport]
	[Guid("22f55882-280b-11d0-a8a9-00a0c90c2004")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPropertyBag2
	{
		[PreserveSig] int Read();
		[PreserveSig] int Write(uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] PROPBAG2[] properties, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Variant[] values);
	}
}
